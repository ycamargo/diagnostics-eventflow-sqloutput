using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.Diagnostics.EventFlow.Configuration;
using Microsoft.Diagnostics.EventFlow.Metadata;
using Microsoft.Diagnostics.EventFlow.Outputs.EventDataParsers;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Diagnostics.EventFlow.Outputs
{
    public class SqlTableOutput : IOutput
    {
        private const string SQLPROVIDER = "System.Data.SqlClient";
        private const string SQLCOMMANDTEMPLATE = "INSERT INTO {0} ({1}) VALUES ({2});";
        private const string PARM_PREFIX = "@parm";
        private const string QUOTE = "\"";
        private const string COMMA_SEP = ", ";
        private const char COLON_SEP = ':';

        private readonly IHealthReporter _healthReporter;

        private SqlTableOutputConfiguration Configuration { get; set; }
        private ConnectionStringSettings ConnectionSettings { get; set; }
        private string SqlCommandText { get; set; }
        private IList<SqlParameter> SqlCommandParameters { get; set; }

        public SqlTableOutput(IConfiguration configuration, IHealthReporter healthReporter)
        {
            _healthReporter = healthReporter;

            Configuration = new SqlTableOutputConfiguration();
            try
            {
                configuration.Bind(Configuration);
                Initialize();
            }
            catch
            {
                _healthReporter.ReportProblem($"{nameof(SqlTableOutput)}: Invalid {nameof(SqlTableOutput)} configuration encountered: '{configuration.ToString()}'",
                   EventFlowContextIdentifiers.Configuration);
                throw;
            }
        }

        private void Initialize()
        {
            if (string.IsNullOrEmpty(Configuration.ConnectionString) && string.IsNullOrEmpty(Configuration.ConnectionStringName))
                throw new ArgumentNullException($"No connection strings are configured.", nameof(Configuration.ConnectionString));

            if (string.IsNullOrEmpty(Configuration.LogTableName))
                throw new ArgumentNullException($"No log destination table is configured.", nameof(Configuration.LogTableName));

            // ConnectionString has priority over ConnectionStringName
            if (!string.IsNullOrEmpty(Configuration.ConnectionString))
            {
                ConnectionSettings = new ConnectionStringSettings(nameof(SqlTableOutput), Configuration.ConnectionString);
            }
            else
            {
                // read connection string and provider factory from the configuration file
                var cs = ConfigurationManager.ConnectionStrings[Configuration.ConnectionStringName];
                ConnectionSettings = cs ?? throw new ArgumentException($"Connection string '{Configuration.ConnectionStringName}' is not declared in <connectionStrings /> section.", nameof(Configuration.ConnectionStringName));
            }

            // For now, only Sql Server is accepted
            if (!ConnectionSettings.ProviderName.Equals(SQLPROVIDER))
                throw new ArgumentException($"Connection string '{Configuration.ConnectionStringName}' uses a different provider than {SQLPROVIDER}.", nameof(Configuration.ConnectionStringName));

            // reset command data
            SqlCommandText = null;
            SqlCommandParameters = null;
        }

        public async Task SendEventsAsync(IReadOnlyCollection<EventData> events, long transmissionSequenceNumber, CancellationToken cancellationToken)
        {
            // Use a "Supressed" transaction scope to not be affected by callers rolling backs
            using (var scope = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
            {
                try
                {
                    using (SqlConnection connection = new SqlConnection(ConnectionSettings.ConnectionString))
                    {
                        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                        using (SqlCommand command = CreateCommand(connection))
                        {
                            var transactionName = String.Concat(nameof(SqlTableOutput), transmissionSequenceNumber);

                            using (command.Transaction = connection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted, transactionName))
                            {
                                try
                                {
                                    var inserted = 0;
                                    command.Prepare();
#if DEBUG
                                    Console.WriteLine($"{nameof(SqlTableOutput)}: will try to insert #{events.Count} events into SQL Server database");
#endif
                                    // Execute insert for each row in table
                                    foreach (var row in CreateEventsDataTable(events))
                                    {
                                        foreach (var col in row)
                                            command.Parameters[String.Concat(PARM_PREFIX, col.Key)].Value = col.Value ?? DBNull.Value;

                                        if (cancellationToken.IsCancellationRequested)
                                            return;

                                        var result = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                                        command.Transaction.Save(transactionName);
                                        inserted += result;
                                    }
                                    command.Transaction.Commit();
                                    _healthReporter.ReportHealthy($"{nameof(SqlTableOutput)}: #{inserted} events written to SQL Server database", EventFlowContextIdentifiers.Output);
#if DEBUG
                                    Console.WriteLine($"{nameof(SqlTableOutput)}: #{inserted} events written to SQL Server database");
#endif
                                }
                                catch (Exception sqlex)
                                {
                                    _healthReporter.ReportWarning($"{nameof(SqlTableOutput)}: error writing data to SQL database. {sqlex.Message}", EventFlowContextIdentifiers.Output);
#if DEBUG
                                    Console.WriteLine($"{nameof(SqlTableOutput)}: error writing data to SQL database. {sqlex.Message}");
                                    Console.WriteLine(sqlex.StackTrace);
#endif

                                    try
                                    {
                                        command.Transaction.Rollback(transactionName);
                                        connection.Close();
                                    }
                                    catch (Exception rex)
                                    {
                                        _healthReporter.ReportWarning($"{nameof(SqlTableOutput)}: error rolling back. {rex.Message}", EventFlowContextIdentifiers.Output);
#if DEBUG
                                        Console.WriteLine($"{nameof(SqlTableOutput)}: error rolling back. {rex.Message}");
                                        Console.WriteLine(rex.StackTrace);
#endif
                                    }
                                    throw;
                                }
                            }
                        }
                        connection.Close();
                    }

                    // Not really needed, since the scope is "Suppressed"
                    scope.Complete();
                }
                catch (Exception ex)
                {
                    _healthReporter.ReportProblem($"{nameof(SqlTableOutput)}: write of events data to SQL Server database has failed. {ex.Message}", EventFlowContextIdentifiers.Output);
#if DEBUG
                    Console.WriteLine($"{nameof(SqlTableOutput)}: write of events data to SQL Server database has failed. {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
#endif
                }
            }
        }

        private IList<IDictionary<string, object>> CreateEventsDataTable(IReadOnlyCollection<EventData> events)
        {
            // Parse received events creating a List of EventDataParsed (and its subclasses)
            List<EventDataParsed> parsedEvents = new List<EventDataParsed>();
            foreach (EventData eventData in events)
            {
                parsedEvents.AddRange(GetCollectionOfParsedEvents(eventData));
            }

            // Prepare Collection of values for inserting
            var table = new List<IDictionary<string, object>>();
            foreach (var item in parsedEvents)
            {
                var row = new Dictionary<string, object>();

                // Get all properties from EventDataParsed (or its subclasses)
                var props = TypeDescriptor.GetProperties(item.GetType()).Cast<PropertyDescriptor>();

                foreach (var col in Configuration.SqlColumnTypes)
                {
                    if (Configuration.ColumnMappings.TryGetValue(col.Key, out string colMapping))
                    {
                        // Read the mapped property
                        var prop = props.FirstOrDefault(propertyInfo => propertyInfo.Name.Equals(colMapping));
                        row.Add(col.Key, prop?.GetValue(item));
                    }
                    else if (Configuration.CustomColumnMappings.TryGetValue(col.Key, out colMapping))
                    {
                        // Read the mapped custom property
                        row.Add(col.Key, item.CustomProperties[colMapping]);
                    }
                    else
                    {
                        // if hasn't a mapping configuration, adds a null value
                        row.Add(col.Key, null);
                    }
                }

                table.Add(row);
            }

            return table;
        }

        private SqlCommand CreateCommand(SqlConnection connection)
        {
            // If command data isn't initialized, do it
            if (String.IsNullOrEmpty(SqlCommandText))
            {
                var sqlFields = new StringBuilder();
                var sqlParms = new StringBuilder();
                var parms = new List<SqlParameter>();

                // Read configuration of Column SQl Types and create:
                // the parameterized INSERT command and the Command parameters
                foreach (var col in Configuration.SqlColumnTypes)
                {
                    // Create column identifiers part of the INSERT string
                    // put all column names between quotes (") avoiding name and reserved words conflicts
                    sqlFields.Append(QUOTE);
                    sqlFields.Append(col.Key);
                    sqlFields.Append(QUOTE);
                    sqlFields.Append(COMMA_SEP);

                    // Create parameter identifiers part of the INSERT string
                    sqlParms.Append(PARM_PREFIX);
                    sqlParms.Append(col.Key);
                    sqlParms.Append(COMMA_SEP);

                    // Create Parameters
                    var type = col.Value;
                    var size = 0;
                    var scale = (byte)0;
                    var precision = (byte)0;

                    // If has Size or Scale & Precision
                    var parts = col.Value.Split(COLON_SEP);
                    if (parts.Length > 1)
                    {
                        type = parts[0];
                        try
                        {
                            // Size is set
                            if (parts.Length == 2)
                                size = Convert.ToInt32(parts[1]);

                            // Scale & Precision are set
                            if (parts.Length == 3)
                            {
                                scale = Convert.ToByte(parts[1]);
                                precision = Convert.ToByte(parts[2]);
                            }
                        }
                        catch (Exception ex)
                        {
                            _healthReporter.ReportWarning($"{nameof(SqlTableOutput)}: configuration of SQL column size '{col.Key}:{col.Value}' is invalid, will use 0 instead. {ex.Message}", EventFlowContextIdentifiers.Configuration);
                        }
                    }

                    SqlDbType parmType = SqlDbType.Variant;
                    try
                    {
                        // try to get a valid SqlDbType enumeration item
                        parmType = (SqlDbType)Enum.Parse(typeof(SqlDbType), type);
                    }
                    catch (Exception ex)
                    {
                        _healthReporter.ReportWarning($"{nameof(SqlTableOutput)}: configuration of SQL column type for '{col.Key}:{col.Value}' is invalid, will use Variant instead. {ex.Message}", EventFlowContextIdentifiers.Configuration);
                    }

                    var parm = new SqlParameter()
                    {
                        ParameterName = String.Concat(PARM_PREFIX, col.Key),
                        SqlDbType = parmType,
                        Direction = ParameterDirection.Input,
                        IsNullable = true,
                        Size = size,
                        Scale = scale,
                        Precision = precision
                    };

                    parms.Add(parm);
                }

                SqlCommandText = String.Format(SQLCOMMANDTEMPLATE, Configuration.LogTableName, sqlFields.ToString(0, sqlFields.Length - COMMA_SEP.Length), sqlParms.ToString(0, sqlParms.Length - COMMA_SEP.Length));
                SqlCommandParameters = parms;
            }

            // Create command and associate it to the Connection, the INSERT command and the parameters array
            SqlCommand command = new SqlCommand
            {
                CommandType = CommandType.Text,
                CommandText = SqlCommandText,
                Connection = connection,
            };
            // Copy parameters from properties, since each new Command requires its own Parameters instances
            command.Parameters.AddRange(DeepCopyParameters());

            return command;
        }

        private IEnumerable<EventDataParsed> GetCollectionOfParsedEvents(EventData eventData)
        {
            bool reportedAsSpecialEvent = false;
            EventDataParsed row;

            // Synthesize a separate record for each metric, request, dependency and exception metadata associated with the event

            if (eventData.TryGetMetadata(MetricData.MetricMetadataKind, out IReadOnlyCollection<EventMetadata> metadataSet))
            {
                foreach (var metricMetadata in metadataSet)
                {
                    row = ParseMetricEvent(eventData, metricMetadata);
                    if (row != null)
                    {
                        reportedAsSpecialEvent = true;
                        yield return row;
                    }
                }
            }
            else if (eventData.TryGetMetadata(RequestData.RequestMetadataKind, out metadataSet))
            {
                foreach (var requestMetadata in metadataSet)
                {
                    row = ParseRequestEvent(eventData, requestMetadata);
                    if (row != null)
                    {
                        reportedAsSpecialEvent = true;
                        yield return row;
                    }
                }
            }
            else if (eventData.TryGetMetadata(DependencyData.DependencyMetadataKind, out metadataSet))
            {
                foreach (var dependencyMetadata in metadataSet)
                {
                    row = ParseDependencyEvent(eventData, dependencyMetadata);
                    if (row != null)
                    {
                        reportedAsSpecialEvent = true;
                        yield return row;
                    }
                }
            }
            else if (eventData.TryGetMetadata(ExceptionData.ExceptionMetadataKind, out metadataSet))
            {
                foreach (var exceptionMetadata in metadataSet)
                {
                    row = ParseExceptionEvent(eventData, exceptionMetadata);
                    if (row != null)
                    {
                        reportedAsSpecialEvent = true;
                        yield return row;
                    }
                }
            }

            if (!reportedAsSpecialEvent)
            {
                row = new EventDataParsed(eventData, ParseEventCustomProperties(eventData));
                yield return row;
            }
        }

        private EventDataParsed ParseMetricEvent(EventData eventData, EventMetadata metricMetadata)
        {
            var result = MetricData.TryGetData(eventData, metricMetadata, out MetricData metricData);
            if (result.Status != DataRetrievalStatus.Success)
            {
                _healthReporter.ReportProblem($"{nameof(SqlTableOutput)}: {result.Message}", EventFlowContextIdentifiers.Output);
                return null;
            }

            var metricEventData = eventData.DeepClone();
            metricEventData.Payload[nameof(MetricData.MetricName)] = metricData.MetricName;
            metricEventData.Payload[nameof(MetricData.Value)] = metricData.Value;
            return new MetricDataParsed(metricEventData, ParseEventCustomProperties(eventData));
        }

        private EventDataParsed ParseRequestEvent(EventData eventData, EventMetadata requestMetadata)
        {
            var result = RequestData.TryGetData(eventData, requestMetadata, out RequestData requestData);
            if (result.Status != DataRetrievalStatus.Success)
            {
                _healthReporter.ReportProblem($"{nameof(SqlTableOutput)}: {result.Message}", EventFlowContextIdentifiers.Output);
                return null;
            }

            var requestEventData = eventData.DeepClone();
            requestEventData.Payload[nameof(RequestData.RequestName)] = requestData.RequestName;
            if (requestData.Duration != null)
            {
                requestEventData.Payload[nameof(RequestData.Duration)] = requestData.Duration;
            }
            if (requestData.IsSuccess != null)
            {
                requestEventData.Payload[nameof(RequestData.IsSuccess)] = requestData.IsSuccess;
            }
            if (requestData.ResponseCode != null)
            {
                requestEventData.Payload[nameof(RequestData.ResponseCode)] = requestData.ResponseCode;
            }
            return new RequestDataParsed(requestEventData, ParseEventCustomProperties(eventData));
        }

        private EventDataParsed ParseDependencyEvent(EventData eventData, EventMetadata dependencyMetadata)
        {
            var result = DependencyData.TryGetData(eventData, dependencyMetadata, out DependencyData dependencyData);
            if (result.Status != DataRetrievalStatus.Success)
            {
                _healthReporter.ReportProblem($"{nameof(SqlTableOutput)}: {result.Message}", EventFlowContextIdentifiers.Output);
                return null;
            }

            var dependencyEventData = eventData.DeepClone();
            if (dependencyData.Duration != null)
            {
                dependencyEventData.Payload[nameof(DependencyData.Duration)] = dependencyData.Duration;
            }
            if (dependencyData.IsSuccess != null)
            {
                dependencyEventData.Payload[nameof(DependencyData.IsSuccess)] = dependencyData.IsSuccess;
            }
            if (dependencyData.ResponseCode != null)
            {
                dependencyEventData.Payload[nameof(DependencyData.ResponseCode)] = dependencyData.ResponseCode;
            }
            if (dependencyData.Target != null)
            {
                dependencyEventData.Payload[nameof(DependencyData.Target)] = dependencyData.Target;
            }
            if (dependencyData.DependencyType != null)
            {
                dependencyEventData.Payload[nameof(DependencyData.DependencyType)] = dependencyData.DependencyType;
            }
            return new DependencyDataParsed(dependencyEventData, ParseEventCustomProperties(eventData));
        }

        private EventDataParsed ParseExceptionEvent(EventData eventData, EventMetadata exceptionMetadata)
        {
            var result = ExceptionData.TryGetData(eventData, exceptionMetadata, out ExceptionData exceptionData);
            if (result.Status != DataRetrievalStatus.Success)
            {
                _healthReporter.ReportProblem($"{nameof(SqlTableOutput)}: {result.Message}", EventFlowContextIdentifiers.Output);
                return null;
            }

            var exceptionEventData = eventData.DeepClone();
            exceptionEventData.Payload[nameof(ExceptionData.Exception)] = exceptionData.Exception.ToString();
            return new ExceptionDataParsed(exceptionEventData, ParseEventCustomProperties(eventData));
        }

        private IDictionary<string, string> ParseEventCustomProperties(EventData eventData)
        {
            IDictionary<string, string> customProperties = null;

            if (Configuration.CustomColumnMappings != null && Configuration.CustomColumnMappings.Count > 0)
            {
                customProperties = new Dictionary<string, string>();
                foreach (var col in Configuration.CustomColumnMappings)
                {
                    eventData.TryGetPropertyValue(col.Value, out object value);
                    customProperties.Add(col.Value, value?.ToString());
                }
            }

            return customProperties;
        }

        private SqlParameter[] DeepCopyParameters()
        {
            var newArray = new SqlParameter[SqlCommandParameters.Count];
            var i = 0;
            // Performs a deep copy on SqlCommandParameters
            foreach (var p in SqlCommandParameters)
            {
                newArray[i] = new SqlParameter()
                {
                    ParameterName = p.ParameterName,
                    SqlDbType = p.SqlDbType,
                    Direction = p.Direction,
                    IsNullable = p.IsNullable,
                    Size = p.Size,
                    Scale = p.Scale,
                    Precision = p.Precision
                };
                i++;
            }
            return newArray;
        }
    }
}

using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.EventFlow.Configuration
{
    public class SqlTableOutputConfiguration
    {
        /// <summary>
        /// Gets or sets the name of the connection string (as specified in <see href="http://msdn.microsoft.com/en-us/library/bf7sd233.aspx">&lt;connectionStrings&gt; configuration section</see>.
        /// </summary>
        public string ConnectionStringName { get; set; }

        /// <summary>
        /// Gets or sets the connection string. When provided, it overrides the value retrieved from <see cref="ConnectionStringName"/>
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// Gets or sets the name of the of the Log table to use as a destination for a BulkInsert
        /// </summary>
        public string LogTableName { get; set; }

        /// <summary>
        /// Gets or sets the collection of Sql column's data types
        /// Key="Sql table column name", Value="column data type (use SqlDbType enum values)"
        /// or
        /// Key="Sql table column name", Value="column data type (use SqlDbType enum values):column size (for variable lenght types)"
        /// or
        /// Key="Sql table column name", Value="column data type (use SqlDbType enum values):column scale:column precision (for decimal types)"
        /// </summary>
        public IDictionary<string, string> SqlColumnTypes { get; set; }

        /// <summary>
        /// Gets or sets the collection of column mappings to <see cref="EventDataParsed" /> classes and subclasses
        /// Key="Sql table column name", Value="EventParsedData (or subclasses) property"
        /// Possible values for <see cref="EventDataParsed" /> fields are:
        /// - LogDate, Level, LoggerClassName, LogMessage
        /// and for <see cref="ExceptionEventDataParsed" /> events:
        /// - ExceptionDescription
        /// and for <see cref="MetricEventDataParsed" /> events:
        /// - MetricName, MetricValue
        /// and for <see cref="RequestEventDataParsed" /> events:
        /// - RequestName, Duration, IsSuccess, ResponseCode
        /// and for <see cref="DependencyEventDataParsed" /> events:
        /// - Duration, IsSuccess, ResponseCode, Target, DependencyType
        /// </summary>
        public IDictionary<string, string> ColumnMappings { get; set; }

        /// <summary>
        /// Gets or sets the collection of custom column mappings to <see cref="EventDataParsed" /> classes and subclasses
        /// Key="Sql table column name", Value="EventData payload property"
        /// These columns must be Nullable on the database
        /// </summary>
        public IDictionary<string, string> CustomColumnMappings { get; set; }
    }
}

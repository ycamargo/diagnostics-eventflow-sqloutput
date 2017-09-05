# diagnostics-eventflow-sqloutput
Implementation of a Custom Output for Microsoft.Diagnostics.EventFlow to write log events to a Sql Server table

## Getting Started
1. To quickly get started with Microsoft.Diagnostics.EventFlow, see https://github.com/Azure/diagnostics-eventflow/blob/master/README.md 
2. Install the Microsoft.Diagnostics.EventFlow.Outputs.SqlTable nuget package
3. To use this Output to write your events/logs to a Sql Server table you need to edit your eventFlowConfig.json file. This file contains configuration for the EventFlow pipeline. To enable SqlTableOutput you need to add a reference to it on the "extensions" section and, also, add proper configurations of your table structure, Sql Server connection settings and table columns to EventData mappings into the "outputs" section of this file. See below, a sample configuration that takes input only from "Microsoft.Extensions.Logging" and route log data to 3 different outputs - StdOutput (Console), Application Insights (with a filter) and a Sql Server table called "ApplicationEvents":
```js
{
  "inputs": [
    {
      "type": "Microsoft.Extensions.Logging"
    }
  ],
  "filters": [],
  "outputs": [
    {
      "type": "StdOutput"
    },
    {
      "type": "ApplicationInsights",
      // insert a valid instrumentationKey.
      "instrumentationKey": "00000000-0000-0000-0000-000000000000",
      // Output only filter
      "filters": [
        {
          "type": "drop",
          "include": "Level == Verbose"
        }
      ]
    },
    {
      "type": "SqlTableOutput",

      "connectionStringName": "myApplicationConnectionStr",
      "logTableName": "ApplicationEvents",

      // IDENTITY columns MUST BE left out of this configuration
      "sqlColumnTypes:EventDate": "DateTime",
      "sqlColumnTypes:ProcessName": "NVarChar:255",
      "sqlColumnTypes:Provider": "NVarChar:255",
      "sqlColumnTypes:EventId": "Int",
      "sqlColumnTypes:Level": "NVarChar:20",
      "sqlColumnTypes:ExceptionData": "NVarChar:4000",
      "sqlColumnTypes:LogMessage": "NVarChar:4000",

      "columnMappings:EventDate": "LogDate",
      "columnMappings:Provider": "LoggerClassName",
      "columnMappings:Level": "Level",
      "columnMappings:ExceptionData": "ExceptionDescription",
      "columnMappings:LogMessage": "LogMessage",

      "customColumnMappings:ProcessName": "ProcessID",
      "customColumnMappings:EventId": "EventId"
    }
  ],
  "schemaVersion": "2016-08-11",
  "settings": {
    "pipelineBufferSize": "1000",
    "maxEventBatchSize": "50",
    "maxBatchDelayMsec": "500",
    "maxConcurrency": "10",
    "pipelineCompletionTimeoutMsec": "20000"
  },
  "healthReporter": {
    "type": "CsvHealthReporter",
    "logFileFolder": ".",
    "logFilePrefix": "EventFlow_HealthReport",
    "minReportLevel": "Message", // Message = Verbose, Warning, Error - default is Warning
    "throttlingPeriodMsec": "1000"
  },
  "extensions": [
    {
      "category": "outputFactory",
      "type": "SqlTableOutput",
      "qualifiedTypeName": "Microsoft.Diagnostics.EventFlow.Outputs.SqlTableOutputFactory, Microsoft.Diagnostics.EventFlow.Outputs.SqlTable"
    }
  ]
}
```

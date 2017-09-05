using System;
using Microsoft.Diagnostics.EventFlow.Metadata;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.EventFlow.Outputs.EventDataParsers
{
    public class EventDataParsed
    {
        public EventDataParsed(EventData eventData, IDictionary<string, string> customProperties = null)
        {
            LogDate = eventData.Timestamp.DateTime;
            Level = eventData.Level;
            LoggerClassName = eventData.ProviderName;

            object message;
            if (!eventData.TryGetPropertyValue("Message", out message))
                message = JsonConvert.SerializeObject(eventData);
            LogMessage = message.ToString();

            CustomProperties = customProperties;
        }

        public DateTime LogDate { get; set; }
        public LogLevel Level { get; set; }
        public string LoggerClassName { get; set; }
        public string LogMessage { get; set; }
        public IDictionary<string, string> CustomProperties { get; set; }
    }

    public class MetricDataParsed : EventDataParsed
    {
        public MetricDataParsed(EventData eventData, IDictionary<string, string> customProperties = null) : base(eventData, customProperties)
        {
            MetricName = eventData.Payload[nameof(MetricData.MetricName)].ToString();
            MetricValue = eventData.Payload[nameof(MetricData.Value)].ToString();
        }
        public string MetricName { get; set; }
        public string MetricValue { get; set; }
    }

    public class ExceptionDataParsed : EventDataParsed
    {
        public ExceptionDataParsed(EventData eventData, IDictionary<string, string> customProperties = null) : base(eventData, customProperties)
        {
            ExceptionDescription = eventData.Payload[nameof(ExceptionData.Exception)].ToString();
        }
        public string ExceptionDescription { get; set; }
    }

    public class RequestDataParsed : EventDataParsed
    {
        public RequestDataParsed(EventData eventData, IDictionary<string, string> customProperties = null) : base(eventData, customProperties)
        {
            RequestName = eventData.Payload[nameof(RequestData.RequestName)].ToString();
            Duration = eventData.Payload[nameof(RequestData.Duration)].ToString();
            IsSuccess = eventData.Payload[nameof(RequestData.IsSuccess)].ToString();
            ResponseCode = eventData.Payload[nameof(RequestData.ResponseCode)].ToString();
        }

        public string RequestName { get; set; }
        public string Duration { get; set; }
        public string IsSuccess { get; set; }
        public string ResponseCode { get; set; }
    }

    public class DependencyDataParsed : EventDataParsed
    {
        public DependencyDataParsed(EventData eventData, IDictionary<string, string> customProperties = null) : base(eventData, customProperties)
        {
            Duration = eventData.Payload[nameof(DependencyData.Duration)].ToString();
            IsSuccess = eventData.Payload[nameof(DependencyData.IsSuccess)].ToString();
            ResponseCode = eventData.Payload[nameof(DependencyData.ResponseCode)].ToString();
            Target = eventData.Payload[nameof(DependencyData.Target)].ToString();
            DependencyType = eventData.Payload[nameof(DependencyData.DependencyType)].ToString();
        }

        public string Duration { get; set; }
        public string IsSuccess { get; set; }
        public string ResponseCode { get; set; }
        public string Target { get; set; }
        public string DependencyType { get; set; }
    }
}

using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// Helper class for building OpenTelemetry protobuf objects in E2E tests.
/// Reduces code duplication by providing reusable builder methods with sensible defaults.
///
/// Design Note: This is a more comprehensive version compared to unit_tests/Helpers/TestBuilders.cs.
/// The duplication is intentional to keep test projects independent:
/// - TestBuilders (unit tests): Simpler, focused on unit test scenarios with minimal setup
/// - ProtobufBuilders (E2E tests): More comprehensive, with extension methods and flexible parameters
///
/// This class provides:
/// - Flexible builder methods with optional parameters for realistic test data
/// - Extension methods (WithAttributes) for fluent test setup
/// - Support for all signal types (traces, logs, metrics)
/// - Realistic defaults (proper timestamps, trace IDs, etc.)
/// </summary>
public static class ProtobufBuilders
{
    #region Trace Builders

    /// <summary>
    /// Creates a Span with sensible defaults.
    /// </summary>
    public static Span CreateSpan(
        string name = "test-span",
        Status.Types.StatusCode statusCode = Status.Types.StatusCode.Unset,
        string? statusMessage = null,
        long startTimeUnixNano = 1700000000000000000,
        long endTimeUnixNano = 1700000001000000000,
        byte[]? traceId = null,
        byte[]? spanId = null)
    {
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(traceId ?? new byte[16]),
            SpanId = ByteString.CopyFrom(spanId ?? new byte[8]),
            Name = name,
            StartTimeUnixNano = (ulong)startTimeUnixNano,
            EndTimeUnixNano = (ulong)endTimeUnixNano
        };

        if (statusCode != Status.Types.StatusCode.Unset)
        {
            span.Status = new Status
            {
                Code = statusCode,
                Message = statusMessage ?? string.Empty
            };
        }

        return span;
    }

    /// <summary>
    /// Creates a Span with error status.
    /// </summary>
    public static Span CreateErrorSpan(
        string name = "error-span",
        string errorMessage = "Test error")
    {
        return CreateSpan(
            name: name,
            statusCode: Status.Types.StatusCode.Error,
            statusMessage: errorMessage);
    }

    /// <summary>
    /// Creates a Span with an exception event.
    /// </summary>
    public static Span CreateSpanWithException(
        string name = "span-with-exception",
        string exceptionType = "System.Exception",
        string exceptionMessage = "Test exception")
    {
        var span = CreateSpan(name: name);

        var exceptionEvent = new Span.Types.Event
        {
            Name = "exception",
            TimeUnixNano = 1700000000000000000
        };
        exceptionEvent.Attributes.Add(new KeyValue
        {
            Key = "exception.type",
            Value = new AnyValue { StringValue = exceptionType }
        });
        exceptionEvent.Attributes.Add(new KeyValue
        {
            Key = "exception.message",
            Value = new AnyValue { StringValue = exceptionMessage }
        });

        span.Events.Add(exceptionEvent);
        return span;
    }

    /// <summary>
    /// Adds attributes to a Span.
    /// </summary>
    public static Span WithAttributes(this Span span, params (string key, object value)[] attributes)
    {
        foreach (var (key, value) in attributes)
        {
            span.Attributes.Add(CreateKeyValue(key, value));
        }
        return span;
    }

    /// <summary>
    /// Creates an ExportTraceServiceRequest from one or more spans.
    /// </summary>
    public static ExportTraceServiceRequest CreateTraceRequest(
        params Span[] spans)
    {
        return CreateTraceRequest(
            serviceName: "test-service",
            serviceVersion: "1.0.0",
            instrumentationScopeName: "test-instrumentation",
            instrumentationScopeVersion: "1.0.0",
            spans: spans);
    }

    /// <summary>
    /// Creates an ExportTraceServiceRequest with full customization.
    /// </summary>
    public static ExportTraceServiceRequest CreateTraceRequest(
        string serviceName,
        string serviceVersion,
        string instrumentationScopeName,
        string instrumentationScopeVersion,
        params Span[] spans)
    {
        var scopeSpan = new ScopeSpans
        {
            Scope = new InstrumentationScope
            {
                Name = instrumentationScopeName,
                Version = instrumentationScopeVersion
            }
        };
        foreach (var span in spans)
        {
            scopeSpan.Spans.Add(span);
        }

        var resourceSpan = new ResourceSpans
        {
            Resource = new Resource()
        };
        resourceSpan.Resource.Attributes.Add(CreateKeyValue("service.name", serviceName));
        resourceSpan.Resource.Attributes.Add(CreateKeyValue("service.version", serviceVersion));
        resourceSpan.ScopeSpans.Add(scopeSpan);

        var request = new ExportTraceServiceRequest();
        request.ResourceSpans.Add(resourceSpan);

        return request;
    }

    #endregion

    #region Log Builders

    /// <summary>
    /// Creates a LogRecord with sensible defaults.
    /// </summary>
    public static LogRecord CreateLogRecord(
        SeverityNumber severity = SeverityNumber.Info,
        string severityText = "INFO",
        string body = "Test log message",
        long timeUnixNano = 1700000000000000000)
    {
        var logRecord = new LogRecord
        {
            TimeUnixNano = (ulong)timeUnixNano,
            SeverityNumber = severity,
            SeverityText = severityText,
            Body = new AnyValue { StringValue = body }
        };

        return logRecord;
    }

    /// <summary>
    /// Creates a LogRecord with error severity.
    /// </summary>
    public static LogRecord CreateErrorLogRecord(
        string body = "Test error message")
    {
        return CreateLogRecord(
            severity: SeverityNumber.Error,
            severityText: "Error",
            body: body);
    }

    /// <summary>
    /// Creates a LogRecord with exception attributes.
    /// </summary>
    public static LogRecord CreateLogRecordWithException(
        string exceptionType = "System.InvalidOperationException",
        string exceptionMessage = "Test exception",
        SeverityNumber severity = SeverityNumber.Info)
    {
        var logRecord = CreateLogRecord(severity: severity);

        logRecord.Attributes.Add(CreateKeyValue("exception.type", exceptionType));
        logRecord.Attributes.Add(CreateKeyValue("exception.message", exceptionMessage));

        return logRecord;
    }

    /// <summary>
    /// Adds attributes to a LogRecord.
    /// </summary>
    public static LogRecord WithAttributes(this LogRecord logRecord, params (string key, object value)[] attributes)
    {
        foreach (var (key, value) in attributes)
        {
            logRecord.Attributes.Add(CreateKeyValue(key, value));
        }
        return logRecord;
    }

    /// <summary>
    /// Creates an ExportLogsServiceRequest from one or more log records.
    /// </summary>
    public static ExportLogsServiceRequest CreateLogsRequest(
        params LogRecord[] logRecords)
    {
        return CreateLogsRequest(
            serviceName: "test-service",
            instrumentationScopeName: "test-log-scope",
            instrumentationScopeVersion: "1.0.0",
            logRecords: logRecords);
    }

    /// <summary>
    /// Creates an ExportLogsServiceRequest with full customization.
    /// </summary>
    public static ExportLogsServiceRequest CreateLogsRequest(
        string serviceName,
        string instrumentationScopeName,
        string instrumentationScopeVersion,
        params LogRecord[] logRecords)
    {
        var scopeLog = new ScopeLogs
        {
            Scope = new InstrumentationScope
            {
                Name = instrumentationScopeName,
                Version = instrumentationScopeVersion
            }
        };
        foreach (var logRecord in logRecords)
        {
            scopeLog.LogRecords.Add(logRecord);
        }

        var resourceLog = new ResourceLogs
        {
            Resource = new Resource()
        };
        resourceLog.Resource.Attributes.Add(CreateKeyValue("service.name", serviceName));
        resourceLog.ScopeLogs.Add(scopeLog);

        var request = new ExportLogsServiceRequest();
        request.ResourceLogs.Add(resourceLog);

        return request;
    }

    #endregion

    #region Metric Builders

    /// <summary>
    /// Creates a histogram metric with sensible defaults.
    /// </summary>
    public static Metric CreateHistogramMetric(
        string name = "http.request.duration",
        string unit = "ms",
        string description = "HTTP request duration in milliseconds",
        double sum = 5000.0,
        ulong count = 100,
        double[]? explicitBounds = null,
        ulong[]? bucketCounts = null)
    {
        var metric = new Metric
        {
            Name = name,
            Unit = unit,
            Description = description
        };

        var histogram = new Histogram();
        var dataPoint = new HistogramDataPoint
        {
            StartTimeUnixNano = 1700000000000000000,
            TimeUnixNano = 1700000001000000000,
            Count = count,
            Sum = sum
        };

        // Default histogram buckets
        var bounds = explicitBounds ?? new[] { 100.0, 500.0, 1000.0 };
        var counts = bucketCounts ?? new ulong[] { 30, 50, 15, 5 };

        foreach (var bound in bounds)
        {
            dataPoint.ExplicitBounds.Add(bound);
        }

        foreach (var bucketCount in counts)
        {
            dataPoint.BucketCounts.Add(bucketCount);
        }

        histogram.DataPoints.Add(dataPoint);
        metric.Histogram = histogram;

        return metric;
    }

    /// <summary>
    /// Adds attributes to a histogram metric's data point.
    /// </summary>
    public static Metric WithAttributes(this Metric metric, params (string key, object value)[] attributes)
    {
        if (metric.Histogram != null && metric.Histogram.DataPoints.Count > 0)
        {
            var dataPoint = metric.Histogram.DataPoints[0];
            foreach (var (key, value) in attributes)
            {
                dataPoint.Attributes.Add(CreateKeyValue(key, value));
            }
        }
        return metric;
    }

    /// <summary>
    /// Creates an ExportMetricsServiceRequest from one or more metrics.
    /// </summary>
    public static ExportMetricsServiceRequest CreateMetricsRequest(
        params Metric[] metrics)
    {
        return CreateMetricsRequest(
            serviceName: "test-service",
            hostName: "test-host",
            instrumentationScopeName: "test-metrics-scope",
            instrumentationScopeVersion: "1.0.0",
            metrics: metrics);
    }

    /// <summary>
    /// Creates an ExportMetricsServiceRequest with full customization.
    /// </summary>
    public static ExportMetricsServiceRequest CreateMetricsRequest(
        string serviceName,
        string hostName,
        string instrumentationScopeName,
        string instrumentationScopeVersion,
        params Metric[] metrics)
    {
        var scopeMetric = new ScopeMetrics
        {
            Scope = new InstrumentationScope
            {
                Name = instrumentationScopeName,
                Version = instrumentationScopeVersion
            }
        };
        foreach (var metric in metrics)
        {
            scopeMetric.Metrics.Add(metric);
        }

        var resourceMetric = new ResourceMetrics
        {
            Resource = new Resource()
        };
        resourceMetric.Resource.Attributes.Add(CreateKeyValue("service.name", serviceName));
        resourceMetric.Resource.Attributes.Add(CreateKeyValue("host.name", hostName));
        resourceMetric.ScopeMetrics.Add(scopeMetric);

        var request = new ExportMetricsServiceRequest();
        request.ResourceMetrics.Add(resourceMetric);

        return request;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a KeyValue pair from a key and value.
    /// Automatically infers the type from the value.
    /// </summary>
    private static KeyValue CreateKeyValue(string key, object value)
    {
        var anyValue = value switch
        {
            string s => new AnyValue { StringValue = s },
            int i => new AnyValue { IntValue = i },
            long l => new AnyValue { IntValue = l },
            double d => new AnyValue { DoubleValue = d },
            bool b => new AnyValue { BoolValue = b },
            _ => throw new ArgumentException($"Unsupported value type: {value.GetType()}")
        };

        return new KeyValue { Key = key, Value = anyValue };
    }

    #endregion
}

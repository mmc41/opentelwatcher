using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Trace.V1;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services;

/// <summary>
/// Service for detecting errors and exceptions in OpenTelemetry data.
/// Inspects protobuf structures to identify error traces and logs.
/// </summary>
public class ErrorDetectionService : IErrorDetectionService
{
    /// <summary>
    /// Minimum severity number that indicates an error or fatal condition.
    /// Per OpenTelemetry specification:
    /// - 17-20: ERROR severity levels
    /// - 21-24: FATAL severity levels
    /// </summary>
    private const int ErrorSeverityThreshold = 17;
    /// <summary>
    /// Determines whether the trace request contains any spans with errors.
    /// Checks for:
    /// - Span status code = STATUS_CODE_ERROR (value 2)
    /// - Span events with name = "exception"
    /// </summary>
    public bool ContainsErrors(ExportTraceServiceRequest request)
    {
        if (request?.ResourceSpans == null)
            return false;

        foreach (var resourceSpan in request.ResourceSpans)
        {
            if (resourceSpan?.ScopeSpans == null)
                continue;

            foreach (var scopeSpan in resourceSpan.ScopeSpans)
            {
                if (scopeSpan?.Spans == null)
                    continue;

                foreach (var span in scopeSpan.Spans)
                {
                    // Check status code for ERROR (value 2)
                    if (span.Status?.Code == Status.Types.StatusCode.Error)
                        return true;

                    // Check for exception events
                    if (span.Events != null)
                    {
                        foreach (var spanEvent in span.Events)
                        {
                            if (spanEvent.Name == "exception")
                                return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether the log request contains any records with errors.
    /// Checks for:
    /// - Severity number >= 17 (ERROR or FATAL per OpenTelemetry spec)
    /// - Attributes with keys: exception.type, exception.message, or exception.stacktrace
    /// </summary>
    public bool ContainsErrors(ExportLogsServiceRequest request)
    {
        if (request?.ResourceLogs == null)
            return false;

        foreach (var resourceLog in request.ResourceLogs)
        {
            if (resourceLog?.ScopeLogs == null)
                continue;

            foreach (var scopeLog in resourceLog.ScopeLogs)
            {
                if (scopeLog?.LogRecords == null)
                    continue;

                foreach (var logRecord in scopeLog.LogRecords)
                {
                    // Check severity number for ERROR or FATAL levels
                    // SeverityNumber is an enum, cast to int for comparison
                    if ((int)logRecord.SeverityNumber >= ErrorSeverityThreshold)
                        return true;

                    // Check for exception attributes
                    if (logRecord.Attributes != null)
                    {
                        foreach (var attribute in logRecord.Attributes)
                        {
                            if (attribute.Key == "exception.type" ||
                                attribute.Key == "exception.message" ||
                                attribute.Key == "exception.stacktrace")
                                return true;
                        }
                    }
                }
            }
        }

        return false;
    }
}

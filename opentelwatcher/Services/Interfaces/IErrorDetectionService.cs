using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Service for detecting errors and exceptions in OpenTelemetry data.
/// </summary>
public interface IErrorDetectionService
{
    /// <summary>
    /// Determines whether the trace request contains any spans with errors.
    /// </summary>
    /// <param name="request">The OTLP trace export request to inspect.</param>
    /// <returns>True if any span has an error status or exception event; otherwise, false.</returns>
    bool ContainsErrors(ExportTraceServiceRequest request);

    /// <summary>
    /// Determines whether the log request contains any records with errors.
    /// </summary>
    /// <param name="request">The OTLP log export request to inspect.</param>
    /// <returns>True if any log record has error severity or exception attributes; otherwise, false.</returns>
    bool ContainsErrors(ExportLogsServiceRequest request);
}

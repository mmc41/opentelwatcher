using OpenTelWatcher.Configuration;

namespace OpenTelWatcher.Models;

/// <summary>
/// Represents a processed telemetry item ready for receiver consumption.
/// </summary>
public sealed record TelemetryItem(
    SignalType Signal,          // Signal type (Traces, Logs, Metrics)
    string NdjsonLine,          // Pre-serialized NDJSON string (includes \n)
    bool IsError,               // Pre-detected error status
    DateTimeOffset Timestamp    // When item was created (UTC)
);

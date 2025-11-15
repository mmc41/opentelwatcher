using Google.Protobuf;

namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Service for writing telemetry data to NDJSON files with rotation support.
/// </summary>
public interface ITelemetryFileWriter : IDisposable
{
    /// <summary>
    /// Writes a Protobuf telemetry message to the appropriate signal file.
    /// </summary>
    /// <typeparam name="T">Type of Protobuf message.</typeparam>
    /// <param name="message">Telemetry message to write.</param>
    /// <param name="signal">Signal type (traces, logs, or metrics).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    Task WriteAsync<T>(T message, string signal, CancellationToken cancellationToken = default)
        where T : IMessage;

    /// <summary>
    /// Gets the current file path for a given signal type.
    /// </summary>
    /// <param name="signal">Signal type (traces, logs, or metrics).</param>
    /// <returns>Current file path for the signal.</returns>
    string GetCurrentFilePath(string signal);
}

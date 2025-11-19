using OpenTelWatcher.Models;

namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Receives telemetry items from the pipeline and processes them.
/// </summary>
public interface ITelemetryReceiver
{
    /// <summary>
    /// Writes a telemetry item to the receiver's destination.
    /// </summary>
    Task WriteAsync(TelemetryItem item, CancellationToken cancellationToken);
}

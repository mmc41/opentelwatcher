using OpenTelWatcher.Models;

namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Determines whether a telemetry item should be processed by a receiver.
/// </summary>
public interface ITelemetryFilter
{
    /// <summary>
    /// Returns true if the item should be written by the receiver.
    /// </summary>
    bool ShouldWrite(TelemetryItem item);
}

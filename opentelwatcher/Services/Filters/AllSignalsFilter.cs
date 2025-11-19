using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services.Filters;

/// <summary>
/// Filter that accepts all telemetry items regardless of signal type or error status.
/// </summary>
public sealed class AllSignalsFilter : ITelemetryFilter
{
    public bool ShouldWrite(TelemetryItem item) => true;
}

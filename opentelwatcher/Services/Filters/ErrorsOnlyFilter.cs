using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services.Filters;

/// <summary>
/// Filter that only accepts telemetry items marked as errors.
/// </summary>
public sealed class ErrorsOnlyFilter : ITelemetryFilter
{
    public bool ShouldWrite(TelemetryItem item) => item.IsError;
}

using OpenTelWatcher.Configuration;

namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Diagnostic information about a telemetry file.
/// </summary>
public record FileInfo(string Path, long SizeBytes, DateTime LastModified);

/// <summary>
/// Service for collecting diagnostic information about the application.
/// </summary>
public interface IDiagnosticsCollector
{
    /// <summary>
    /// Gets current health status from the health monitor.
    /// </summary>
    HealthStatus GetHealthStatus();

    /// <summary>
    /// Gets recent error history.
    /// </summary>
    IReadOnlyList<string> GetRecentErrors();

    /// <summary>
    /// Gets information about active telemetry files.
    /// </summary>
    /// <param name="signal">Signal type filter. Use Unspecified to return all files.</param>
    /// <returns>Collection of file information.</returns>
    IReadOnlyList<FileInfo> GetFileInfo(SignalType signal = SignalType.Unspecified);

    /// <summary>
    /// Gets count of consecutive errors.
    /// </summary>
    int GetConsecutiveErrorCount();

    /// <summary>
    /// Gets the configured output directory.
    /// </summary>
    string GetOutputDirectory();

    /// <summary>
    /// Gets telemetry statistics including message counts.
    /// </summary>
    /// <returns>Telemetry statistics with traces/logs/metrics counts.</returns>
    (long TracesReceived, long LogsReceived, long MetricsReceived) GetTelemetryStatistics();
}

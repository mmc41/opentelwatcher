using Microsoft.Extensions.Logging;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services;

/// <summary>
/// Service for collecting diagnostic information about the application.
/// </summary>
public class DiagnosticsCollector : IDiagnosticsCollector
{
    private readonly OpenTelWatcherOptions _options;
    private readonly IHealthMonitor _healthMonitor;
    private readonly ITelemetryStatistics _statistics;
    private readonly ILogger<DiagnosticsCollector> _logger;

    public DiagnosticsCollector(
        OpenTelWatcherOptions options,
        IHealthMonitor healthMonitor,
        ITelemetryStatistics statistics,
        ILogger<DiagnosticsCollector> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public HealthStatus GetHealthStatus()
    {
        return _healthMonitor.Status;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetRecentErrors()
    {
        return _healthMonitor.GetRecentErrors();
    }

    /// <inheritdoc/>
    public int GetConsecutiveErrorCount()
    {
        return _healthMonitor.ConsecutiveErrorCount;
    }

    /// <inheritdoc/>
    public string GetOutputDirectory()
    {
        return Path.GetFullPath(_options.OutputDirectory);
    }

    /// <inheritdoc/>
    public IReadOnlyList<OpenTelWatcher.Services.Interfaces.FileInfo> GetFileInfo(string? signal = null)
    {
        var fileInfos = new List<OpenTelWatcher.Services.Interfaces.FileInfo>();

        // SECURITY: Validate signal parameter against allowlist to prevent directory traversal
        if (signal != null)
        {
            if (!SignalTypes.IsValid(signal))
            {
                // Invalid signal - return empty list rather than throwing to avoid breaking API contract
                return fileInfos;
            }
        }

        // Check if output directory exists
        if (!Directory.Exists(_options.OutputDirectory))
        {
            return fileInfos;
        }

        // Get all .ndjson files in the output directory
        var searchPattern = signal != null ? $"{signal}.*.ndjson" : "*.ndjson";
        var files = Directory.GetFiles(_options.OutputDirectory, searchPattern);

        foreach (var filePath in files)
        {
            try
            {
                var systemFileInfo = new System.IO.FileInfo(filePath);
                var fileInfo = new OpenTelWatcher.Services.Interfaces.FileInfo(
                    Path: filePath,
                    SizeBytes: systemFileInfo.Length,
                    LastModified: systemFileInfo.LastWriteTimeUtc
                );
                fileInfos.Add(fileInfo);
            }
            catch (UnauthorizedAccessException ex)
            {
                // Skip files that can't be accessed due to permissions
                _logger.LogDebug(ex, "Skipping file due to access denied: {FilePath}", filePath);
                continue;
            }
            catch (IOException ex)
            {
                // Skip files that can't be accessed due to I/O errors
                _logger.LogDebug(ex, "Skipping file due to I/O error: {FilePath}", filePath);
                continue;
            }
            // Let critical exceptions (OutOfMemoryException, StackOverflowException, etc.) propagate
        }

        return fileInfos;
    }

    /// <inheritdoc/>
    public (long TracesReceived, long LogsReceived, long MetricsReceived) GetTelemetryStatistics()
    {
        return (_statistics.TracesReceived, _statistics.LogsReceived, _statistics.MetricsReceived);
    }
}

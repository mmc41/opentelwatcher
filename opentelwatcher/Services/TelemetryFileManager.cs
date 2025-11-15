using Microsoft.Extensions.Logging;
using OpenTelWatcher.Services.Interfaces;
using OpenTelWatcher.Utilities;

namespace OpenTelWatcher.Services;

/// <summary>
/// Service for managing telemetry data files.
/// Provides operations for clearing and managing stored telemetry files.
/// </summary>
public class TelemetryFileManager : ITelemetryFileManager
{
    private readonly ILogger<TelemetryFileManager> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public TelemetryFileManager(ILogger<TelemetryFileManager> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc/>
    public async Task<int> ClearAllFilesAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory cannot be null or whitespace", nameof(outputDirectory));
        }

        if (!Directory.Exists(outputDirectory))
        {
            _logger.LogWarning("Output directory does not exist: {OutputDirectory}", outputDirectory);
            return 0;
        }

        // Use shared utility for clearing logic
        _logger.LogInformation("Clearing all NDJSON files in {OutputDirectory}", outputDirectory);

        var result = await TelemetryCleaner.ClearFilesAsync(_loggerFactory, outputDirectory, cancellationToken);

        _logger.LogInformation("Successfully deleted {FilesDeleted} of {TotalFiles} NDJSON files",
            result.FilesDeleted, result.FilesBeforeCount);

        return result.FilesDeleted;
    }
}

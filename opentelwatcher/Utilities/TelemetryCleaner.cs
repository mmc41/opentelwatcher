using Microsoft.Extensions.Logging;

namespace OpenTelWatcher.Utilities;

/// <summary>
/// Static utility for clearing telemetry data files.
/// Provides shared file deletion logic used by both the API endpoint and CLI command.
/// </summary>
public static class TelemetryCleaner
{
    /// <summary>
    /// Clears all NDJSON telemetry files from the specified directory.
    /// Includes retry logic to handle files that may be temporarily locked.
    /// </summary>
    /// <param name="loggerFactory">Logger factory for creating logger instance</param>
    /// <param name="outputDirectory">Directory containing telemetry files</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Statistics about the clearing operation</returns>
    public static async Task<ClearResult> ClearFilesAsync(ILoggerFactory loggerFactory, string outputDirectory, CancellationToken cancellationToken = default)
    {
        if (loggerFactory == null)
        {
            throw new ArgumentNullException(nameof(loggerFactory));
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory cannot be null or whitespace", nameof(outputDirectory));
        }

        var logger = loggerFactory.CreateLogger("OpenTelWatcher.Utilities.TelemetryCleaner");

        if (!Directory.Exists(outputDirectory))
        {
            return new ClearResult
            {
                FilesDeleted = 0,
                SpaceFreedBytes = 0,
                DirectoryPath = outputDirectory,
                FilesBeforeCount = 0
            };
        }

        // Get before stats
        var ndjsonFiles = Directory.GetFiles(outputDirectory, "*.ndjson", SearchOption.TopDirectoryOnly);
        var filesBeforeCount = ndjsonFiles.Length;
        var sizeBeforeBytes = ndjsonFiles.Sum(f =>
        {
            try
            {
                return new FileInfo(f).Length;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to get file size for {FilePath}, skipping in size calculation", f);
                return 0L;
            }
        });

        // Delete files
        var filesDeleted = 0;
        foreach (var filePath in ndjsonFiles)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (await TryDeleteFileAsync(logger, filePath, cancellationToken))
            {
                filesDeleted++;
            }
        }

        return new ClearResult
        {
            FilesDeleted = filesDeleted,
            SpaceFreedBytes = sizeBeforeBytes,
            DirectoryPath = outputDirectory,
            FilesBeforeCount = filesBeforeCount
        };
    }

    /// <summary>
    /// Attempts to delete a file with retry logic to handle files that may be temporarily locked.
    /// </summary>
    private static async Task<bool> TryDeleteFileAsync(ILogger logger, string filePath, CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        const int retryDelayMs = 100;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                File.Delete(filePath);
                return true;
            }
            catch (IOException ex) when (attempt < maxRetries - 1)
            {
                // File might be locked by a write operation, wait and retry
                logger.LogDebug(ex, "File locked on attempt {Attempt}/{MaxRetries}: {FilePath}, retrying after {DelayMs}ms",
                    attempt + 1, maxRetries, filePath, retryDelayMs);
                await Task.Delay(retryDelayMs, cancellationToken);
            }
            catch (IOException ex)
            {
                // File still locked after all retries
                logger.LogWarning(ex, "Failed to delete file after {MaxRetries} attempts (file locked): {FilePath}",
                    maxRetries, filePath);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                // Access denied, skip this file
                logger.LogWarning(ex, "Access denied when deleting file: {FilePath}", filePath);
                return false;
            }
            catch (Exception ex)
            {
                // Unexpected error, skip this file
                logger.LogError(ex, "Unexpected error deleting file: {FilePath}", filePath);
                return false;
            }
        }

        // All retries failed
        return false;
    }
}

/// <summary>
/// Result of a telemetry data clearing operation.
/// </summary>
public sealed record ClearResult
{
    /// <summary>
    /// Number of files successfully deleted.
    /// </summary>
    public required int FilesDeleted { get; init; }

    /// <summary>
    /// Total bytes freed by deleting files.
    /// </summary>
    public required long SpaceFreedBytes { get; init; }

    /// <summary>
    /// Directory path where files were cleared from.
    /// </summary>
    public required string DirectoryPath { get; init; }

    /// <summary>
    /// Number of files present before clearing operation.
    /// </summary>
    public required int FilesBeforeCount { get; init; }
}

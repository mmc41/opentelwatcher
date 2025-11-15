namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Service for managing telemetry data files.
/// Provides operations for clearing and managing stored telemetry files.
/// </summary>
public interface ITelemetryFileManager
{
    /// <summary>
    /// Deletes all NDJSON telemetry files from the output directory.
    /// This operation is safe to call while telemetry is being written.
    /// </summary>
    /// <param name="outputDirectory">The directory containing telemetry files</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task that returns the number of files deleted</returns>
    Task<int> ClearAllFilesAsync(string outputDirectory, CancellationToken cancellationToken = default);
}

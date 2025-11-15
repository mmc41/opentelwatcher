namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Service for managing telemetry file rotation.
/// </summary>
public interface IFileRotationService
{
    /// <summary>
    /// Checks if a file needs rotation based on size limits.
    /// </summary>
    /// <param name="filePath">Path to the file to check.</param>
    /// <param name="maxFileSizeMB">Maximum file size in megabytes.</param>
    /// <returns>True if file needs rotation, false otherwise.</returns>
    bool ShouldRotate(string filePath, int maxFileSizeMB);

    /// <summary>
    /// Generates a new file path for a signal with UTC timestamp.
    /// </summary>
    /// <param name="outputDirectory">Base output directory.</param>
    /// <param name="signal">Signal type (traces, logs, or metrics).</param>
    /// <returns>New file path with format: {signal}.yyyyMMdd_HHmmss.ndjson</returns>
    string GenerateNewFilePath(string outputDirectory, string signal);

    /// <summary>
    /// Gets the current active file path for a signal, creating if needed.
    /// </summary>
    /// <param name="outputDirectory">Base output directory.</param>
    /// <param name="signal">Signal type (traces, logs, or metrics).</param>
    /// <returns>Current active file path.</returns>
    string GetOrCreateFilePath(string outputDirectory, string signal);

    /// <summary>
    /// Rotates the active file for a signal by generating and caching a new file path.
    /// </summary>
    /// <param name="outputDirectory">Base output directory.</param>
    /// <param name="signal">Signal type (traces, logs, or metrics).</param>
    /// <returns>New file path for the signal.</returns>
    string RotateFile(string outputDirectory, string signal);
}

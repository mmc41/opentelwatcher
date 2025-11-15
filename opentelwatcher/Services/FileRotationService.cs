using OpenTelWatcher.Services.Interfaces;
using System.Collections.Concurrent;

namespace OpenTelWatcher.Services;

/// <summary>
/// Service for managing telemetry file rotation based on size limits.
/// Thread-safe implementation using per-signal semaphores for rotation operations.
/// </summary>
public class FileRotationService : IFileRotationService, IDisposable
{
    private readonly ConcurrentDictionary<string, string> _activeFilePaths = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _rotationLocks = new();
    private bool _disposed = false;

    /// <inheritdoc/>
    public bool ShouldRotate(string filePath, int maxFileSizeMB)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        var fileInfo = new System.IO.FileInfo(filePath);
        var maxSizeBytes = maxFileSizeMB * 1024 * 1024; // Convert MB to bytes

        return fileInfo.Length > maxSizeBytes;
    }

    /// <inheritdoc/>
    public string GenerateNewFilePath(string outputDirectory, string signal)
    {
        // Generate UTC timestamp with millisecond precision to prevent collisions: yyyyMMdd_HHmmss_fff
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        var fileName = $"{signal}.{timestamp}.ndjson";

        return Path.Combine(outputDirectory, fileName);
    }

    /// <inheritdoc/>
    public string GetOrCreateFilePath(string outputDirectory, string signal)
    {
        // Use signal as the key to track active file per signal type
        return _activeFilePaths.GetOrAdd(signal, _ =>
        {
            // Ensure output directory exists
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            return GenerateNewFilePath(outputDirectory, signal);
        });
    }

    /// <inheritdoc/>
    public string RotateFile(string outputDirectory, string signal)
    {
        // Get or create a semaphore for this signal type
        var semaphore = _rotationLocks.GetOrAdd(signal, _ => new SemaphoreSlim(1, 1));

        semaphore.Wait();
        try
        {
            // Ensure output directory exists
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            // Generate new file path
            var newFilePath = GenerateNewFilePath(outputDirectory, signal);

            // Update cached path
            _activeFilePaths[signal] = newFilePath;

            return newFilePath;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Disposes resources used by the FileRotationService, including all semaphores.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose pattern implementation.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources (semaphores)
                foreach (var semaphore in _rotationLocks.Values)
                {
                    semaphore?.Dispose();
                }
                _rotationLocks.Clear();
            }

            _disposed = true;
        }
    }
}

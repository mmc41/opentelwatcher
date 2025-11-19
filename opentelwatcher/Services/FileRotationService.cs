using OpenTelWatcher.Configuration;
using OpenTelWatcher.Services.Interfaces;
using System.Collections.Concurrent;

namespace OpenTelWatcher.Services;

/// <summary>
/// Service for managing telemetry file rotation based on size limits.
/// Thread-safe implementation using per-signal semaphores for rotation operations.
/// </summary>
public class FileRotationService : IFileRotationService, IDisposable
{
    private readonly ITimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, string> _activeFilePaths = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _rotationLocks = new();
    private bool _disposed = false;

    public FileRotationService(ITimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

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
    public string GenerateNewFilePath(string outputDirectory, SignalType signal)
    {
        // Generate UTC timestamp with millisecond precision to prevent collisions: yyyyMMdd_HHmmss_fff
        var timestamp = _timeProvider.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        var fileName = $"{signal.ToLowerString()}.{timestamp}.ndjson";

        return Path.Combine(outputDirectory, fileName);
    }

    /// <inheritdoc/>
    public string GetOrCreateFilePath(string outputDirectory, SignalType signal)
    {
        // Use signal string as the key to track active file per signal type
        var signalKey = signal.ToLowerString();
        return _activeFilePaths.GetOrAdd(signalKey, _ =>
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
    public string RotateFile(string outputDirectory, SignalType signal)
    {
        // Use signal string as the key
        var signalKey = signal.ToLowerString();

        // Get or create a semaphore for this signal type
        var semaphore = _rotationLocks.GetOrAdd(signalKey, _ => new SemaphoreSlim(1, 1));

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
            _activeFilePaths[signalKey] = newFilePath;

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

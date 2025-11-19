using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services.Receivers;

/// <summary>
/// Writes telemetry items to NDJSON files with configurable file extension.
/// Thread-safe implementation using per-signal semaphores.
/// </summary>
public sealed class FileReceiver : ITelemetryReceiver, IDisposable
{
    private readonly IFileRotationService _rotationService;
    private readonly string _outputDirectory;
    private readonly string _fileExtension;
    private readonly int _maxFileSizeMB;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _signalLocks;
    private readonly ILogger<FileReceiver> _logger;
    private bool _disposed;

    public FileReceiver(
        IFileRotationService rotationService,
        string outputDirectory,
        string fileExtension,
        int maxFileSizeMB,
        ILogger<FileReceiver> logger)
    {
        _rotationService = rotationService ?? throw new ArgumentNullException(nameof(rotationService));
        _outputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
        _fileExtension = fileExtension ?? throw new ArgumentNullException(nameof(fileExtension));
        _maxFileSizeMB = maxFileSizeMB;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _signalLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
    }

    public async Task WriteAsync(TelemetryItem item, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FileReceiver));
        }

        var signalKey = item.Signal.ToLowerString();
        var semaphore = _signalLocks.GetOrAdd(signalKey, _ => new SemaphoreSlim(1, 1));

        bool semaphoreAcquired = false;
        try
        {
            await semaphore.WaitAsync(cancellationToken);
            semaphoreAcquired = true;

            // Get base path from rotation service
            var basePath = _rotationService.GetOrCreateFilePath(_outputDirectory, item.Signal);

            // Transform to target extension
            var targetPath = basePath.Replace(".ndjson", _fileExtension);

            // Check rotation
            if (File.Exists(targetPath) && _rotationService.ShouldRotate(targetPath, _maxFileSizeMB))
            {
                var newBasePath = _rotationService.RotateFile(_outputDirectory, item.Signal);
                targetPath = newBasePath.Replace(".ndjson", _fileExtension);
            }

            // Check disk space
            CheckDiskSpace(targetPath, item.NdjsonLine.Length);

            // Write
            await File.AppendAllTextAsync(targetPath, item.NdjsonLine, cancellationToken);

            _logger.LogDebug("Wrote telemetry to {FilePath}", targetPath);
        }
        finally
        {
            if (semaphoreAcquired)
            {
                semaphore.Release();
            }
        }
    }

    private void CheckDiskSpace(string filePath, int requiredBytes)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(filePath)!);
            var requiredSpaceWithBuffer = requiredBytes + ApiConstants.DiskSpace.MinFreeSpaceBytes;

            if (drive.AvailableFreeSpace < requiredSpaceWithBuffer)
            {
                throw new IOException(
                    $"Insufficient disk space. Available: {drive.AvailableFreeSpace / (1024 * 1024)} MB, " +
                    $"Required (including buffer): {requiredSpaceWithBuffer / (1024 * 1024)} MB");
            }
        }
        catch (Exception ex) when (ex is not IOException)
        {
            _logger.LogWarning(ex, "Failed to check disk space for {FilePath}", filePath);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var semaphore in _signalLocks.Values)
        {
            semaphore.Dispose();
        }
        _signalLocks.Clear();
        _disposed = true;
    }
}

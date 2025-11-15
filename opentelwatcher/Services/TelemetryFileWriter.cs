using Google.Protobuf;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Serialization;
using OpenTelWatcher.Services.Interfaces;
using System.Collections.Concurrent;

namespace OpenTelWatcher.Services;

/// <summary>
/// Service for writing telemetry data to NDJSON files with rotation support.
/// Thread-safe implementation using per-signal semaphores.
/// Automatically writes error traces and logs to separate .errors.ndjson files.
/// </summary>
public class TelemetryFileWriter : ITelemetryFileWriter
{
    private readonly OpenTelWatcherOptions _options;
    private readonly IFileRotationService _fileRotationService;
    private readonly IHealthMonitor _healthMonitor;
    private readonly IErrorDetectionService _errorDetectionService;
    private readonly ILogger<TelemetryFileWriter> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _signalLocks = new();
    private bool _disposed = false;

    public TelemetryFileWriter(
        OpenTelWatcherOptions options,
        IFileRotationService fileRotationService,
        IHealthMonitor healthMonitor,
        IErrorDetectionService errorDetectionService,
        ILogger<TelemetryFileWriter> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _fileRotationService = fileRotationService ?? throw new ArgumentNullException(nameof(fileRotationService));
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _errorDetectionService = errorDetectionService ?? throw new ArgumentNullException(nameof(errorDetectionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task WriteAsync<T>(T message, string signal, CancellationToken cancellationToken = default)
        where T : IMessage
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        if (string.IsNullOrWhiteSpace(signal))
        {
            throw new ArgumentException("Signal cannot be null or whitespace", nameof(signal));
        }

        // Get or create a semaphore for this signal type
        var semaphore = _signalLocks.GetOrAdd(signal, _ => new SemaphoreSlim(1, 1));

        // Track whether we successfully acquired the semaphore to prevent release on cancellation
        bool semaphoreAcquired = false;
        try
        {
            await semaphore.WaitAsync(cancellationToken);
            semaphoreAcquired = true;

            // Detect errors BEFORE JSON conversion for efficiency
            bool containsErrors = DetectErrors(message, signal);

            // Convert Protobuf message to JSON (single conversion for both files)
            var json = ProtobufToJsonConverter.ConvertToJson(message, _options.PrettyPrint);

            // Format as NDJSON line
            var ndjsonLine = NdjsonWriter.FormatAsNdjsonLine(json);

            // Get current file path for normal file
            var filePath = _fileRotationService.GetOrCreateFilePath(_options.OutputDirectory, signal);

            // Check if rotation is needed for normal file
            if (_fileRotationService.ShouldRotate(filePath, _options.MaxFileSizeMB))
            {
                // Rotate file and get new file path
                filePath = _fileRotationService.RotateFile(_options.OutputDirectory, signal);
            }

            // Check disk space before writing to prevent corruption
            CheckDiskSpace(filePath, ndjsonLine.Length);

            // Write to normal file
            await File.AppendAllTextAsync(filePath, ndjsonLine, cancellationToken);

            // If errors detected, also write to error file
            if (containsErrors)
            {
                await WriteToErrorFileAsync(signal, ndjsonLine, cancellationToken);
            }

            // Record success in health monitor
            _healthMonitor.RecordSuccess();
        }
        catch (Exception ex)
        {
            // Only record error if we acquired the semaphore (not cancellation during wait)
            if (semaphoreAcquired)
            {
                // Record error in health monitor
                _healthMonitor.RecordError($"Failed to write {signal}: {ex.Message}");
            }
            throw;
        }
        finally
        {
            // Only release if we successfully acquired the semaphore
            if (semaphoreAcquired)
            {
                semaphore.Release();
            }
        }
    }

    /// <inheritdoc/>
    public string GetCurrentFilePath(string signal)
    {
        if (string.IsNullOrWhiteSpace(signal))
        {
            throw new ArgumentException("Signal cannot be null or whitespace", nameof(signal));
        }

        return _fileRotationService.GetOrCreateFilePath(_options.OutputDirectory, signal);
    }

    /// <summary>
    /// Detects if the telemetry message contains errors based on signal type.
    /// Uses type-safe protobuf inspection before JSON conversion.
    /// </summary>
    /// <param name="message">The protobuf message to inspect</param>
    /// <param name="signal">The signal type (traces, logs, metrics)</param>
    /// <returns>True if the message contains errors; otherwise, false</returns>
    private bool DetectErrors<T>(T message, string signal) where T : IMessage
    {
        return signal switch
        {
            "traces" when message is ExportTraceServiceRequest traceRequest
                => _errorDetectionService.ContainsErrors(traceRequest),
            "logs" when message is ExportLogsServiceRequest logsRequest
                => _errorDetectionService.ContainsErrors(logsRequest),
            _ => false // Metrics and other signals don't have error detection
        };
    }

    /// <summary>
    /// Writes telemetry data to the error file with rotation support.
    /// Error files use the pattern: {signal}.{timestamp}.errors.ndjson
    /// </summary>
    /// <param name="signal">Signal type (traces, logs)</param>
    /// <param name="ndjsonLine">The NDJSON formatted line to write</param>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task WriteToErrorFileAsync(string signal, string ndjsonLine, CancellationToken cancellationToken)
    {
        // Get current normal file path and derive error file path from it
        var normalFilePath = _fileRotationService.GetOrCreateFilePath(_options.OutputDirectory, signal);

        // Convert: "traces.20251114_144158_780.ndjson" -> "traces.20251114_144158_780.errors.ndjson"
        var errorFilePath = normalFilePath.Replace(".ndjson", ".errors.ndjson");

        // Check if rotation is needed for error file (based on error file size)
        if (File.Exists(errorFilePath) && _fileRotationService.ShouldRotate(errorFilePath, _options.MaxFileSizeMB))
        {
            // Rotate normal file to get a fresh timestamp, then derive error path from that
            var newNormalFilePath = _fileRotationService.RotateFile(_options.OutputDirectory, signal);
            errorFilePath = newNormalFilePath.Replace(".ndjson", ".errors.ndjson");
        }

        // Write to error file
        await File.AppendAllTextAsync(errorFilePath, ndjsonLine, cancellationToken);

        _logger.LogDebug("Wrote error record to {ErrorFile}", errorFilePath);
    }

    /// <summary>
    /// Checks if there is sufficient disk space available before writing.
    /// Throws IOException if disk space is critically low.
    /// </summary>
    /// <param name="filePath">Path to the file being written</param>
    /// <param name="requiredBytes">Number of bytes to write</param>
    private void CheckDiskSpace(string filePath, int requiredBytes)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(filePath)!);

            // Reserve safety buffer to prevent disk from filling completely
            var requiredSpaceWithBuffer = requiredBytes + ApiConstants.DiskSpace.MinFreeSpaceBytes;

            if (drive.AvailableFreeSpace < requiredSpaceWithBuffer)
            {
                throw new IOException(
                    $"Insufficient disk space. Available: {drive.AvailableFreeSpace / (1024 * 1024)} MB, " +
                    $"Required (including 100 MB buffer): {requiredSpaceWithBuffer / (1024 * 1024)} MB");
            }
        }
        catch (IOException)
        {
            // Re-throw IOException (disk space errors)
            throw;
        }
        catch (NotSupportedException ex)
        {
            // Unsupported file system - ignore and attempt write anyway
            _logger.LogWarning(ex, "Unsupported file system when checking disk space for {FilePath}", filePath);
        }
        catch (ArgumentException ex)
        {
            // Invalid path - ignore and attempt write anyway
            _logger.LogWarning(ex, "Invalid path when checking disk space: {FilePath}", filePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            // Access denied to drive info - ignore and attempt write anyway
            _logger.LogWarning(ex, "Access denied to drive info for {FilePath}", filePath);
        }
    }

    /// <summary>
    /// Disposes resources used by the TelemetryFileWriter, including all semaphores.
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
                foreach (var semaphore in _signalLocks.Values)
                {
                    semaphore?.Dispose();
                }
                _signalLocks.Clear();
            }

            _disposed = true;
        }
    }
}

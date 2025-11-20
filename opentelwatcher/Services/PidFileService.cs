using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services;

/// <summary>
/// Manages process ID registration in a shared PID file for multi-instance coordination.
/// </summary>
/// <remarks>
/// PID File: opentelwatcher.pid located in temp directory (or executable dir for dev builds).
/// Format: NDJSON with entries like {"pid":12345,"port":4318,"timestamp":"2025-01-17T08:00:00Z"}
///
/// Usage:
/// - Instance detection: Check if another instance is running on a port before starting
/// - Daemon mode: Track background process PID for later shutdown
/// - Multi-instance: Multiple instances on different ports share the same PID file
/// - Stale cleanup: Automatically removes entries for dead processes
///
/// Thread Safety:
/// - File-level locking with retry logic (5 attempts, 50ms delay) prevents race conditions
/// - Exclusive locks for writes (Register/Unregister), shared locks for reads
/// - Process name validation prevents PID recycling false positives
///
/// Cleanup:
/// - Graceful: ApplicationStopping event calls Unregister()
/// - Ungraceful: ProcessExit handler ensures cleanup on crashes
/// - Empty file is auto-deleted when last entry removed
/// - Idempotent: Unregister() safe to call multiple times
///
/// Known Limitations:
/// - Hard kill (SIGKILL/Task Manager force) may bypass cleanup leaving stale entries
/// - Network shares may have unreliable file locking
/// - Container restarts lose temp directory PID file
///
/// Error Handling: All methods catch exceptions and never throw. Fatal errors (permissions, disk full) are
/// logged at Error level, while recoverable errors (temporary locks) are logged at Warning level.
/// </remarks>
public sealed class PidFileService : IPidFileService
{
    private readonly IEnvironment _environment;
    private readonly IProcessProvider _processProvider;
    private readonly ITimeProvider _timeProvider;
    private readonly ILogger<PidFileService> _logger;
    private readonly int _currentPid;
    private readonly object _fileLock = new();
    private bool _isUnregistered = false;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string PidFilePath { get; }

    public PidFileService(
        IEnvironment environment,
        IProcessProvider processProvider,
        ITimeProvider timeProvider,
        ILogger<PidFileService> logger)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _processProvider = processProvider ?? throw new ArgumentNullException(nameof(processProvider));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _currentPid = _environment.CurrentProcessId;

        // Determine platform-appropriate PID file directory
        string pidDirectory = _environment.GetRuntimeDirectory();

        PidFilePath = Path.Combine(pidDirectory, "opentelwatcher.pid");
    }

    /// <summary>
    /// Register the current process with PID, port, and timestamp.
    /// Uses file-level locking to prevent race conditions when multiple processes register simultaneously.
    /// </summary>
    public void Register(int port)
    {
        lock (_fileLock)
        {
            FileStream? lockStream = null;
            try
            {
                // Acquire exclusive file lock to prevent race conditions
                lockStream = AcquireFileLock(PidFilePath);

                // Read existing entries
                var entries = ReadEntriesFromStream(lockStream);

                // Add new entry
                var newEntry = new PidEntry
                {
                    Pid = _currentPid,
                    Port = port,
                    Timestamp = _timeProvider.UtcNow
                };
                entries.Add(newEntry);

                // Write all entries back atomically
                WriteEntriesToStream(lockStream, entries);

                _logger.LogInformation(
                    "Registered process {ProcessId} on port {Port} in PID file: {PidFilePath}",
                    _currentPid, port, PidFilePath);
            }
            catch (Exception ex)
            {
                // Distinguish between fatal errors (permissions, disk full) and recoverable errors (temporary locks)
                // PID registration is optional functionality, so we never throw, but fatal errors are logged at Error level
                if (IsFatalException(ex))
                {
                    _logger.LogError(ex,
                        "Fatal error registering PID in {PidFilePath}. This may prevent daemon mode and multi-instance coordination. " +
                        "Check file permissions and disk space.",
                        PidFilePath);
                }
                else
                {
                    _logger.LogWarning(ex, "Failed to register PID in {PidFilePath}", PidFilePath);
                }
            }
            finally
            {
                lockStream?.Dispose();
            }
        }
    }

    /// <summary>
    /// Unregister the current process from the PID file.
    /// Safe to call multiple times - will only perform cleanup once.
    /// Uses file-level locking to prevent race conditions when multiple processes unregister simultaneously.
    /// </summary>
    public void Unregister()
    {
        lock (_fileLock)
        {
            // Idempotent: if already unregistered, do nothing
            if (_isUnregistered)
            {
                return;
            }

            FileStream? lockStream = null;
            try
            {
                if (!File.Exists(PidFilePath))
                {
                    _isUnregistered = true;
                    return; // Nothing to unregister
                }

                // Acquire exclusive file lock to prevent race conditions
                lockStream = AcquireFileLock(PidFilePath);

                // Read existing entries
                var entries = ReadEntriesFromStream(lockStream);

                // Filter out current process
                var remainingEntries = entries.Where(e => e.Pid != _currentPid).ToList();

                // Write remaining entries back (or delete file if empty)
                if (remainingEntries.Count > 0)
                {
                    WriteEntriesToStream(lockStream, remainingEntries);
                }
                else
                {
                    // Close and dispose the lock stream before deleting
                    lockStream?.Dispose();
                    lockStream = null;
                    File.Delete(PidFilePath);
                }

                _isUnregistered = true;
                _logger.LogInformation("Unregistered process {ProcessId} from PID file", _currentPid);
            }
            catch (Exception ex)
            {
                // Distinguish between fatal errors (permissions, disk full) and recoverable errors (temporary locks)
                // PID unregistration is optional functionality, so we never throw, but fatal errors are logged at Error level
                if (IsFatalException(ex))
                {
                    _logger.LogError(ex,
                        "Fatal error unregistering PID from {PidFilePath}. This may leave stale entries. " +
                        "Check file permissions and disk space.",
                        PidFilePath);
                }
                else
                {
                    _logger.LogWarning(ex, "Failed to unregister PID from {PidFilePath}", PidFilePath);
                }
                // Mark as unregistered even on failure to avoid repeated failed attempts
                _isUnregistered = true;
            }
            finally
            {
                lockStream?.Dispose();
            }
        }
    }

    /// <summary>
    /// Get all registered PID entries.
    /// Uses file-level locking to prevent reading while another process is writing.
    /// </summary>
    public IReadOnlyList<PidEntry> GetRegisteredEntries()
    {
        lock (_fileLock)
        {
            FileStream? lockStream = null;
            try
            {
                if (!File.Exists(PidFilePath))
                {
                    return Array.Empty<PidEntry>();
                }

                // Acquire shared file lock for reading
                lockStream = AcquireFileLock(PidFilePath, exclusive: false);

                // Read entries from stream
                var entries = ReadEntriesFromStream(lockStream);

                return entries;
            }
            catch (Exception ex)
            {
                // Distinguish between fatal errors (permissions) and recoverable errors (temporary locks)
                if (IsFatalException(ex))
                {
                    _logger.LogError(ex,
                        "Fatal error reading PIDs from {PidFilePath}. Port auto-detection may not work. " +
                        "Check file permissions.",
                        PidFilePath);
                }
                else
                {
                    _logger.LogWarning(ex, "Failed to read PIDs from {PidFilePath}", PidFilePath);
                }
                return Array.Empty<PidEntry>();
            }
            finally
            {
                lockStream?.Dispose();
            }
        }
    }

    /// <summary>
    /// Get all registered PID entries for a specific port
    /// </summary>
    public IReadOnlyList<PidEntry> GetRegisteredEntriesForPort(int port)
    {
        return GetRegisteredEntries()
            .Where(e => e.Port == port)
            .ToList();
    }

    /// <summary>
    /// Find the entry for a specific PID
    /// </summary>
    public PidEntry? GetEntryByPid(int pid)
    {
        return GetRegisteredEntries().FirstOrDefault(e => e.Pid == pid);
    }

    /// <summary>
    /// Find the entry for a specific port (returns first match if multiple)
    /// </summary>
    public PidEntry? GetEntryByPort(int port)
    {
        return GetRegisteredEntries().FirstOrDefault(e => e.Port == port);
    }

    /// <summary>
    /// Remove stale entries (process no longer running).
    /// Uses file-level locking to prevent race conditions during cleanup.
    /// </summary>
    public int CleanStaleEntries()
    {
        lock (_fileLock)
        {
            FileStream? lockStream = null;
            try
            {
                if (!File.Exists(PidFilePath)) return 0;

                // Acquire exclusive file lock for write operation
                lockStream = AcquireFileLock(PidFilePath);

                // Read existing entries
                var entries = ReadEntriesFromStream(lockStream);

                // Filter to only running processes
                var runningEntries = entries.Where(e => e.IsRunning(_processProvider)).ToList();
                var removedCount = entries.Count - runningEntries.Count;

                if (removedCount > 0)
                {
                    if (runningEntries.Count > 0)
                    {
                        WriteEntriesToStream(lockStream, runningEntries);
                    }
                    else
                    {
                        // Close and dispose the lock stream before deleting
                        lockStream?.Dispose();
                        lockStream = null;
                        File.Delete(PidFilePath);
                    }

                    _logger.LogInformation(
                        "Cleaned {Count} stale PID entries from {PidFilePath}",
                        removedCount, PidFilePath);
                }

                return removedCount;
            }
            catch (Exception ex)
            {
                // Distinguish between fatal errors (permissions, disk full) and recoverable errors (temporary locks)
                if (IsFatalException(ex))
                {
                    _logger.LogError(ex,
                        "Fatal error cleaning stale entries from {PidFilePath}. Stale entries may accumulate. " +
                        "Check file permissions and disk space.",
                        PidFilePath);
                }
                else
                {
                    _logger.LogWarning(ex, "Failed to clean stale entries from {PidFilePath}", PidFilePath);
                }
                return 0;
            }
            finally
            {
                lockStream?.Dispose();
            }
        }
    }

    /// <summary>
    /// Acquires a file lock for the PID file.
    /// </summary>
    /// <param name="filePath">The path to the PID file</param>
    /// <param name="exclusive">If true, acquires exclusive lock; if false, acquires shared lock</param>
    /// <returns>FileStream with appropriate lock</returns>
    private FileStream AcquireFileLock(string filePath, bool exclusive = true)
    {
        // Retry logic to handle transient file access issues
        const int maxRetries = 5;
        const int retryDelayMs = 50;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var fileMode = File.Exists(filePath) ? FileMode.Open : FileMode.Create;
                var fileAccess = exclusive ? FileAccess.ReadWrite : FileAccess.Read;
                var fileShare = exclusive ? FileShare.None : FileShare.Read;

                return new FileStream(filePath, fileMode, fileAccess, fileShare);
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                // File is locked by another process, wait and retry
                _timeProvider.Sleep(retryDelayMs);
            }
        }

        // Final attempt without catching
        var finalFileMode = File.Exists(filePath) ? FileMode.Open : FileMode.Create;
        var finalFileAccess = exclusive ? FileAccess.ReadWrite : FileAccess.Read;
        var finalFileShare = exclusive ? FileShare.None : FileShare.Read;
        return new FileStream(filePath, finalFileMode, finalFileAccess, finalFileShare);
    }

    /// <summary>
    /// Reads PID entries from a file stream.
    /// </summary>
    private List<PidEntry> ReadEntriesFromStream(FileStream stream)
    {
        var entries = new List<PidEntry>();

        stream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, leaveOpen: true);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var entry = JsonSerializer.Deserialize<PidEntry>(line, JsonOptions);
                if (entry != null) entries.Add(entry);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse PID entry: {Line}", line);
            }
        }

        return entries;
    }

    /// <summary>
    /// Writes PID entries to a file stream, replacing existing content.
    /// </summary>
    private void WriteEntriesToStream(FileStream stream, List<PidEntry> entries)
    {
        stream.Seek(0, SeekOrigin.Begin);
        stream.SetLength(0); // Truncate file

        using var writer = new StreamWriter(stream, leaveOpen: true);
        foreach (var entry in entries)
        {
            var json = JsonSerializer.Serialize(entry, JsonOptions);
            writer.WriteLine(json);
        }
        writer.Flush();
    }

    /// <summary>
    /// Determines if an exception represents a fatal error that should be logged at Error level
    /// vs a recoverable error that should be logged at Warning level.
    /// </summary>
    /// <remarks>
    /// Fatal errors include:
    /// - UnauthorizedAccessException: Permission denied on PID file or directory
    /// - DirectoryNotFoundException: PID directory doesn't exist
    /// - IOException with disk full, path too long, or network path errors
    ///
    /// Recoverable errors include:
    /// - IOException for file locking (handled by retry logic)
    /// - Other transient I/O errors
    /// </remarks>
    private static bool IsFatalException(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => true,
            DirectoryNotFoundException => true,
            IOException ioEx => IsFatalIOException(ioEx),
            _ => false
        };
    }

    /// <summary>
    /// Determines if an IOException represents a fatal error.
    /// </summary>
    private static bool IsFatalIOException(IOException ioEx)
    {
        // Check for specific fatal error codes (Windows HRESULTs)
        const int ERROR_DISK_FULL = unchecked((int)0x80070070);
        const int ERROR_HANDLE_DISK_FULL = unchecked((int)0x80070027);
        const int ERROR_FILENAME_EXCED_RANGE = unchecked((int)0x800700CE);
        const int ERROR_BAD_NETPATH = unchecked((int)0x80070035);

        int hResult = ioEx.HResult;
        return hResult == ERROR_DISK_FULL
            || hResult == ERROR_HANDLE_DISK_FULL
            || hResult == ERROR_FILENAME_EXCED_RANGE
            || hResult == ERROR_BAD_NETPATH;
    }
}

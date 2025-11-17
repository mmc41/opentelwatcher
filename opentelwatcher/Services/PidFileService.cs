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
/// - Disk full causes silent failures (logged as warnings)
///
/// Error Handling: All methods catch exceptions and log warnings. PID file failures never crash the app.
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
        string pidDirectory = GetPidFileDirectory();

        PidFilePath = Path.Combine(pidDirectory, "opentelwatcher.pid");
    }

    /// <summary>
    /// Gets the appropriate directory for the PID file based on platform and deployment scenario.
    /// </summary>
    private string GetPidFileDirectory()
    {
        // For development/testing: Use executable directory if running from artifacts
        var executableDir = _environment.BaseDirectory;
        if (executableDir.Contains("artifacts"))
        {
            return executableDir;
        }

        // For production deployments: Use platform-appropriate temp directory
        // Linux/macOS: XDG_RUNTIME_DIR provides per-user runtime directory
        // Windows: Path.GetTempPath() provides user temp directory
        var xdgRuntimeDir = _environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(xdgRuntimeDir) && Directory.Exists(xdgRuntimeDir))
        {
            return xdgRuntimeDir;
        }

        // Fallback to OS temp directory
        return _environment.TempPath;
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
                // Log but don't throw - PID registration is optional functionality
                _logger.LogWarning(ex, "Failed to register PID in {PidFilePath}", PidFilePath);
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
                // Log but don't throw - PID unregistration is optional functionality
                _logger.LogWarning(ex, "Failed to unregister PID from {PidFilePath}", PidFilePath);
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
                _logger.LogWarning(ex, "Failed to read PIDs from {PidFilePath}", PidFilePath);
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
                _logger.LogWarning(ex, "Failed to clean stale entries from {PidFilePath}", PidFilePath);
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
}

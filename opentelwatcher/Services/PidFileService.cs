using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services;

/// <summary>
/// Service for managing process ID registration in opentelwatcher.pid file.
/// The PID file is located in a platform-appropriate temporary directory.
/// On Linux/macOS: Uses XDG_RUNTIME_DIR or temp directory.
/// On Windows: Uses temp directory.
/// For development builds: Uses executable directory (artifacts/bin/watcher/Debug).
/// File format: JSON Lines (NDJSON) with PID, port, and timestamp.
/// </summary>
public sealed class PidFileService : IPidFileService
{
    private readonly int _currentPid;
    private readonly object _fileLock = new();
    private bool _isUnregistered = false;
    private readonly ILogger<PidFileService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string PidFilePath { get; }

    public PidFileService(ILogger<PidFileService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _currentPid = Environment.ProcessId;

        // Determine platform-appropriate PID file directory
        string pidDirectory = GetPidFileDirectory();

        PidFilePath = Path.Combine(pidDirectory, "opentelwatcher.pid");
    }

    /// <summary>
    /// Gets the appropriate directory for the PID file based on platform and deployment scenario.
    /// </summary>
    private static string GetPidFileDirectory()
    {
        // For development/testing: Use executable directory if running from artifacts
        var executableDir = AppContext.BaseDirectory;
        if (executableDir.Contains("artifacts"))
        {
            return executableDir;
        }

        // For production deployments: Use platform-appropriate temp directory
        // Linux/macOS: XDG_RUNTIME_DIR provides per-user runtime directory
        // Windows: Path.GetTempPath() provides user temp directory
        var xdgRuntimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(xdgRuntimeDir) && Directory.Exists(xdgRuntimeDir))
        {
            return xdgRuntimeDir;
        }

        // Fallback to OS temp directory
        return Path.GetTempPath();
    }

    /// <summary>
    /// Register the current process with PID, port, and timestamp
    /// </summary>
    public void Register(int port)
    {
        lock (_fileLock)
        {
            try
            {
                var entry = new PidEntry
                {
                    Pid = _currentPid,
                    Port = port,
                    Timestamp = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(entry, JsonOptions);
                File.AppendAllLines(PidFilePath, new[] { json });

                _logger.LogInformation(
                    "Registered process {ProcessId} on port {Port} in PID file: {PidFilePath}",
                    _currentPid, port, PidFilePath);
            }
            catch (Exception ex)
            {
                // Log but don't throw - PID registration is optional functionality
                _logger.LogWarning(ex, "Failed to register PID in {PidFilePath}", PidFilePath);
            }
        }
    }

    /// <summary>
    /// Unregister the current process from the PID file.
    /// Safe to call multiple times - will only perform cleanup once.
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

            try
            {
                if (!File.Exists(PidFilePath))
                {
                    _isUnregistered = true;
                    return; // Nothing to unregister
                }

                var entries = GetRegisteredEntries();
                var remainingEntries = entries
                    .Where(e => e.Pid != _currentPid)
                    .ToList();

                // Rewrite the file with remaining entries (or delete if empty)
                if (remainingEntries.Count > 0)
                {
                    var lines = remainingEntries.Select(e => JsonSerializer.Serialize(e, JsonOptions));
                    File.WriteAllLines(PidFilePath, lines);
                }
                else
                {
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
        }
    }

    /// <summary>
    /// Get all registered PID entries
    /// </summary>
    public IReadOnlyList<PidEntry> GetRegisteredEntries()
    {
        lock (_fileLock)
        {
            try
            {
                if (!File.Exists(PidFilePath))
                {
                    return Array.Empty<PidEntry>();
                }

                var lines = File.ReadAllLines(PidFilePath);
                var entries = new List<PidEntry>();

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var entry = JsonSerializer.Deserialize<PidEntry>(line, JsonOptions);
                        if (entry != null) entries.Add(entry);
                    }
                    catch (JsonException)
                    {
                        // Try parsing as legacy format (plain integer)
                        if (int.TryParse(line.Trim(), out var pid))
                        {
                            // Create legacy entry with unknown port and timestamp
                            entries.Add(new PidEntry
                            {
                                Pid = pid,
                                Port = 0, // Unknown
                                Timestamp = DateTime.MinValue // Unknown
                            });
                        }
                        else
                        {
                            _logger.LogWarning("Failed to parse PID entry: {Line}", line);
                        }
                    }
                }

                return entries;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read PIDs from {PidFilePath}", PidFilePath);
                return Array.Empty<PidEntry>();
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
    /// Remove stale entries (process no longer running)
    /// </summary>
    public int CleanStaleEntries()
    {
        lock (_fileLock)
        {
            try
            {
                if (!File.Exists(PidFilePath)) return 0;

                var entries = GetRegisteredEntries();
                var runningEntries = entries.Where(e => e.IsRunning()).ToList();
                var removedCount = entries.Count - runningEntries.Count;

                if (removedCount > 0)
                {
                    if (runningEntries.Count > 0)
                    {
                        var lines = runningEntries.Select(e => JsonSerializer.Serialize(e, JsonOptions));
                        File.WriteAllLines(PidFilePath, lines);
                    }
                    else
                    {
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
        }
    }

    /// <summary>
    /// Get all registered process IDs from the opentelwatcher.pid file
    /// </summary>
    [Obsolete("Use GetRegisteredEntries() instead")]
    public IReadOnlyList<int> GetRegisteredPids()
    {
        return GetRegisteredEntries().Select(e => e.Pid).ToList();
    }
}

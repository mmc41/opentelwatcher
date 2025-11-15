using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services;

/// <summary>
/// Service for managing process ID registration in opentelwatcher.pid file.
/// The PID file is located in a platform-appropriate temporary directory.
/// On Linux/macOS: Uses XDG_RUNTIME_DIR or temp directory.
/// On Windows: Uses temp directory.
/// For development builds: Uses executable directory (artifacts/bin/watcher/Debug).
/// </summary>
public sealed class PidFileService : IPidFileService
{
    private readonly int _currentPid;
    private readonly object _fileLock = new();
    private bool _isUnregistered = false;
    private readonly ILogger<PidFileService> _logger;

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
    /// Register the current process ID by appending it to the opentelwatcher.pid file
    /// </summary>
    public void Register()
    {
        lock (_fileLock)
        {
            try
            {
                // Append the current PID to the file (create if not exists)
                File.AppendAllLines(PidFilePath, new[] { _currentPid.ToString() });
                _logger.LogInformation("Registered process {ProcessId} in PID file: {PidFilePath}", _currentPid, PidFilePath);
            }
            catch (Exception ex)
            {
                // Log but don't throw - PID registration is optional functionality
                _logger.LogWarning(ex, "Failed to register PID in {PidFilePath}", PidFilePath);
            }
        }
    }

    /// <summary>
    /// Unregister the current process ID by removing it from the opentelwatcher.pid file.
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

                // Read all PIDs
                var lines = File.ReadAllLines(PidFilePath);

                // Filter out the current PID
                var remainingPids = lines
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Where(line => int.TryParse(line.Trim(), out var pid) && pid != _currentPid)
                    .ToList();

                // Rewrite the file with remaining PIDs (or delete if empty)
                if (remainingPids.Count > 0)
                {
                    File.WriteAllLines(PidFilePath, remainingPids);
                }
                else
                {
                    File.Delete(PidFilePath);
                }

                _isUnregistered = true;
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
    /// Get all registered process IDs from the opentelwatcher.pid file
    /// </summary>
    public IReadOnlyList<int> GetRegisteredPids()
    {
        lock (_fileLock)
        {
            try
            {
                if (!File.Exists(PidFilePath))
                {
                    return Array.Empty<int>();
                }

                var lines = File.ReadAllLines(PidFilePath);
                var pids = new List<int>();

                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line) && int.TryParse(line.Trim(), out var pid))
                    {
                        pids.Add(pid);
                    }
                }

                return pids;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read PIDs from {PidFilePath}", PidFilePath);
                return Array.Empty<int>();
            }
        }
    }
}

using System.Diagnostics;

namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Represents a single entry in the PID file
/// </summary>
public sealed record PidEntry
{
    public required int Pid { get; init; }
    public required int Port { get; init; }
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Checks if this entry represents a running process
    /// </summary>
    public bool IsRunning()
    {
        try
        {
            var process = Process.GetProcessById(Pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // Process not found
        }
    }

    /// <summary>
    /// Gets the age of this entry
    /// </summary>
    public TimeSpan Age => DateTime.UtcNow - Timestamp;
}

/// <summary>
/// Service for managing process ID registration in opentelwatcher.pid file
/// </summary>
public interface IPidFileService
{
    /// <summary>
    /// Register the current process with PID, port, and timestamp
    /// </summary>
    void Register(int port);

    /// <summary>
    /// Unregister the current process from the PID file
    /// </summary>
    void Unregister();

    /// <summary>
    /// Get all registered PID entries
    /// </summary>
    IReadOnlyList<PidEntry> GetRegisteredEntries();

    /// <summary>
    /// Get all registered PID entries for a specific port
    /// </summary>
    IReadOnlyList<PidEntry> GetRegisteredEntriesForPort(int port);

    /// <summary>
    /// Find the entry for a specific PID
    /// </summary>
    PidEntry? GetEntryByPid(int pid);

    /// <summary>
    /// Find the entry for a specific port (returns first match if multiple)
    /// </summary>
    PidEntry? GetEntryByPort(int port);

    /// <summary>
    /// Remove stale entries (process no longer running)
    /// </summary>
    int CleanStaleEntries();

    /// <summary>
    /// Gets the path to the PID file
    /// </summary>
    string PidFilePath { get; }

    // DEPRECATED - keep for backward compatibility
    [Obsolete("Use GetRegisteredEntries() instead")]
    IReadOnlyList<int> GetRegisteredPids();
}

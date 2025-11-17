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
    /// Checks if this entry represents a running opentelwatcher process.
    /// Validates both process existence and process identity to prevent false positives from PID recycling.
    /// </summary>
    /// <param name="processProvider">The process provider to use for querying process information.</param>
    /// <returns>True if the process is running and is an opentelwatcher process; false otherwise.</returns>
    public bool IsRunning(IProcessProvider processProvider)
    {
        var process = processProvider.GetProcessById(Pid);

        if (process == null || process.HasExited)
            return false;

        // Validate it's actually an opentelwatcher process to prevent PID recycling false positives
        var processName = process.ProcessName.ToLowerInvariant();

        // Valid process names:
        // - "opentelwatcher" or "watcher" (published executable)
        // - "dotnet" (development mode running via dotnet run)
        return processName.Contains("opentelwatcher") ||
               processName.Contains("watcher") ||
               processName.Contains("dotnet");
    }

    /// <summary>
    /// Gets the age of this entry.
    /// </summary>
    /// <param name="timeProvider">The time provider to use for getting the current time.</param>
    /// <returns>The timespan between the entry timestamp and current time.</returns>
    public TimeSpan GetAge(ITimeProvider timeProvider) => timeProvider.UtcNow - Timestamp;
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
}

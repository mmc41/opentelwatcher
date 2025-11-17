namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Represents a process instance retrieved by PID lookup.
/// Used for querying process information (name, status) to validate PID file entries.
/// </summary>
/// <remarks>
/// Scope: Represents ANY process on the system (not just the current application).
/// Use IEnvironment.CurrentProcessId for the currently running application's PID.
///
/// Design Purpose:
/// - Abstraction for System.Diagnostics.Process to enable unit testing
/// - Allows mocking process queries without spawning real processes
/// - Used by PidEntry.IsRunning() to validate processes and prevent PID recycling
/// </remarks>
public interface IProcess
{
    /// <summary>
    /// Gets the unique identifier for the process.
    /// </summary>
    int Id { get; }

    /// <summary>
    /// Gets the name of the process.
    /// </summary>
    string ProcessName { get; }

    /// <summary>
    /// Gets a value indicating whether the process has exited.
    /// </summary>
    bool HasExited { get; }
}

/// <summary>
/// Abstraction for process management operations to enable testability.
/// Wraps System.Diagnostics.Process static methods.
/// </summary>
public interface IProcessProvider
{
    /// <summary>
    /// Gets a process by its process ID.
    /// </summary>
    /// <param name="pid">The process ID to look up.</param>
    /// <returns>The process if found and accessible, null otherwise.</returns>
    IProcess? GetProcessById(int pid);
}

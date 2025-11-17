namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Provides access to environment and system properties for the current application.
/// Used for process context, directory paths, and environment variables.
/// </summary>
/// <remarks>
/// Scope: Represents the CURRENT application's execution context (not other processes).
/// Use IProcessProvider to query information about other processes on the system.
///
/// Design Purpose:
/// - Abstraction for Environment, AppContext, and Path static classes to enable unit testing
/// - Allows mocking system properties without environment-specific configuration
/// - Used by PidFileService to determine PID file location and current process ID
/// </remarks>
public interface IEnvironment
{
    /// <summary>
    /// Gets the unique identifier of the current process (this running application).
    /// </summary>
    int CurrentProcessId { get; }

    /// <summary>
    /// Gets the directory where the application is located.
    /// </summary>
    string BaseDirectory { get; }

    /// <summary>
    /// Gets the current working directory.
    /// </summary>
    string CurrentDirectory { get; }

    /// <summary>
    /// Gets the path to the current process executable, or null if not available.
    /// </summary>
    string? ProcessPath { get; }

    /// <summary>
    /// Gets the path to the system temporary directory.
    /// </summary>
    string TempPath { get; }

    /// <summary>
    /// Retrieves the value of an environment variable.
    /// </summary>
    /// <param name="name">The name of the environment variable.</param>
    /// <returns>The value of the environment variable, or null if not found.</returns>
    string? GetEnvironmentVariable(string name);
}

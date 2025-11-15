namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Service for managing process ID registration in opentelwatcher.pid file
/// </summary>
public interface IPidFileService
{
    /// <summary>
    /// Register the current process ID in the opentelwatcher.pid file
    /// </summary>
    void Register();

    /// <summary>
    /// Unregister the current process ID from the opentelwatcher.pid file
    /// </summary>
    void Unregister();

    /// <summary>
    /// Get all registered process IDs from the opentelwatcher.pid file
    /// </summary>
    /// <returns>List of process IDs</returns>
    IReadOnlyList<int> GetRegisteredPids();

    /// <summary>
    /// Gets the path to the opentelwatcher.pid file
    /// </summary>
    string PidFilePath { get; }
}

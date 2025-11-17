namespace OpenTelWatcher.CLI.Services;

/// <summary>
/// Resolves the port number for CLI commands by either using an explicitly provided port
/// or auto-resolving from the PID file when a single instance is running.
/// </summary>
public interface IPortResolver
{
    /// <summary>
    /// Resolves the port to use for a CLI command.
    /// </summary>
    /// <param name="explicitPort">
    /// The explicitly provided port number, or null to auto-resolve from PID file.
    /// </param>
    /// <returns>
    /// The port number to use.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when auto-resolution fails because:
    /// - No running instances found (empty or non-existent PID file)
    /// - Multiple instances running (user must specify --port explicitly)
    /// </exception>
    int ResolvePort(int? explicitPort);
}

using OpenTelWatcher.CLI.Models;

namespace OpenTelWatcher.CLI.Services;

/// <summary>
/// Client for communicating with watcher HTTP API
/// </summary>
public interface IOpenTelWatcherApiClient
{
    /// <summary>
    /// Get application information from running instance (version + diagnostics)
    /// </summary>
    /// <returns>Info response or null if not running</returns>
    Task<InfoResponse?> GetInfoAsync();

    /// <summary>
    /// Get instance status including compatibility check
    /// </summary>
    /// <param name="cliVersion">CLI version for compatibility check</param>
    /// <returns>Instance status</returns>
    Task<InstanceStatus> GetInstanceStatusAsync(Version cliVersion);

    /// <summary>
    /// Send shutdown command to running instance
    /// </summary>
    /// <returns>True if shutdown initiated successfully</returns>
    Task<bool> ShutdownAsync();

    /// <summary>
    /// Wait for instance to stop (connection refused)
    /// </summary>
    /// <param name="timeoutSeconds">Maximum wait time in seconds</param>
    /// <returns>True if stopped, false if timeout</returns>
    Task<bool> WaitForShutdownAsync(int timeoutSeconds = 30);

    /// <summary>
    /// Clear all telemetry files from running instance
    /// </summary>
    /// <returns>Clear response or null if request failed</returns>
    Task<ClearResponse?> ClearAsync();

    /// <summary>
    /// Get telemetry and file statistics from running instance
    /// </summary>
    /// <returns>Stats response or null if not running</returns>
    Task<StatsResponse?> GetStatsAsync();
}

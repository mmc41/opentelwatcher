using OpenTelWatcher.CLI.Models;

namespace OpenTelWatcher.CLI.Services;

/// <summary>
/// Client for communicating with watcher HTTP API
/// </summary>
public interface IOpenTelWatcherApiClient
{
    /// <summary>
    /// Get application status from running instance (version, diagnostics, and statistics)
    /// </summary>
    /// <returns>Status response or null if not running</returns>
    Task<StatusResponse?> GetStatusAsync();

    /// <summary>
    /// Get application information from running instance (deprecated - use GetStatusAsync)
    /// </summary>
    /// <returns>Info response or null if not running</returns>
    [Obsolete("Use GetStatusAsync instead")]
    Task<InfoResponse?> GetInfoAsync();

    /// <summary>
    /// Get instance status including compatibility check
    /// </summary>
    /// <param name="cliVersion">CLI version for compatibility check</param>
    /// <returns>Instance status</returns>
    Task<InstanceStatus> GetInstanceStatusAsync(Version cliVersion);

    /// <summary>
    /// Send stop command to running instance
    /// </summary>
    /// <returns>True if stop initiated successfully</returns>
    Task<bool> StopAsync();

    /// <summary>
    /// Send shutdown command to running instance (deprecated - use StopAsync)
    /// </summary>
    /// <returns>True if shutdown initiated successfully</returns>
    [Obsolete("Use StopAsync instead")]
    Task<bool> ShutdownAsync();

    /// <summary>
    /// Wait for instance to stop (connection refused)
    /// </summary>
    /// <param name="timeoutSeconds">Maximum wait time in seconds</param>
    /// <returns>True if stopped, false if timeout</returns>
    Task<bool> WaitForStopAsync(int timeoutSeconds = 30);

    /// <summary>
    /// Wait for instance to stop (deprecated - use WaitForStopAsync)
    /// </summary>
    /// <param name="timeoutSeconds">Maximum wait time in seconds</param>
    /// <returns>True if stopped, false if timeout</returns>
    [Obsolete("Use WaitForStopAsync instead")]
    Task<bool> WaitForShutdownAsync(int timeoutSeconds = 30);

    /// <summary>
    /// Clear all telemetry files from running instance
    /// </summary>
    /// <returns>Clear response or null if request failed</returns>
    Task<ClearResponse?> ClearAsync();

    /// <summary>
    /// Get telemetry and file statistics from running instance (deprecated - use GetStatusAsync)
    /// </summary>
    /// <returns>Stats response or null if not running</returns>
    [Obsolete("Use GetStatusAsync instead. Statistics are now included in the status response.")]
    Task<StatsResponse?> GetStatsAsync();
}

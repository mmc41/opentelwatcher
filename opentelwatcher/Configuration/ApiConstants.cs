namespace OpenTelWatcher.Configuration;

/// <summary>
/// Constants for API and network operations.
/// </summary>
public static class ApiConstants
{
    /// <summary>
    /// Timeout constants for various operations.
    /// </summary>
    public static class Timeouts
    {
        /// <summary>
        /// Timeout for API requests to check if instance is running (3 seconds).
        /// </summary>
        public const int ApiRequestSeconds = 3;

        /// <summary>
        /// Timeout for health check when starting daemon (10 seconds).
        /// </summary>
        public const int HealthCheckSeconds = 10;

        /// <summary>
        /// Timeout for waiting for graceful shutdown (30 seconds).
        /// </summary>
        public const int ShutdownWaitSeconds = 30;

        /// <summary>
        /// Polling interval for health checks (500 milliseconds).
        /// </summary>
        public const int HealthCheckPollIntervalMs = 500;
    }

    /// <summary>
    /// Network-related constants.
    /// </summary>
    public static class Network
    {
        /// <summary>
        /// Localhost IP address for consistent endpoint usage (127.0.0.1).
        /// </summary>
        public const string LocalhostIp = "127.0.0.1";
    }

    /// <summary>
    /// Disk space constants for file operations.
    /// </summary>
    public static class DiskSpace
    {
        /// <summary>
        /// Minimum free space buffer to maintain when writing files (100 MB).
        /// </summary>
        public const long MinFreeSpaceBytes = 100 * 1024 * 1024;
    }
}

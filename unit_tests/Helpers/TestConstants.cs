namespace UnitTests.Helpers;

/// <summary>
/// Centralized test constants to avoid magic numbers and improve maintainability.
/// </summary>
public static class TestConstants
{
    /// <summary>
    /// Default configuration values for OpenTelWatcherOptions in tests.
    /// </summary>
    public static class DefaultConfig
    {
        public const int MaxFileSizeMB = 100;
        public const int MaxConsecutiveFileErrors = 10;
        public const int MaxErrorHistorySize = 50;
        public const bool PrettyPrint = false;
    }

    /// <summary>
    /// Test server and network configuration.
    /// </summary>
    public static class Network
    {
        public const int DefaultPort = 4318;
        public const int AlternativePort = 5000;
        public const int ThirdPort = 4319;
        public const string DefaultHost = "127.0.0.1";
        public const string DefaultBaseUrl = "http://127.0.0.1:4318";
    }

    /// <summary>
    /// Test process identifiers.
    /// </summary>
    public static class ProcessIds
    {
        public const int DefaultTestPid = 1234;
        public const int AlternativeTestPid = 99999;
        public const int ThirdTestPid = 88888;
        public const int TestPidForConcurrency = 10000; // Starting point for concurrent tests
    }

    /// <summary>
    /// Test version numbers.
    /// </summary>
    public static class Versions
    {
        public const int MajorVersion = 1;
        public const int MinorVersion = 0;
        public const int PatchVersion = 0;
        public const string VersionString = "1.0.0";
        public const string IncompatibleVersionString = "2.0.0";
    }

    /// <summary>
    /// File size constants for testing.
    /// </summary>
    public static class FileSizes
    {
        public const int OneKB = 1024;
        public const int OneMB = 1048576; // 1024 * 1024
        public const int SmallTestFileSizeBytes = 1000;
        public const int LargeTestFileSizeBytes = 1100000; // Slightly over 1MB
    }

    /// <summary>
    /// Timing constants for tests.
    /// </summary>
    public static class Timing
    {
        public const int DefaultUptimeSeconds = 3600; // 1 hour
        public const int OneSecondMs = 1000;
        public const int OneMillisecond = 1;
    }

    /// <summary>
    /// Test file patterns and names.
    /// </summary>
    public static class FileNames
    {
        public const string PidFileName = "opentelwatcher.pid";
        public const string DefaultOutputDirectory = "./telemetry-data";
        public const string ErrorsFilePattern = "*.errors.ndjson";
        public const string NdjsonPattern = "*.ndjson";
    }
}

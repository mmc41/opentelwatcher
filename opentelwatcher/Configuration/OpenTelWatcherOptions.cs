namespace OpenTelWatcher.Configuration;

/// <summary>
/// Configuration options for the OpenTelWatcher service.
/// </summary>
public class OpenTelWatcherOptions
{
    /// <summary>
    /// Directory where telemetry files will be written.
    /// Default: "./telemetry-data"
    /// </summary>
    public string OutputDirectory { get; set; } = "./telemetry-data";

    /// <summary>
    /// Maximum file size in megabytes before rotation.
    /// Default: 100 MB
    /// </summary>
    public int MaxFileSizeMB { get; set; } = 100;

    /// <summary>
    /// Enable pretty-printing (indented) JSON output for readability.
    /// Default: false
    /// </summary>
    public bool PrettyPrint { get; set; } = false;

    /// <summary>
    /// Maximum number of error events to retain in memory.
    /// Valid range: 10-1000
    /// Default: 50
    /// </summary>
    public int MaxErrorHistorySize { get; set; } = 50;

    /// <summary>
    /// Number of consecutive file write failures before entering degraded state.
    /// Valid range: 3-100
    /// Default: 10
    /// </summary>
    public int MaxConsecutiveFileErrors { get; set; } = 10;

    /// <summary>
    /// HTTP request timeout in seconds.
    /// Default: 30 seconds
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;
}

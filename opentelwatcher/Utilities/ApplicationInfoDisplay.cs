using OpenTelWatcher.Configuration;

namespace OpenTelWatcher.Utilities;

/// <summary>
/// Display mode for application information
/// </summary>
public enum DisplayMode
{
    /// <summary>
    /// Startup banner mode - shown when the application starts
    /// </summary>
    Startup,

    /// <summary>
    /// Info command mode - shown when user runs 'watcher info'
    /// </summary>
    Info,

    /// <summary>
    /// Stop command mode - shown when user runs 'watcher stop'
    /// </summary>
    Stop,

    /// <summary>
    /// Error mode - shown when an error occurs (e.g., instance already running)
    /// </summary>
    Error
}

/// <summary>
/// Configuration for displaying application information
/// </summary>
public class ApplicationInfoConfig
{
    public required string Version { get; init; }
    public int Port { get; init; } // Only used in Startup mode
    public required string OutputDirectory { get; init; }
    public int? ProcessId { get; init; }
    public string? PidFilePath { get; init; }
    public bool Silent { get; init; }
    public bool Verbose { get; init; }
    public string? HealthStatus { get; init; }
    public int? ConsecutiveErrors { get; init; }
    public List<string>? RecentErrors { get; init; }
    public int? FileCount { get; init; }
    public long? TotalFileSize { get; init; }
    public string? ErrorMessage { get; init; } // Error message for Error mode
    public string? ErrorDetails { get; init; } // Additional error details
}

/// <summary>
/// Utility class for displaying application information in a consistent format
/// </summary>
public static class ApplicationInfoDisplay
{
    public static void Display(DisplayMode mode, ApplicationInfoConfig config)
    {
        // Silent mode: suppress all output except errors
        if (config.Silent && mode != DisplayMode.Error)
        {
            return;
        }

        var urls = $"http://{ApiConstants.Network.LocalhostIp}:{config.Port}";
        var outputDir = Path.GetFullPath(config.OutputDirectory);

        // Header (all modes)
        Console.WriteLine();
        Console.WriteLine("================================================================================");
        Console.WriteLine($"\x1b[1mOpenTelWatcher v{config.Version} - OTLP/HTTP Receiver\x1b[0m");
        Console.WriteLine("https://github.com/mmc41/opentelwatcher");
        Console.WriteLine("================================================================================");

        // Error mode specific display
        if (mode == DisplayMode.Error)
        {
            if (!string.IsNullOrEmpty(config.ErrorMessage))
            {
                var orgColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {config.ErrorMessage}");
                Console.ForegroundColor = orgColor;
            }
            if (!string.IsNullOrEmpty(config.ErrorDetails))
            {
                Console.WriteLine(config.ErrorDetails);
            }
            Console.WriteLine("================================================================================");
            return; // Exit early for error mode
        }

        // Stop mode specific header
        if (mode == DisplayMode.Stop)
        {
            Console.WriteLine("Stopping service...");
            Console.WriteLine();
        }

        // Process ID (both modes, shown as "Daemon processId" if available)
        if (config.ProcessId.HasValue)
        {
            Console.WriteLine($"Daemon processId: \x1b[1m{config.ProcessId.Value}\x1b[0m");
            Console.WriteLine();
        }

        // Startup-specific verbose info
        if (mode == DisplayMode.Startup && config.Verbose)
        {
            Console.WriteLine($"Status Dashboard: \x1b[1m{urls}/\x1b[0m");
            Console.WriteLine($"API Documentation: \x1b[1m{urls}/swagger\x1b[0m");
            if (config.PidFilePath != null)
            {
                Console.WriteLine($"PID File: \x1b[1m{Path.GetFullPath(config.PidFilePath)}\x1b[0m");
            }
            Console.WriteLine();
        }

        // OTLP endpoints (startup and info modes only, not stop)
        if (mode != DisplayMode.Stop)
        {
            Console.WriteLine($"OTLP Endpoints:");
            Console.WriteLine($"  - Traces:  \x1b[1m{urls}/v1/traces\x1b[0m");
            Console.WriteLine($"  - Logs:    \x1b[1m{urls}/v1/logs\x1b[0m");
            Console.WriteLine($"  - Metrics: \x1b[1m{urls}/v1/metrics\x1b[0m");
            Console.WriteLine();
        }

        // Health status and errors (info mode only)
        if (mode == DisplayMode.Info)
        {
            if (config.HealthStatus != null)
            {
                Console.WriteLine($"Health Status: \x1b[1m{config.HealthStatus}\x1b[0m");
            }
            if (config.ConsecutiveErrors.HasValue)
            {
                Console.WriteLine($"Consecutive Errors: \x1b[1m{config.ConsecutiveErrors.Value}\x1b[0m");
            }
            Console.WriteLine();

            // Display recent errors only with --verbose
            if (config.Verbose && config.RecentErrors != null && config.RecentErrors.Count > 0)
            {
                Console.WriteLine($"Recent Errors ({config.RecentErrors.Count}):");
                foreach (var error in config.RecentErrors.Take(5))
                {
                    Console.WriteLine($"  {error}");
                }
                Console.WriteLine();
            }
        }

        // File statistics
        // - Startup mode: always show as "Existing files" (files already on disk)
        // - Info mode: always show as "Files"
        // - Stop mode: only show with --verbose
        bool shouldShowFiles = (mode == DisplayMode.Startup) ||
                               (mode == DisplayMode.Info) ||
                               (mode == DisplayMode.Stop && config.Verbose);

        if (shouldShowFiles && config.FileCount.HasValue && config.TotalFileSize.HasValue)
        {
            var formattedSize = FormatBytes(config.TotalFileSize.Value);
            var filesLabel = mode == DisplayMode.Startup ? "Existing files" : "Files";
            Console.WriteLine($"{filesLabel}: \x1b[1m{config.FileCount.Value} file(s), {formattedSize} total\x1b[0m");
            Console.WriteLine();
        }

        // Output directory
        // - Startup and Info modes: always show
        // - Stop mode: only show with --verbose
        bool shouldShowOutputDir = (mode == DisplayMode.Startup) ||
                                   (mode == DisplayMode.Info) ||
                                   (mode == DisplayMode.Stop && config.Verbose);

        if (shouldShowOutputDir)
        {
            Console.WriteLine($"Output Directory: \x1b[1m{outputDir}\x1b[0m");
            Console.WriteLine();
        }

        // Mode-specific footer
        if (mode == DisplayMode.Startup)
        {
            Console.WriteLine("Stop server with CTRL-C or type 'watcher stop' in a seperate shell");
            Console.WriteLine("================================================================================");
            var orgColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine("WARNING: No authentication enabled. For local development use only.");
            Console.ForegroundColor = orgColor;
            Console.WriteLine("================================================================================");
            Console.WriteLine();
        }
        else // Info and Stop modes
        {
            Console.WriteLine("================================================================================");

            // Stop mode shows waiting message
            if (mode == DisplayMode.Stop)
            {
                Console.WriteLine();
                Console.Write("Waiting for service to stop");
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}

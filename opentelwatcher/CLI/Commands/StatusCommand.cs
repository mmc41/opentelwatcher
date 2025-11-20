using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.CLI.Services;
using OpenTelWatcher.Services.Interfaces;
using OpenTelWatcher.Utilities;

namespace OpenTelWatcher.CLI.Commands;

/// <summary>
/// Executes status/health checking business logic for the 'opentelwatcher status' command in dual modes.
/// API mode (default): Queries GET /api/status for health, version, config, file stats, telemetry counts, errors.
/// Filesystem mode (--output-dir specified or no instance running): Scans directory for *.errors.ndjson files.
/// Supports filtered displays (--errors-only, --stats-only, --verbose, --quiet). Returns exit code 0 (healthy),
/// 1 (errors detected), or 2 (system error). StatusCommandBuilder creates CLI structure; this class handles
/// mode selection, HTTP queries, filesystem scanning, and multi-format output.
/// </summary>
/// <remarks>
/// Scope: Health checks, error detection, statistics aggregation, dual-mode coordination, exit code mapping.
/// Builder: StatusCommandBuilder resolves port/fallback → This class: Queries API or scans filesystem
/// </remarks>
public sealed class StatusCommand
{
    private readonly IOpenTelWatcherApiClient _apiClient;
    private readonly IPortResolver _portResolver;
    private readonly ILogger<StatusCommand> _logger;
    private readonly IErrorFileScanner _errorFileScanner;

    public StatusCommand(
        IOpenTelWatcherApiClient apiClient,
        IPortResolver portResolver,
        ILogger<StatusCommand> logger,
        IErrorFileScanner errorFileScanner)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _portResolver = portResolver ?? throw new ArgumentNullException(nameof(portResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _errorFileScanner = errorFileScanner ?? throw new ArgumentNullException(nameof(errorFileScanner));
    }

    public async Task<CommandResult> ExecuteAsync(StatusOptions options, bool jsonOutput = false)
    {
        var result = new Dictionary<string, object>();
        var cliVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

        // Determine mode: API or Filesystem
        // If --output-dir is specified, force filesystem mode
        // Otherwise, try API mode first (with port resolution if needed)

        if (!string.IsNullOrWhiteSpace(options.OutputDir))
        {
            // Explicit filesystem mode
            return await ExecuteFilesystemModeAsync(options.OutputDir, options.Quiet, jsonOutput);
        }

        // Note: Port resolution now happens in CliApplication before this command is invoked
        // The HttpClient is already configured with the correct port

        // Try API mode
        var status = await _apiClient.GetInstanceStatusAsync(cliVersion);
        if (!status.IsRunning)
        {
            // No instance running - this is an error for API mode
            // (User can use --output-dir to force filesystem mode)
            return BuildNoInstanceRunningResult(result, options.Quiet, jsonOutput);
        }

        // API mode - get full status
        var info = await _apiClient.GetStatusAsync();
        if (info is null)
        {
            return BuildFailedToRetrieveInfoResult(result, options.Quiet, jsonOutput);
        }

        // Build result based on flags
        if (options.ErrorsOnly)
        {
            return BuildErrorsOnlyResult(result, status, info, options.Verbose, options.Quiet, jsonOutput);
        }
        else if (options.StatsOnly)
        {
            return BuildStatsOnlyResult(result, info, options.Quiet, jsonOutput);
        }
        else if (options.Verbose)
        {
            return BuildVerboseResult(result, status, info, options.Quiet, jsonOutput);
        }
        else
        {
            // Default: Full diagnostic information (InfoCommand style)
            return BuildFullInfoResult(result, status, info, options.Quiet, jsonOutput);
        }
    }

    /// <summary>
    /// Filesystem mode: Scan directory for error files (CheckCommand functionality)
    /// </summary>
    private async Task<CommandResult> ExecuteFilesystemModeAsync(string outputDir, bool quiet, bool jsonOutput)
    {
        var result = new Dictionary<string, object>();

        // Check if directory exists - this is a system error, not a "no errors" state
        if (!Directory.Exists(outputDir))
        {
            var message = $"Output directory does not exist: {outputDir}";

            result["success"] = false;
            result["error"] = "DirectoryNotFound";
            result["message"] = message;
            result["outputDirectory"] = outputDir;

            if (jsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (!quiet)
            {
                Console.WriteLine($"Error: {message}.");
                Console.WriteLine();
                Console.WriteLine("Hint: Ensure the output directory exists before running status check.");
            }

            return await Task.FromResult(CommandResult.SystemError(message));
        }

        // Find all error files
        var errorFiles = Directory.GetFiles(outputDir, "*.errors.ndjson")
            .Select(Path.GetFileName)
            .Where(name => name != null)
            .Cast<string>()
            .OrderBy(name => name)
            .ToList();

        var hasErrors = errorFiles.Count > 0;

        result["success"] = true;
        result["mode"] = "filesystem";
        result["hasErrors"] = hasErrors;
        result["errorFileCount"] = errorFiles.Count;
        result["errorFiles"] = errorFiles;
        result["outputDirectory"] = outputDir;

        OutputFilesystemResult(result, jsonOutput, quiet, hasErrors);

        return hasErrors
            ? await Task.FromResult(CommandResult.UserError("Errors detected"))
            : await Task.FromResult(CommandResult.Success("No errors"));
    }

    private CommandResult BuildNoInstanceRunningResult(Dictionary<string, object> result, bool quiet, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "No instance running";
        result["message"] = "No OpenTelWatcher instance is currently running.";
        result["hint"] = "Use --output-dir to scan a directory for errors without a running instance.";

        OutputResult(result, jsonOutput, quiet, isError: true, errorType: "No instance running");
        return CommandResult.SystemError("No instance running");
    }

    private CommandResult BuildFailedToRetrieveInfoResult(Dictionary<string, object> result, bool quiet, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Failed to retrieve application information";

        OutputResult(result, jsonOutput, quiet, isError: true, errorType: "Failed to retrieve info");
        return CommandResult.SystemError("Failed to retrieve info");
    }

    /// <summary>
    /// Default mode: Full diagnostic information (InfoCommand style)
    /// </summary>
    private CommandResult BuildFullInfoResult(Dictionary<string, object> result, InstanceStatus status, StatusResponse info, bool quiet, bool jsonOutput)
    {
        var errorFileCount = _errorFileScanner.CountErrorFiles(info.Configuration.OutputDirectory);
        var healthy = errorFileCount == 0;

        result["success"] = true;
        result["mode"] = "api";
        result["healthy"] = healthy;
        result["compatible"] = status.IsCompatible;
        if (!status.IsCompatible)
        {
            result["incompatibilityReason"] = status.IncompatibilityReason!;
        }
        result["version"] = info.Version;
        result["port"] = info.Port;
        result["processId"] = info.ProcessId;
        result["uptimeSeconds"] = info.UptimeSeconds;
        result["outputDirectory"] = info.Configuration.OutputDirectory;
        result["fileCount"] = info.Files.Count;
        result["totalSizeBytes"] = info.Files.TotalSizeBytes;
        result["errorFileCount"] = errorFileCount;
        result["healthStatus"] = info.Health.Status;
        result["consecutiveErrors"] = info.Health.ConsecutiveErrors;
        result["recentErrors"] = info.Health.RecentErrors;

        OutputResult(result, jsonOutput, quiet, isError: false, outputMode: "full", status: status, info: info);

        return healthy
            ? CommandResult.Success("Healthy")
            : CommandResult.UserError("Unhealthy");
    }

    /// <summary>
    /// --errors-only mode: Show only error-related information
    /// </summary>
    private CommandResult BuildErrorsOnlyResult(Dictionary<string, object> result, InstanceStatus status, StatusResponse info, bool verbose, bool quiet, bool jsonOutput)
    {
        var errorFileCount = _errorFileScanner.CountErrorFiles(info.Configuration.OutputDirectory);
        var healthy = errorFileCount == 0;

        result["success"] = true;
        result["mode"] = "api";
        result["healthy"] = healthy;
        result["errorFileCount"] = errorFileCount;
        result["healthStatus"] = info.Health.Status;
        result["consecutiveErrors"] = info.Health.ConsecutiveErrors;
        result["recentErrors"] = info.Health.RecentErrors;
        result["outputDirectory"] = info.Configuration.OutputDirectory;

        if (verbose)
        {
            // Include error file list
            var errorFiles = _errorFileScanner.GetErrorFiles(info.Configuration.OutputDirectory);
            result["errorFiles"] = errorFiles;
        }

        OutputResult(result, jsonOutput, quiet, isError: false, outputMode: "errors", status: status, info: info, verbose: verbose);

        return healthy
            ? CommandResult.Success("No errors")
            : CommandResult.UserError("Errors detected");
    }

    /// <summary>
    /// --stats-only mode: Show only telemetry and file statistics
    /// </summary>
    private CommandResult BuildStatsOnlyResult(Dictionary<string, object> result, StatusResponse info, bool quiet, bool jsonOutput)
    {
        result["success"] = true;
        result["mode"] = "api";
        result["telemetry"] = new Dictionary<string, object>
        {
            ["traces"] = new Dictionary<string, object> { ["requests"] = info.Telemetry.Traces.Requests },
            ["logs"] = new Dictionary<string, object> { ["requests"] = info.Telemetry.Logs.Requests },
            ["metrics"] = new Dictionary<string, object> { ["requests"] = info.Telemetry.Metrics.Requests }
        };
        result["files"] = new Dictionary<string, object>
        {
            ["count"] = info.Files.Count,
            ["totalSizeBytes"] = info.Files.TotalSizeBytes,
            ["breakdown"] = new Dictionary<string, object>
            {
                ["traces"] = new Dictionary<string, object>
                {
                    ["count"] = info.Files.Breakdown.Traces.Count,
                    ["sizeBytes"] = info.Files.Breakdown.Traces.SizeBytes
                },
                ["logs"] = new Dictionary<string, object>
                {
                    ["count"] = info.Files.Breakdown.Logs.Count,
                    ["sizeBytes"] = info.Files.Breakdown.Logs.SizeBytes
                },
                ["metrics"] = new Dictionary<string, object>
                {
                    ["count"] = info.Files.Breakdown.Metrics.Count,
                    ["sizeBytes"] = info.Files.Breakdown.Metrics.SizeBytes
                }
            }
        };
        result["uptimeSeconds"] = info.UptimeSeconds;

        OutputResult(result, jsonOutput, quiet, isError: false, outputMode: "stats", info: info);
        return CommandResult.Success("Stats retrieved");
    }

    /// <summary>
    /// --verbose mode: Show detailed diagnostic information
    /// </summary>
    private CommandResult BuildVerboseResult(Dictionary<string, object> result, InstanceStatus status, StatusResponse info, bool quiet, bool jsonOutput)
    {
        var errorFileCount = _errorFileScanner.CountErrorFiles(info.Configuration.OutputDirectory);
        var healthy = errorFileCount == 0;

        result["success"] = true;
        result["mode"] = "api";
        result["healthy"] = healthy;
        result["compatible"] = status.IsCompatible;
        if (!status.IsCompatible)
        {
            result["incompatibilityReason"] = status.IncompatibilityReason!;
        }
        result["version"] = info.Version;
        result["port"] = info.Port;
        result["processId"] = info.ProcessId;
        result["uptimeSeconds"] = info.UptimeSeconds;
        result["outputDirectory"] = info.Configuration.OutputDirectory;
        result["errorFileCount"] = errorFileCount;
        result["errorFiles"] = _errorFileScanner.GetErrorFiles(info.Configuration.OutputDirectory);
        result["healthStatus"] = info.Health.Status;
        result["consecutiveErrors"] = info.Health.ConsecutiveErrors;
        result["recentErrors"] = info.Health.RecentErrors;
        result["telemetry"] = new Dictionary<string, object>
        {
            ["traces"] = new Dictionary<string, object> { ["requests"] = info.Telemetry.Traces.Requests },
            ["logs"] = new Dictionary<string, object> { ["requests"] = info.Telemetry.Logs.Requests },
            ["metrics"] = new Dictionary<string, object> { ["requests"] = info.Telemetry.Metrics.Requests }
        };
        result["files"] = new Dictionary<string, object>
        {
            ["count"] = info.Files.Count,
            ["totalSizeBytes"] = info.Files.TotalSizeBytes,
            ["breakdown"] = new Dictionary<string, object>
            {
                ["traces"] = new Dictionary<string, object>
                {
                    ["count"] = info.Files.Breakdown.Traces.Count,
                    ["sizeBytes"] = info.Files.Breakdown.Traces.SizeBytes
                },
                ["logs"] = new Dictionary<string, object>
                {
                    ["count"] = info.Files.Breakdown.Logs.Count,
                    ["sizeBytes"] = info.Files.Breakdown.Logs.SizeBytes
                },
                ["metrics"] = new Dictionary<string, object>
                {
                    ["count"] = info.Files.Breakdown.Metrics.Count,
                    ["sizeBytes"] = info.Files.Breakdown.Metrics.SizeBytes
                }
            }
        };
        result["configuration"] = info.Configuration;

        OutputResult(result, jsonOutput, quiet, isError: false, outputMode: "verbose", status: status, info: info);

        return healthy
            ? CommandResult.Success("Healthy")
            : CommandResult.UserError("Unhealthy");
    }

    private void OutputFilesystemResult(Dictionary<string, object> result, bool jsonOutput, bool quiet, bool hasErrors)
    {
        if (jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        if (quiet)
        {
            return;
        }

        var errorFileCount = (int)result["errorFileCount"];
        var outputDir = (string)result["outputDirectory"];

        if (!hasErrors)
        {
            Console.WriteLine($"✓ No error files found in {outputDir}.");
        }
        else
        {
            Console.WriteLine($"✗ {errorFileCount} error file{(errorFileCount != 1 ? "s" : "")} detected in {outputDir}.");
            var errorFiles = (List<string>)result["errorFiles"];
            foreach (var file in errorFiles)
            {
                Console.WriteLine($"  - {file}");
            }
        }
    }

    private void OutputResult(
        Dictionary<string, object> result,
        bool jsonOutput,
        bool quiet,
        bool isError,
        string? errorType = null,
        string? outputMode = null,
        InstanceStatus? status = null,
        StatusResponse? info = null,
        bool verbose = false)
    {
        CommandOutputFormatter.Output(result, jsonOutput, quiet, _ =>
        {
            if (isError)
            {
                OutputErrorText(result, errorType!);
            }
            else
            {
                switch (outputMode)
                {
                    case "full":
                        OutputFullInfoText(result, status!, info!);
                        break;
                    case "errors":
                        OutputErrorsOnlyText(result, verbose);
                        break;
                    case "stats":
                        OutputStatsOnlyText(result);
                        break;
                    case "verbose":
                        OutputVerboseText(result, status!, info!);
                        break;
                }
            }
        });
    }

    private void OutputErrorText(Dictionary<string, object> result, string errorType)
    {
        switch (errorType)
        {
            case "No instance running":
                Console.WriteLine((string)result["message"]);
                if (result.ContainsKey("hint"))
                {
                    Console.WriteLine();
                    Console.WriteLine($"Hint: {result["hint"]}");
                }
                break;

            case "Failed to retrieve info":
                Console.WriteLine($"Error: {result["error"]}.");
                break;
        }
    }

    private void OutputFullInfoText(Dictionary<string, object> result, InstanceStatus status, StatusResponse info)
    {
        if (!status.IsCompatible)
        {
            Console.WriteLine("Warning: Incompatible version detected.");
            Console.WriteLine($"  {result["incompatibilityReason"]}");
            Console.WriteLine();
            Console.WriteLine("Information may be unreliable.");
            Console.WriteLine();
        }

        var config = new ApplicationInfoConfig
        {
            Version = info.Version,
            Port = info.Port,
            OutputDirectory = info.Configuration.OutputDirectory,
            ProcessId = info.ProcessId,
            HealthStatus = info.Health.Status,
            ConsecutiveErrors = info.Health.ConsecutiveErrors,
            RecentErrors = info.Health.RecentErrors,
            FileCount = info.Files.Count,
            TotalFileSize = info.Files.TotalSizeBytes,
            Silent = false,
            Verbose = false
        };

        ApplicationInfoDisplay.Display(DisplayMode.Info, config);
    }

    private void OutputErrorsOnlyText(Dictionary<string, object> result, bool verbose)
    {
        var healthy = (bool)result["healthy"];
        var errorFileCount = (int)result["errorFileCount"];
        var healthStatus = (string)result["healthStatus"];
        var consecutiveErrors = (int)result["consecutiveErrors"];
        var recentErrors = (int)result["recentErrors"];

        var healthIcon = healthy ? "✓" : "✗";
        var healthText = healthy ? "Healthy" : "Unhealthy";

        Console.WriteLine($"{healthIcon} {healthText}.");
        Console.WriteLine();
        Console.WriteLine("Error Summary:");
        Console.WriteLine($"  Error files:         {errorFileCount}");
        Console.WriteLine($"  Health status:       {healthStatus}");
        Console.WriteLine($"  Consecutive errors:  {consecutiveErrors}");
        Console.WriteLine($"  Recent errors:       {recentErrors}");

        if (verbose && result.ContainsKey("errorFiles"))
        {
            var errorFiles = (List<string>)result["errorFiles"];
            if (errorFiles.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Error Files:");
                foreach (var file in errorFiles)
                {
                    Console.WriteLine($"  - {file}");
                }
            }
        }
    }

    private void OutputStatsOnlyText(Dictionary<string, object> result)
    {
        var telemetry = (Dictionary<string, object>)result["telemetry"];
        var files = (Dictionary<string, object>)result["files"];
        var uptimeSeconds = (long)result["uptimeSeconds"];

        var tracesReq = (long)((Dictionary<string, object>)telemetry["traces"])["requests"];
        var logsReq = (long)((Dictionary<string, object>)telemetry["logs"])["requests"];
        var metricsReq = (long)((Dictionary<string, object>)telemetry["metrics"])["requests"];

        var fileCount = (int)files["count"];
        var totalSize = (long)files["totalSizeBytes"];
        var breakdown = (Dictionary<string, object>)files["breakdown"];

        var tracesFiles = (Dictionary<string, object>)breakdown["traces"];
        var logsFiles = (Dictionary<string, object>)breakdown["logs"];
        var metricsFiles = (Dictionary<string, object>)breakdown["metrics"];

        var tracesCount = (int)tracesFiles["count"];
        var tracesSize = (long)tracesFiles["sizeBytes"];
        var logsCount = (int)logsFiles["count"];
        var logsSize = (long)logsFiles["sizeBytes"];
        var metricsCount = (int)metricsFiles["count"];
        var metricsSize = (long)metricsFiles["sizeBytes"];

        Console.WriteLine("Telemetry Statistics:");
        Console.WriteLine($"  Traces received:  {tracesReq,6} request{(tracesReq != 1 ? "s" : " ")}.");
        Console.WriteLine($"  Logs received:    {logsReq,6} request{(logsReq != 1 ? "s" : " ")}.");
        Console.WriteLine($"  Metrics received: {metricsReq,6} request{(metricsReq != 1 ? "s" : " ")}.");
        Console.WriteLine();
        Console.WriteLine($"Files: {fileCount} ({NumberFormatter.FormatBytes(totalSize)}).");
        Console.WriteLine($"  traces:  {tracesCount,3} file{(tracesCount != 1 ? "s" : " ")} ({NumberFormatter.FormatBytes(tracesSize)}).");
        Console.WriteLine($"  logs:    {logsCount,3} file{(logsCount != 1 ? "s" : " ")} ({NumberFormatter.FormatBytes(logsSize)}).");
        Console.WriteLine($"  metrics: {metricsCount,3} file{(metricsCount != 1 ? "s" : " ")} ({NumberFormatter.FormatBytes(metricsSize)}).");
        Console.WriteLine();
        Console.WriteLine($"Uptime: {UptimeFormatter.FormatUptime(TimeSpan.FromSeconds(uptimeSeconds))}.");
    }

    private void OutputVerboseText(Dictionary<string, object> result, InstanceStatus status, StatusResponse info)
    {
        if (!status.IsCompatible)
        {
            Console.WriteLine("Warning: Incompatible version detected.");
            Console.WriteLine($"  {result["incompatibilityReason"]}");
            Console.WriteLine();
        }

        var config = new ApplicationInfoConfig
        {
            Version = info.Version,
            Port = info.Port,
            OutputDirectory = info.Configuration.OutputDirectory,
            ProcessId = info.ProcessId,
            HealthStatus = info.Health.Status,
            ConsecutiveErrors = info.Health.ConsecutiveErrors,
            RecentErrors = info.Health.RecentErrors,
            FileCount = info.Files.Count,
            TotalFileSize = info.Files.TotalSizeBytes,
            Silent = false,
            Verbose = true
        };

        ApplicationInfoDisplay.Display(DisplayMode.Info, config);
    }
}

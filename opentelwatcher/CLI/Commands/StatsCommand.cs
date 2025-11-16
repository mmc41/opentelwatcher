using System.Reflection;
using System.Text.Json;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.CLI.Services;
using OpenTelWatcher.Utilities;

namespace OpenTelWatcher.CLI.Commands;

/// <summary>
/// Stats command - provides telemetry and file statistics
/// </summary>
public sealed class StatsCommand
{
    private readonly IOpenTelWatcherApiClient _apiClient;

    public StatsCommand(IOpenTelWatcherApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<CommandResult> ExecuteAsync(bool silent = false, bool jsonOutput = false)
    {
        var result = new Dictionary<string, object>();
        var cliVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

        // Step 1: Check if instance is running
        var status = await _apiClient.GetInstanceStatusAsync(cliVersion);
        if (!status.IsRunning)
        {
            return BuildNoInstanceRunningResult(result, silent, jsonOutput);
        }

        // Step 2: Get stats
        var stats = await _apiClient.GetStatsAsync();
        if (stats is null)
        {
            return BuildFailedToRetrieveStatsResult(result, silent, jsonOutput);
        }

        // Step 3: Build success result
        return BuildSuccessResult(result, stats, silent, jsonOutput);
    }

    private CommandResult BuildNoInstanceRunningResult(Dictionary<string, object> result, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "No instance running";
        result["message"] = "No OpenTelWatcher instance is currently running.";

        OutputResult(result, jsonOutput, silent, isError: true, errorType: "No instance running");
        return CommandResult.SystemError("No instance running");
    }

    private CommandResult BuildFailedToRetrieveStatsResult(Dictionary<string, object> result, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Failed to retrieve statistics";

        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Failed to retrieve stats");
        return CommandResult.SystemError("Failed to retrieve stats");
    }

    private CommandResult BuildSuccessResult(Dictionary<string, object> result, StatsResponse stats, bool silent, bool jsonOutput)
    {
        result["success"] = true;
        result["telemetry"] = new Dictionary<string, object>
        {
            ["traces"] = new Dictionary<string, object> { ["requests"] = stats.Telemetry.Traces.Requests },
            ["logs"] = new Dictionary<string, object> { ["requests"] = stats.Telemetry.Logs.Requests },
            ["metrics"] = new Dictionary<string, object> { ["requests"] = stats.Telemetry.Metrics.Requests }
        };
        result["files"] = new Dictionary<string, object>
        {
            ["traces"] = new Dictionary<string, object>
            {
                ["count"] = stats.Files.Traces.Count,
                ["sizeBytes"] = stats.Files.Traces.SizeBytes
            },
            ["logs"] = new Dictionary<string, object>
            {
                ["count"] = stats.Files.Logs.Count,
                ["sizeBytes"] = stats.Files.Logs.SizeBytes
            },
            ["metrics"] = new Dictionary<string, object>
            {
                ["count"] = stats.Files.Metrics.Count,
                ["sizeBytes"] = stats.Files.Metrics.SizeBytes
            }
        };
        result["uptimeSeconds"] = stats.UptimeSeconds;

        OutputResult(result, jsonOutput, silent, isError: false);
        return CommandResult.Success("Stats retrieved");
    }

    private void OutputResult(Dictionary<string, object> result, bool jsonOutput, bool silent, bool isError, string? errorType = null)
    {
        if (jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        if (silent)
        {
            return;
        }

        if (isError)
        {
            OutputErrorText(result, errorType!);
        }
        else
        {
            OutputSuccessText(result);
        }
    }

    private void OutputErrorText(Dictionary<string, object> result, string errorType)
    {
        switch (errorType)
        {
            case "No instance running":
                Console.WriteLine((string)result["message"]);
                break;

            case "Failed to retrieve stats":
                Console.WriteLine($"Error: {result["error"]}");
                break;
        }
    }

    private void OutputSuccessText(Dictionary<string, object> result)
    {
        var telemetry = (Dictionary<string, object>)result["telemetry"];
        var files = (Dictionary<string, object>)result["files"];
        var uptimeSeconds = (long)result["uptimeSeconds"];

        var tracesReq = (long)((Dictionary<string, object>)telemetry["traces"])["requests"];
        var logsReq = (long)((Dictionary<string, object>)telemetry["logs"])["requests"];
        var metricsReq = (long)((Dictionary<string, object>)telemetry["metrics"])["requests"];

        var tracesFiles = (Dictionary<string, object>)files["traces"];
        var logsFiles = (Dictionary<string, object>)files["logs"];
        var metricsFiles = (Dictionary<string, object>)files["metrics"];

        var tracesCount = (int)tracesFiles["count"];
        var tracesSize = (long)tracesFiles["sizeBytes"];
        var logsCount = (int)logsFiles["count"];
        var logsSize = (long)logsFiles["sizeBytes"];
        var metricsCount = (int)metricsFiles["count"];
        var metricsSize = (long)metricsFiles["sizeBytes"];

        var totalFiles = tracesCount + logsCount + metricsCount;
        var totalSize = tracesSize + logsSize + metricsSize;

        Console.WriteLine("Telemetry Statistics:");
        Console.WriteLine($"  Traces received:  {tracesReq,6} request{(tracesReq != 1 ? "s" : " ")}");
        Console.WriteLine($"  Logs received:    {logsReq,6} request{(logsReq != 1 ? "s" : " ")}");
        Console.WriteLine($"  Metrics received: {metricsReq,6} request{(metricsReq != 1 ? "s" : " ")}");
        Console.WriteLine();
        Console.WriteLine($"Files: {totalFiles} ({NumberFormatter.FormatBytes(totalSize)})");
        Console.WriteLine($"  traces:  {tracesCount,3} file{(tracesCount != 1 ? "s" : " ")} ({NumberFormatter.FormatBytes(tracesSize)})");
        Console.WriteLine($"  logs:    {logsCount,3} file{(logsCount != 1 ? "s" : " ")} ({NumberFormatter.FormatBytes(logsSize)})");
        Console.WriteLine($"  metrics: {metricsCount,3} file{(metricsCount != 1 ? "s" : " ")} ({NumberFormatter.FormatBytes(metricsSize)})");
        Console.WriteLine();
        Console.WriteLine($"Uptime: {UptimeFormatter.FormatUptime(TimeSpan.FromSeconds(uptimeSeconds))}");
    }
}

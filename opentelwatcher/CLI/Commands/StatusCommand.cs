using System.Reflection;
using System.Text.Json;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.CLI.Services;
using OpenTelWatcher.Utilities;

namespace OpenTelWatcher.CLI.Commands;

/// <summary>
/// Status command - provides quick one-line health summary
/// </summary>
public sealed class StatusCommand
{
    private readonly IOpenTelWatcherApiClient _apiClient;

    public StatusCommand(IOpenTelWatcherApiClient apiClient)
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

        // Step 2: Get instance info
        var info = await _apiClient.GetInfoAsync();
        if (info is null)
        {
            return BuildFailedToRetrieveInfoResult(result, silent, jsonOutput);
        }

        // Step 3: Build success result
        return BuildSuccessResult(result, info, silent, jsonOutput);
    }

    private CommandResult BuildNoInstanceRunningResult(Dictionary<string, object> result, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "No instance running";
        result["message"] = "No OpenTelWatcher instance is currently running.";

        OutputResult(result, jsonOutput, silent, isError: true, errorType: "No instance running");
        return CommandResult.SystemError("No instance running");
    }

    private CommandResult BuildFailedToRetrieveInfoResult(Dictionary<string, object> result, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Failed to retrieve application information";

        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Failed to retrieve info");
        return CommandResult.SystemError("Failed to retrieve info");
    }

    private CommandResult BuildSuccessResult(Dictionary<string, object> result, InfoResponse info, bool silent, bool jsonOutput)
    {
        var fileCount = info.Files.Count;
        var totalSizeBytes = info.Files.TotalSizeBytes;
        var outputDirectory = info.Configuration.OutputDirectory;

        // Count error files by scanning the output directory
        var errorFileCount = CountErrorFiles(outputDirectory);
        var healthy = errorFileCount == 0;

        result["success"] = true;
        result["healthy"] = healthy;
        result["fileCount"] = fileCount;
        result["errorFileCount"] = errorFileCount;
        result["totalSizeBytes"] = totalSizeBytes;
        result["outputDirectory"] = outputDirectory;

        OutputResult(result, jsonOutput, silent, isError: false);

        // Return appropriate exit code
        return healthy
            ? CommandResult.Success("Healthy")
            : CommandResult.UserError("Unhealthy");
    }

    private int CountErrorFiles(string outputDirectory)
    {
        try
        {
            if (!Directory.Exists(outputDirectory))
            {
                return 0;
            }

            return Directory.GetFiles(outputDirectory, "*.errors.ndjson", SearchOption.TopDirectoryOnly).Length;
        }
        catch
        {
            return 0;
        }
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

            case "Failed to retrieve info":
                Console.WriteLine($"Error: {result["error"]}");
                break;
        }
    }

    private void OutputSuccessText(Dictionary<string, object> result)
    {
        var healthy = (bool)result["healthy"];
        var fileCount = (int)result["fileCount"];
        var errorFileCount = (int)result["errorFileCount"];
        var totalSizeBytes = (long)result["totalSizeBytes"];

        var healthIcon = healthy ? "✓" : "✗";
        var healthStatus = healthy ? "Healthy" : "Unhealthy";
        var totalSize = NumberFormatter.FormatBytes(totalSizeBytes);
        var errorInfo = healthy
            ? "No errors"
            : $"{errorFileCount} ERROR FILE{(errorFileCount != 1 ? "S" : "")} DETECTED";

        // One-line summary format
        Console.WriteLine($"{healthIcon} {healthStatus} | {fileCount} file{(fileCount != 1 ? "s" : "")} ({totalSize}) | {errorInfo}");
    }
}

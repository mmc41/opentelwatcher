using System.Reflection;
using System.Text.Json;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.CLI.Services;

namespace OpenTelWatcher.CLI.Commands;

/// <summary>
/// Info command - displays application information from running instance
/// </summary>
public sealed class InfoCommand
{
    private readonly IOpenTelWatcherApiClient _apiClient;

    public InfoCommand(IOpenTelWatcherApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<CommandResult> ExecuteAsync(bool verbose = false, bool silent = false, bool jsonOutput = false)
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

        // Step 3: Build success result with compatibility check
        return BuildSuccessResult(result, status, info, verbose, silent, jsonOutput);
    }

    private CommandResult BuildNoInstanceRunningResult(Dictionary<string, object> result, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "No instance running";
        result["message"] = "Start the service with 'opentelwatcher start' first.";
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "No instance running");
        return CommandResult.UserError("No instance running");
    }

    private CommandResult BuildFailedToRetrieveInfoResult(Dictionary<string, object> result, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Failed to retrieve application information";
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Failed to retrieve info");
        return CommandResult.SystemError("Failed to retrieve info");
    }

    private CommandResult BuildSuccessResult(Dictionary<string, object> result, InstanceStatus status, InfoResponse info, bool verbose, bool silent, bool jsonOutput)
    {
        result["success"] = true;
        result["compatible"] = status.IsCompatible;
        if (!status.IsCompatible)
        {
            result["incompatibilityReason"] = status.IncompatibilityReason!;
        }
        result["info"] = info;

        OutputResult(result, jsonOutput, silent, isError: false, info: info, verbose: verbose, isCompatible: status.IsCompatible);
        return CommandResult.Success("Information displayed", data: new Dictionary<string, object>
        {
            ["info"] = result
        });
    }

    private void OutputResult(Dictionary<string, object> result, bool jsonOutput, bool silent, bool isError, string? errorType = null, InfoResponse? info = null, bool verbose = false, bool isCompatible = true)
    {
        if (jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        if (isError)
        {
            OutputErrorText(result, errorType!);
        }
        else
        {
            OutputSuccessText(result, info!, verbose, silent, isCompatible);
        }
    }

    private void OutputErrorText(Dictionary<string, object> result, string errorType)
    {
        switch (errorType)
        {
            case "No instance running":
                Console.WriteLine((string)result["error"]);
                Console.WriteLine((string)result["message"]);
                break;

            case "Failed to retrieve info":
                Console.WriteLine((string)result["error"]);
                break;
        }
    }

    private void OutputSuccessText(Dictionary<string, object> result, InfoResponse info, bool verbose, bool silent, bool isCompatible)
    {
        if (!isCompatible)
        {
            Console.WriteLine("Warning: Incompatible version detected");
            Console.WriteLine($"  {result["incompatibilityReason"]}");
            Console.WriteLine();
            Console.WriteLine("Information may be unreliable.");
            Console.WriteLine();
        }

        var config = new OpenTelWatcher.Utilities.ApplicationInfoConfig
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
            Silent = silent,
            Verbose = verbose
        };

        OpenTelWatcher.Utilities.ApplicationInfoDisplay.Display(OpenTelWatcher.Utilities.DisplayMode.Info, config);
    }
}

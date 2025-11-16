using System.Reflection;
using System.Text.Json;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.CLI.Services;

namespace OpenTelWatcher.CLI.Commands;

/// <summary>
/// Shutdown command - stops running watcher instance
/// </summary>
public sealed class ShutdownCommand
{
    private readonly IOpenTelWatcherApiClient _apiClient;

    public ShutdownCommand(IOpenTelWatcherApiClient apiClient)
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

        // Step 2: Get instance info and display pre-shutdown message
        var info = await _apiClient.GetInfoAsync();
        AddInstanceInfoToResult(result, status, info, silent, jsonOutput);

        // Step 3: Send shutdown request
        var shutdownSent = await _apiClient.ShutdownAsync();
        if (!shutdownSent)
        {
            return BuildShutdownRequestFailedResult(result, silent, jsonOutput);
        }

        // Step 4: Wait for graceful shutdown
        var stopped = await _apiClient.WaitForShutdownAsync(ApiConstants.Timeouts.ShutdownWaitSeconds);
        if (stopped)
        {
            return BuildGracefulShutdownResult(result, silent, jsonOutput);
        }

        // Step 5: Graceful shutdown failed - attempt force kill
        return await AttemptForceKillAsync(result, status, silent, jsonOutput);
    }

    private CommandResult BuildNoInstanceRunningResult(Dictionary<string, object> result, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "No instance running";
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "No instance running");
        return CommandResult.UserError("No instance running");
    }

    private CommandResult BuildShutdownRequestFailedResult(Dictionary<string, object> result, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Failed to send shutdown request";
        result["message"] = "Service may have already stopped";
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Failed to send shutdown request");
        return CommandResult.SystemError("Failed to send shutdown request");
    }

    private CommandResult BuildGracefulShutdownResult(Dictionary<string, object> result, bool silent, bool jsonOutput)
    {
        result["success"] = true;
        result["method"] = "graceful";
        result["message"] = "Service stopped successfully";
        OutputResult(result, jsonOutput, silent, isError: false);
        return CommandResult.Success("Service stopped");
    }

    private void AddInstanceInfoToResult(Dictionary<string, object> result, InstanceStatus status, InfoResponse? info, bool silent, bool jsonOutput)
    {
        if (info is null)
        {
            // Fallback to basic info
            if (status.Version is not null)
            {
                result["version"] = status.Version.Version;
                result["application"] = status.Version.Application;

                if (!silent && !jsonOutput)
                {
                    Console.WriteLine("Stopping OpenTelWatcher service...");
                    Console.WriteLine($"  Application: {status.Version.Application}");
                    Console.WriteLine($"  Version:     {status.Version.Version}");
                }
            }
        }
        else
        {
            // Add full instance info to result
            result["version"] = info.Version;
            result["port"] = info.Port;
            result["pid"] = info.ProcessId;

            // Display using common formatter (skip for JSON output)
            if (!jsonOutput)
            {
                var config = new OpenTelWatcher.Utilities.ApplicationInfoConfig
                {
                    Version = info.Version,
                    Port = info.Port,
                    OutputDirectory = info.Configuration.OutputDirectory,
                    ProcessId = info.ProcessId,
                    FileCount = info.Files.Count,
                    TotalFileSize = info.Files.TotalSizeBytes,
                    Silent = silent
                };

                OpenTelWatcher.Utilities.ApplicationInfoDisplay.Display(OpenTelWatcher.Utilities.DisplayMode.Stop, config);
            }
        }

        // Track incompatibility
        if (!status.IsCompatible)
        {
            result["compatible"] = false;
            result["incompatibilityReason"] = status.IncompatibilityReason!;

            if (!silent && !jsonOutput)
            {
                Console.WriteLine();
                Console.WriteLine("Warning: Incompatible version detected, but attempting shutdown anyway.");
                Console.WriteLine($"  {status.IncompatibilityReason}");
            }
        }
    }

    private async Task<CommandResult> AttemptForceKillAsync(Dictionary<string, object> result, InstanceStatus status, bool silent, bool jsonOutput)
    {
        if (!silent && !jsonOutput)
        {
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine($"Warning: Service did not stop gracefully within {ApiConstants.Timeouts.ShutdownWaitSeconds} seconds");
        }

        if (!status.Pid.HasValue)
        {
            return BuildShutdownTimeoutResult(result, silent, jsonOutput);
        }

        if (!silent && !jsonOutput)
        {
            Console.WriteLine($"Attempting to forcefully terminate process (PID: {status.Pid.Value})...");
        }

        try
        {
            var process = System.Diagnostics.Process.GetProcessById(status.Pid.Value);

            // Validate process before killing
            if (!ValidateProcess(process))
            {
                return BuildProcessValidationFailedResult(result, process.ProcessName, silent, jsonOutput);
            }

            // Kill the process
            return KillProcess(result, process, status.Pid.Value, silent, jsonOutput);
        }
        catch (ArgumentException)
        {
            return BuildProcessNotFoundResult(result, silent, jsonOutput);
        }
        catch (Exception ex)
        {
            return BuildProcessKillFailedResult(result, ex.Message, silent, jsonOutput);
        }
    }

    private bool ValidateProcess(System.Diagnostics.Process process)
    {
        var processName = process.ProcessName.ToLowerInvariant();
        return processName == "opentelwatcher" || processName == "dotnet";
    }

    private CommandResult KillProcess(Dictionary<string, object> result, System.Diagnostics.Process process, int pid, bool silent, bool jsonOutput)
    {
        if (!silent && !jsonOutput)
        {
            Console.WriteLine($"WARNING: Forcefully terminating process '{process.ProcessName}' (PID: {pid})");
            Console.WriteLine("This will terminate the entire process tree.");
        }

        process.Kill(entireProcessTree: true);

        if (process.WaitForExit(5000))
        {
            result["success"] = true;
            result["method"] = "forceful";
            result["processName"] = process.ProcessName;
            result["pid"] = pid;
            result["message"] = "Service forcefully stopped";
            OutputResult(result, jsonOutput, silent, isError: false);
            return CommandResult.Success("Service forcefully stopped");
        }
        else
        {
            return BuildProcessTerminationTimeoutResult(result, pid, silent, jsonOutput);
        }
    }

    private CommandResult BuildProcessValidationFailedResult(Dictionary<string, object> result, string foundProcessName, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Process validation failed";
        result["expected"] = new[] { "opentelwatcher", "dotnet" };
        result["found"] = foundProcessName;
        result["message"] = "The process may have already stopped and the PID was recycled";
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Process validation failed");
        return CommandResult.SystemError("Process validation failed");
    }

    private CommandResult BuildProcessTerminationTimeoutResult(Dictionary<string, object> result, int pid, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Process termination timeout";
        result["pid"] = pid;
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Process termination timeout");
        return CommandResult.SystemError("Process termination timeout");
    }

    private CommandResult BuildProcessNotFoundResult(Dictionary<string, object> result, bool silent, bool jsonOutput)
    {
        result["success"] = true;
        result["message"] = "Service stopped (process not found - may have already stopped)";
        OutputResult(result, jsonOutput, silent, isError: false, messageType: "process not found");
        return CommandResult.Success("Service stopped");
    }

    private CommandResult BuildProcessKillFailedResult(Dictionary<string, object> result, string exceptionMessage, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Process kill failed";
        result["details"] = exceptionMessage;
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Process kill failed");
        return CommandResult.SystemError("Process kill failed");
    }

    private CommandResult BuildShutdownTimeoutResult(Dictionary<string, object> result, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Shutdown timeout";
        result["message"] = "The service may still be shutting down";
        result["tip"] = "Check status again in a few moments";
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Shutdown timeout");
        return CommandResult.SystemError("Shutdown timeout");
    }

    private void OutputResult(Dictionary<string, object> result, bool jsonOutput, bool silent, bool isError, string? errorType = null, string? messageType = null)
    {
        if (jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        // Text output based on error type or success
        if (isError)
        {
            OutputErrorText(result, errorType!);
        }
        else
        {
            OutputSuccessText(result, silent, messageType);
        }
    }

    private void OutputErrorText(Dictionary<string, object> result, string errorType)
    {
        switch (errorType)
        {
            case "No instance running":
                Console.WriteLine("No instance running.");
                break;

            case "Failed to send shutdown request":
                Console.WriteLine();
                Console.WriteLine($"Error: {errorType}");
                Console.WriteLine((string)result["message"]);
                break;

            case "Process validation failed":
                Console.WriteLine($"Error: Process validation failed. Expected 'opentelwatcher' or 'dotnet', found '{result["found"]}'");
                Console.WriteLine("The process may have already stopped and the PID was recycled.");
                Console.WriteLine("Refusing to terminate potentially unrelated process.");
                break;

            case "Process termination timeout":
                Console.WriteLine("Error: Failed to terminate process.");
                break;

            case "Process kill failed":
                Console.WriteLine($"Error: Failed to kill process: {result["details"]}");
                break;

            case "Shutdown timeout":
                Console.WriteLine("The service may still be shutting down.");
                Console.WriteLine("Check status again in a few moments using 'opentelwatcher'.");
                break;
        }
    }

    private void OutputSuccessText(Dictionary<string, object> result, bool silent, string? messageType)
    {
        var method = result.ContainsKey("method") ? (string)result["method"] : null;

        if (method == "forceful")
        {
            if (!silent) Console.WriteLine("Process terminated successfully.");
        }
        else if (method == "graceful")
        {
            if (!silent)
            {
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine("Service stopped successfully.");
            }
        }
        else if (messageType == "process not found")
        {
            if (!silent) Console.WriteLine("Process not found. It may have already stopped.");
        }
    }
}

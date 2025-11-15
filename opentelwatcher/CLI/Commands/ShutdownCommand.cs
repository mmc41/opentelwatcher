using System.Reflection;
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

    public async Task<CommandResult> ExecuteAsync(bool silent = false)
    {
        // Get CLI version
        var cliVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

        // Check if instance is running
        InstanceStatus status = await _apiClient.GetInstanceStatusAsync(cliVersion);

        if (!status.IsRunning)
        {
            Console.WriteLine("No instance running.");
            return CommandResult.UserError("No instance running");
        }

        // Get full info from running instance
        var info = await _apiClient.GetInfoAsync();

        if (info is null)
        {
            // Fallback to basic display if we can't get info
            if (!silent)
            {
                Console.WriteLine("Stopping OpenTelWatcher service...");
                if (status.Version is not null)
                {
                    Console.WriteLine($"  Application: {status.Version.Application}");
                    Console.WriteLine($"  Version:     {status.Version.Version}");
                }
            }
        }
        else
        {
            // Display using common formatter
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

        if (!status.IsCompatible && !silent)
        {
            Console.WriteLine();
            Console.WriteLine("Warning: Incompatible version detected, but attempting shutdown anyway.");
            Console.WriteLine($"  {status.IncompatibilityReason}");
        }

        // Send shutdown request
        var shutdownSent = await _apiClient.ShutdownAsync();
        if (!shutdownSent)
        {
            Console.WriteLine();
            Console.WriteLine("Error: Failed to send shutdown request");
            Console.WriteLine("Service may have already stopped.");
            return CommandResult.SystemError("Failed to send shutdown request");
        }

        // Wait for service to stop (message already shown by Display method)
        var stopped = await _apiClient.WaitForShutdownAsync(ApiConstants.Timeouts.ShutdownWaitSeconds);

        if (!stopped)
        {
            if (!silent)
            {
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine($"Warning: Service did not stop gracefully within {ApiConstants.Timeouts.ShutdownWaitSeconds} seconds");
            }

            // Attempt to kill the process if we have the PID
            if (status.Pid.HasValue)
            {
                if (!silent)
                {
                    Console.WriteLine($"Attempting to forcefully terminate process (PID: {status.Pid.Value})...");
                }

                try
                {
                    var process = System.Diagnostics.Process.GetProcessById(status.Pid.Value);

                    // SECURITY: Validate this is actually an opentelwatcher process before killing
                    // to prevent terminating unrelated processes if PID was recycled
                    var processName = process.ProcessName.ToLowerInvariant();
                    if (processName != "opentelwatcher" && processName != "dotnet")
                    {
                        Console.WriteLine($"Error: Process validation failed. Expected 'opentelwatcher' or 'dotnet', found '{process.ProcessName}'");
                        Console.WriteLine("The process may have already stopped and the PID was recycled.");
                        Console.WriteLine("Refusing to terminate potentially unrelated process.");
                        return CommandResult.SystemError("Process validation failed");
                    }

                    // Log warning before force kill
                    if (!silent) Console.WriteLine($"WARNING: Forcefully terminating process '{process.ProcessName}' (PID: {status.Pid.Value})");
                    if (!silent) Console.WriteLine("This will terminate the entire process tree.");

                    process.Kill(entireProcessTree: true);

                    // Wait a moment for the process to terminate
                    if (process.WaitForExit(5000))
                    {
                        if (!silent) Console.WriteLine("Process terminated successfully.");
                        return CommandResult.Success("Service forcefully stopped");
                    }
                    else
                    {
                        Console.WriteLine("Error: Failed to terminate process.");
                        return CommandResult.SystemError("Process termination timeout");
                    }
                }
                catch (ArgumentException)
                {
                    // Process not found - it may have already exited
                    if (!silent) Console.WriteLine("Process not found. It may have already stopped.");
                    return CommandResult.Success("Service stopped");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: Failed to kill process: {ex.Message}");
                    return CommandResult.SystemError("Process kill failed");
                }
            }
            else
            {
                Console.WriteLine("The service may still be shutting down.");
                Console.WriteLine("Check status again in a few moments using 'opentelwatcher'.");
                return CommandResult.SystemError("Shutdown timeout");
            }
        }

        if (!silent)
        {
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("Service stopped successfully.");
        }

        return CommandResult.Success("Service stopped");
    }
}

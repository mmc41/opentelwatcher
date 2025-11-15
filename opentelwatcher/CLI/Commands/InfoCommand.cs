using System.Reflection;
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

    public async Task<CommandResult> ExecuteAsync(bool verbose = false, bool silent = false)
    {
        // Get CLI version
        var cliVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

        // Check if instance is running
        var status = await _apiClient.GetInstanceStatusAsync(cliVersion);

        if (!status.IsRunning)
        {
            Console.WriteLine("No instance running.");
            Console.WriteLine("Start the service with 'opentelwatcher start' first.");
            return CommandResult.UserError("No instance running");
        }

        if (!status.IsCompatible)
        {
            Console.WriteLine("Warning: Incompatible version detected");
            Console.WriteLine($"  {status.IncompatibilityReason}");
            Console.WriteLine();
            Console.WriteLine("Information may be unreliable.");
            Console.WriteLine();
        }

        // Get info
        var info = await _apiClient.GetInfoAsync();

        if (info is null)
        {
            Console.WriteLine("Error: Failed to retrieve application information");
            return CommandResult.SystemError("Failed to retrieve info");
        }

        // Display information using common formatter
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

        return CommandResult.Success("Information displayed");
    }
}

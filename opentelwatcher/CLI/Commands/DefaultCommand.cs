using System.Reflection;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.CLI.Services;

namespace OpenTelWatcher.CLI.Commands;

/// <summary>
/// Default command (no arguments) - displays status and available commands
/// </summary>
public sealed class DefaultCommand
{
    private readonly IOpenTelWatcherApiClient _apiClient;

    public DefaultCommand(IOpenTelWatcherApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<CommandResult> ExecuteAsync()
    {
        // Get CLI version
        var cliVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

        // Display banner
        Console.WriteLine("OpenTelWatcher CLI");
        Console.WriteLine($"Version: {cliVersion.Major}.{cliVersion.Minor}.{cliVersion.Build}");
        Console.WriteLine();

        // Check instance status
        var status = await _apiClient.GetInstanceStatusAsync(cliVersion);

        if (!status.IsRunning)
        {
            Console.WriteLine("Status: No instance running");
            Console.WriteLine();
        }
        else if (!status.IsCompatible)
        {
            Console.WriteLine("Status: Incompatible instance detected");
            Console.WriteLine($"Error: {status.IncompatibilityReason}");
            Console.WriteLine();
            DisplayAvailableCommands();
            return CommandResult.SystemError("Incompatible instance detected");
        }
        else
        {
            Console.WriteLine("Status: Instance already running");
            Console.WriteLine($"  Application: {status.Version!.Application}");
            Console.WriteLine($"  Version:     {status.Version.Version}");
            Console.WriteLine();
        }

        DisplayAvailableCommands();

        return CommandResult.Success("Status displayed");
    }

    private static void DisplayAvailableCommands()
    {
        Console.WriteLine("Run 'opentelwatcher --help' to see all available commands and options.");
    }
}

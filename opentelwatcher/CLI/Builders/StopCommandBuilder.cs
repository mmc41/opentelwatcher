using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using OpenTelWatcher.CLI.Commands;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.CLI.Builders;

/// <summary>
/// Builds the 'opentelwatcher stop' command for graceful shutdown of running instances via HTTP API.
/// Constructs options for optional port specification (auto-resolved from PID file if omitted), --silent,
/// and --json output. Handler resolves port, instantiates StopCommand via DI, and invokes shutdown logic.
/// Supports both graceful shutdown (POST /api/stop) and forceful termination (process kill) fallback.
/// StopCommand handles API communication, process validation, and cleanup.
/// </summary>
/// <remarks>
/// Creates: System.CommandLine.Command → Executes: StopCommand.ExecuteAsync() → Result: Instance stopped
/// </remarks>
public sealed class StopCommandBuilder : CommandBuilderBase
{
    public StopCommandBuilder(IEnvironment environment) : base(environment)
    {
    }

    public override Command Build()
    {
        var portOption = new Option<int?>("--port")
        {
            Description = "Port number (auto-detected if single instance running)",
            DefaultValueFactory = _ => null
        };

        var silentOption = new Option<bool>("--silent")
        {
            Description = "Suppress all console output except errors",
            DefaultValueFactory = _ => false
        };

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Output results in JSON format",
            DefaultValueFactory = _ => false
        };

        var stopCommand = new Command("stop", "Stop the running OpenTelWatcher instance\n\n" +
            "Sends shutdown request to the running instance via HTTP API.\n" +
            "Port is auto-detected from PID file when a single instance is running.\n" +
            "Alias: shutdown\n\n" +
            "Options:\n" +
            "  --port <number>          Port number (auto-detected if omitted)\n" +
            "  --silent                 Suppress all output except errors\n" +
            "  --json                   Output results in JSON format")
        {
            portOption,
            silentOption,
            jsonOption
        };
        stopCommand.Aliases.Add("shutdown");

        stopCommand.SetAction(parseResult =>
        {
            var port = parseResult.GetValue(portOption);
            var silent = parseResult.GetValue(silentOption);
            var json = parseResult.GetValue(jsonOption);

            // Resolve port (auto-detect from PID file if not specified)
            var (resolvedPort, shouldContinue) = ResolvePortForCommand(port, silent, json);
            if (!shouldContinue)
                return 1; // Error already reported to user

            var services = BuildServiceProvider(resolvedPort);
            var command = services.GetRequiredService<StopCommand>();

            var options = new CommandOptions { Port = port, Silent = silent };
            var result = command.ExecuteAsync(options, json).GetAwaiter().GetResult();

            // Command handles its own console output
            return result.ExitCode;
        });

        return stopCommand;
    }
}

using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using OpenTelWatcher.CLI.Commands;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.CLI.Builders;

/// <summary>
/// Builds the 'opentelwatcher clear' command for deleting telemetry files in dual-mode operation.
/// Constructs options for port (auto-resolved), --output-dir validation, --verbose stats display,
/// and --silent output suppression. Instance mode queries running server and calls POST /api/clear;
/// standalone mode directly deletes files via TelemetryCleaner utility. Validates directory matches
/// instance configuration to prevent accidental data loss. ClearCommand handles mode detection and cleanup.
/// </summary>
/// <remarks>
/// Creates: System.CommandLine.Command → Executes: ClearCommand.ExecuteAsync() → Result: Files deleted
/// </remarks>
public sealed class ClearCommandBuilder : CommandBuilderBase
{
    public ClearCommandBuilder(IEnvironment environment) : base(environment)
    {
    }

    public override Command Build()
    {
        // Get default output directory from configuration (for standalone mode)
        var defaultOutputDir = GetDefaultOutputDirectory();

        var portOption = new Option<int?>("--port")
        {
            Description = "Port number (auto-detected if single instance running)",
            DefaultValueFactory = _ => null
        };

        var outputDirOption = new Option<string?>("--output-dir")
        {
            Description = "Directory to clear telemetry files from (only used when no instance is running)",
            DefaultValueFactory = _ => null
        };
        outputDirOption.Aliases.Add("-o");

        var silentOption = new Option<bool>("--silent")
        {
            Description = "Suppress all console output except errors",
            DefaultValueFactory = _ => false
        };

        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Enable verbose output with detailed operation information",
            DefaultValueFactory = _ => false
        };

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Output results in JSON format",
            DefaultValueFactory = _ => false
        };

        var clearCommand = new Command("clear", "Clear telemetry data files\n\n" +
            "If an instance is running: Clears files via API using the instance's output directory\n" +
            "  - If --output-dir is provided, it must match the instance's directory (validation)\n" +
            "  - If --output-dir is omitted, uses the instance's configured directory\n" +
            "If no instance running: Clears files directly from specified directory\n\n" +
            "Options:\n" +
            "  --port <number>          Port number (auto-detected if single instance running)\n" +
            "  --output-dir, -o <path>  Directory to clear (validated against instance when running)\n" +
            "  --silent                 Suppress all output except errors (overrides --verbose)\n" +
            "  --verbose                Show detailed operation information\n" +
            "  --json                   Output results in JSON format")
        {
            portOption,
            outputDirOption,
            silentOption,
            verboseOption,
            jsonOption
        };

        clearCommand.SetAction(parseResult =>
        {
            var port = parseResult.GetValue(portOption);
            var outputDir = parseResult.GetValue(outputDirOption);
            var silent = parseResult.GetValue(silentOption);
            var verbose = parseResult.GetValue(verboseOption);
            var json = parseResult.GetValue(jsonOption);

            // Resolve port (auto-detect from PID file if not specified)
            // Allow fallback for clear command (standalone mode)
            var (resolvedPort, _) = ResolvePortForCommand(port, silent, json, allowFallback: true);

            var services = BuildServiceProvider(resolvedPort);
            var command = services.GetRequiredService<ClearCommand>();
            var result = command.ExecuteAsync(outputDir, defaultOutputDir, verbose, silent, json).GetAwaiter().GetResult();

            // Command handles its own console output
            return result.ExitCode;
        });

        return clearCommand;
    }
}

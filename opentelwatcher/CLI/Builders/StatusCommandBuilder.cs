using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using OpenTelWatcher.CLI.Commands;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.Services.Interfaces;
using static OpenTelWatcher.Configuration.DefaultPorts;

namespace OpenTelWatcher.CLI.Builders;

/// <summary>
/// Builds the 'opentelwatcher status' command with dual-mode operation: API mode and filesystem mode.
/// Constructs options for display modes (--errors-only, --stats-only, --verbose, --quiet), port
/// auto-resolution, and optional --output-dir for standalone filesystem scanning. API mode queries
/// running instance via GET /api/status; filesystem mode scans directory for error files without
/// requiring running instance. StatusCommand handles mode selection, data retrieval, and formatting.
/// </summary>
/// <remarks>
/// Creates: System.CommandLine.Command → Executes: StatusCommand.ExecuteAsync() → Result: Health/error info
/// </remarks>
public sealed class StatusCommandBuilder : CommandBuilderBase
{
    public StatusCommandBuilder(IEnvironment environment) : base(environment)
    {
    }

    public override Command Build()
    {
        var portOption = new Option<int?>("--port")
        {
            Description = "Port number (auto-detected if single instance running)",
            DefaultValueFactory = _ => null
        };

        var errorsOnlyOption = new Option<bool>("--errors-only")
        {
            Description = "Show only error-related information",
            DefaultValueFactory = _ => false
        };

        var statsOnlyOption = new Option<bool>("--stats-only")
        {
            Description = "Show only telemetry and file statistics",
            DefaultValueFactory = _ => false
        };

        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Show detailed diagnostic information",
            DefaultValueFactory = _ => false
        };

        var quietOption = new Option<bool>("--quiet")
        {
            Description = "Suppress all output, only exit code",
            DefaultValueFactory = _ => false
        };

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Output results in JSON format",
            DefaultValueFactory = _ => false
        };

        var outputDirOption = new Option<string?>("--output-dir")
        {
            Description = "Force filesystem mode: scan directory for errors (no running instance required)",
            DefaultValueFactory = _ => null
        };

        var statusCommand = new Command("status", "Unified status command with multiple modes\n\n" +
            "Supports dual-mode operation:\n" +
            "- API mode: Query running instance for full diagnostics\n" +
            "- Filesystem mode: Scan directory for error files (standalone, no instance required)\n\n" +
            "Modes:\n" +
            "  Default:        Full diagnostic information (version, health, config, files, stats)\n" +
            "  --errors-only:  Show only error-related information\n" +
            "  --stats-only:   Show only telemetry and file statistics\n" +
            "  --verbose:      Show detailed diagnostic information\n" +
            "  --quiet:        Suppress output, only exit code\n" +
            "  --output-dir:   Force filesystem mode (scan directory for errors)\n\n" +
            "Exit codes:\n" +
            "  0: Healthy (no errors detected)\n" +
            "  1: Unhealthy (errors detected)\n" +
            "  2: System error (failed to retrieve information)\n\n" +
            "Options:\n" +
            "  --port <number>          Port number to query (default: 4318)\n" +
            "  --errors-only            Show only error-related information\n" +
            "  --stats-only             Show only telemetry and file statistics\n" +
            "  --verbose                Show detailed diagnostic information\n" +
            "  --quiet                  Suppress all output except errors\n" +
            "  --json                   Output results in JSON format\n" +
            "  --output-dir <path>      Scan directory for errors (filesystem mode)")
        {
            portOption,
            errorsOnlyOption,
            statsOnlyOption,
            verboseOption,
            quietOption,
            jsonOption,
            outputDirOption
        };

        statusCommand.SetAction(parseResult =>
        {
            var port = parseResult.GetValue(portOption);
            var errorsOnly = parseResult.GetValue(errorsOnlyOption);
            var statsOnly = parseResult.GetValue(statsOnlyOption);
            var verbose = parseResult.GetValue(verboseOption);
            var quiet = parseResult.GetValue(quietOption);
            var json = parseResult.GetValue(jsonOption);
            var outputDir = parseResult.GetValue(outputDirOption);

            // Resolve port (auto-detect from PID file if not specified), but only if not in standalone mode
            int resolvedPort;
            if (outputDir == null)
            {
                var (resolvedPortValue, shouldContinue) = ResolvePortForCommand(port, quiet, json);
                if (!shouldContinue)
                    return 1; // Error already reported to user
                resolvedPort = resolvedPortValue;
            }
            else
            {
                // Standalone mode - use default port (won't be used)
                resolvedPort = Otlp;
            }

            var services = BuildServiceProvider(resolvedPort);
            var command = services.GetRequiredService<StatusCommand>();

            var options = new StatusOptions
            {
                Port = port,
                ErrorsOnly = errorsOnly,
                StatsOnly = statsOnly,
                Verbose = verbose,
                Quiet = quiet,
                OutputDir = outputDir
            };
            var result = command.ExecuteAsync(options, json).GetAwaiter().GetResult();

            // Command handles its own console output
            return result.ExitCode;
        });

        return statusCommand;
    }
}

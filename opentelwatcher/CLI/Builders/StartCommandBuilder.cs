using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using OpenTelWatcher.CLI.Commands;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.Services.Interfaces;
using CliLogLevel = OpenTelWatcher.CLI.Models.LogLevel;
using static OpenTelWatcher.Configuration.DefaultPorts;

namespace OpenTelWatcher.CLI.Builders;

/// <summary>
/// Builds the 'opentelwatcher start' command with options for port, output directory, daemon mode, and tails.
/// Constructs System.CommandLine options, validators (port range, directory existence, mutually exclusive flags),
/// and sets up command handler that instantiates and invokes StartCommand via dependency injection.
/// Supports both foreground (blocking) and background (--daemon) execution modes with optional live
/// telemetry output (--tails). StartCommand performs actual server startup and health checking.
/// </summary>
/// <remarks>
/// Creates: System.CommandLine.Command → Executes: StartCommand.ExecuteAsync() → Result: Server started
/// </remarks>
public sealed class StartCommandBuilder : CommandBuilderBase
{
    public StartCommandBuilder(IEnvironment environment) : base(environment)
    {
    }

    public override Command Build()
    {
        // Get default output directory from configuration
        var defaultOutputDir = GetDefaultOutputDirectory();

        // Define options with validation
        var portOption = new Option<int>("--port")
        {
            Description = "Port number for the OTLP receiver",
            DefaultValueFactory = _ => Otlp
        };

        portOption.Validators.Add(result =>
        {
            var port = result.GetValue(portOption);
            if (port < 1 || port > 65535)
            {
                result.AddError("Port must be between 1 and 65535");
            }
        });

        var outputDirOption = new Option<string>("--output-dir")
        {
            Description = "Directory where telemetry data will be stored",
            DefaultValueFactory = _ => defaultOutputDir
        };
        outputDirOption.Aliases.Add("-o");

        outputDirOption.Validators.Add(result =>
        {
            var dir = result.GetValue(outputDirOption);
            if (string.IsNullOrWhiteSpace(dir))
            {
                result.AddError("Output directory cannot be empty");
                return;
            }

            var fullPath = Path.GetFullPath(dir);
            var parentDir = Path.GetDirectoryName(fullPath);
            if (parentDir != null && !string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
            {
                result.AddError($"Parent directory does not exist: {parentDir}");
            }
        });

        var logLevelOption = new Option<string>("--log-level")
        {
            Description = "Minimum log level (Trace, Debug, Information, Warning, Error, Critical)",
            DefaultValueFactory = _ => "Information"
        };

        var daemonOption = new Option<bool>("--daemon")
        {
            Description = "Run in background (non-blocking mode)",
            DefaultValueFactory = _ => false
        };

        var silentOption = new Option<bool>("--silent")
        {
            Description = "Suppress all console output except errors (higher priority than --verbose)",
            DefaultValueFactory = _ => false
        };

        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Enable verbose output with additional diagnostic information",
            DefaultValueFactory = _ => false
        };

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Output results in JSON format",
            DefaultValueFactory = _ => false
        };
        var tailsOption = new Option<bool>("--tails")
        {
            Description = "Output live telemetry to stdout in addition to files",
            DefaultValueFactory = _ => false
        };

        var tailsFilterErrorsOnlyOption = new Option<bool>("--tails-filter-errors-only")
        {
            Description = "Only output errors when using --tails (requires --tails)",
            DefaultValueFactory = _ => false
        };


        var startCommand = new Command("start", "Start the OpenTelWatcher service\n\n" +
            "Options:\n" +
            "  --port <number>          Port number (default: 4318)\n" +
            "  --output-dir, -o <path>  Output directory (default from appsettings.json)\n" +
            "  --log-level <level>      Log level: Trace, Debug, Information, Warning, Error, Critical (default: Information)\n" +
            "  --daemon                 Run in background (non-blocking mode)\n" +
            "  --tails                  Output live telemetry to stdout (all signals: traces, logs, metrics)\n" +
            "  --tails-filter-errors-only  Only output errors when using --tails (requires --tails)\n" +
            "  --silent                 Suppress all output except errors (overrides --verbose)\n" +
            "  --verbose                Enable verbose console output\n" +
            "  --json                   Output results in JSON format")
        {
            portOption,
            outputDirOption,
            logLevelOption,
            daemonOption,
            silentOption,
            verboseOption,
            tailsOption,
            tailsFilterErrorsOnlyOption,
            jsonOption
        };
        // Add validators for tails options
        startCommand.Validators.Add(result =>
        {
            var daemon = result.GetValue(daemonOption);
            var tails = result.GetValue(tailsOption);
            var tailsFilterErrorsOnly = result.GetValue(tailsFilterErrorsOnlyOption);

            if (tails && daemon)
            {
                result.AddError("Cannot use --tails with --daemon. Tails mode requires foreground operation.");
            }

            if (tailsFilterErrorsOnly && !tails)
            {
                result.AddError("Cannot use --tails-filter-errors-only without --tails.");
            }
        });

        // Use SetAction with parameter binding
        startCommand.SetAction(parseResult =>
        {
            // Extract option values
            var port = parseResult.GetValue(portOption);
            var outputDir = parseResult.GetValue(outputDirOption);
            var logLevelString = parseResult.GetValue(logLevelOption);
            var daemon = parseResult.GetValue(daemonOption);
            var silent = parseResult.GetValue(silentOption);
            var verbose = parseResult.GetValue(verboseOption);
            var tails = parseResult.GetValue(tailsOption);
            var tailsFilterErrorsOnly = parseResult.GetValue(tailsFilterErrorsOnlyOption);
            var json = parseResult.GetValue(jsonOption);

            // Parse log level
            if (!Enum.TryParse<CliLogLevel>(logLevelString, true, out var logLevel))
            {
                Console.Error.WriteLine($"Invalid log level: {logLevelString}");
                return 1;
            }

            var options = new CommandOptions
            {
                Port = port,
                OutputDirectory = outputDir ?? defaultOutputDir,
                LogLevel = logLevel,
                Daemon = daemon,
                Silent = silent,
                Verbose = verbose,
                Tails = tails,
                TailsFilterErrorsOnly = tailsFilterErrorsOnly
            };

            var services = BuildServiceProvider(port);
            var command = services.GetRequiredService<StartCommand>();
            var result = command.ExecuteAsync(options, json).GetAwaiter().GetResult();

            // Command handles its own console output
            return result.ExitCode;
        });

        return startCommand;
    }
}

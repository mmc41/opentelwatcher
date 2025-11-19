using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.CLI.Commands;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.CLI.Services;
using OpenTelWatcher.Hosting;
using OpenTelWatcher.Services;
using OpenTelWatcher.Services.Interfaces;
using CliLogLevel = OpenTelWatcher.CLI.Models.LogLevel;
using static OpenTelWatcher.Configuration.DefaultPorts;

namespace OpenTelWatcher.CLI;

/// <summary>
/// System.CommandLine-based CLI application orchestrator.
/// Follows System.CommandLine best practices: all argument parsing and routing
/// is handled by the framework, no manual argument inspection.
/// </summary>
public sealed class CliApplication
{
    private readonly IEnvironment _environment;

    public CliApplication(IEnvironment environment)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public Task<int> RunAsync(string[] args)
    {
        var rootCommand = BuildRootCommand();

        // Let System.CommandLine handle everything - no manual argument inspection
        var parseResult = rootCommand.Parse(args);
        return Task.FromResult(parseResult.Invoke());
    }

    private RootCommand BuildRootCommand()
    {
        var rootCommand = new RootCommand("OpenTelWatcher - OTLP/HTTP receiver for development and testing\n\n" +
            "Examples:\n" +
            "  opentelwatcher start                              Start with defaults from appsettings.json\n" +
            "  opentelwatcher start --port 5000 -o ./data        Start on custom port with custom output directory\n" +
            "  opentelwatcher start --daemon                     Start in background (non-blocking)\n" +
            "  opentelwatcher stop                               Stop the running instance\n" +
            "  opentelwatcher status                             Quick health status summary\n" +
            "  opentelwatcher status --verbose                   Detailed telemetry and file statistics\n" +
            "  opentelwatcher info                               View application information\n" +
            "  opentelwatcher list                               List telemetry data files\n" +
            "  opentelwatcher check                              Check for error files\n" +
            "  opentelwatcher clear                              Clear telemetry data files\n\n" +
            "File Naming Patterns:\n" +
            "  Normal files: {signal}.{timestamp}.ndjson\n" +
            "  Error files:  {signal}.{timestamp}.errors.ndjson\n\n" +
            "  Where {signal} = traces, logs, or metrics\n" +
            "        {timestamp} = YYYYMMDD_HHMMSS_mmm (UTC)\n\n" +
            "For detailed options: opentelwatcher start --help");

        // Add subcommands
        rootCommand.Subcommands.Add(BuildStartCommand());
        rootCommand.Subcommands.Add(BuildStopCommand());
        rootCommand.Subcommands.Add(BuildStatusCommand());
        rootCommand.Subcommands.Add(BuildListCommand());
        rootCommand.Subcommands.Add(BuildClearCommand());

        return rootCommand;
    }

    /// <summary>
    /// Gets the default output directory from configuration.
    /// Reads from appsettings.json/appsettings.Development.json using standard .NET configuration system.
    /// </summary>
    private string GetDefaultOutputDirectory()
    {
        var environmentName = _environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                           ?? _environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                           ?? "Production";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(_environment.CurrentDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
            .Build();

        return configuration["OpenTelWatcher:OutputDirectory"] ?? "./telemetry-data";
    }

    private Command BuildStartCommand()
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

    private Command BuildStopCommand()
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

    private Command BuildStatusCommand()
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

    private Command BuildClearCommand()
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
            tailsOption,
            tailsFilterErrorsOnlyOption,
            jsonOption
        };

        clearCommand.SetAction(parseResult =>
        {
            var port = parseResult.GetValue(portOption);
            var outputDir = parseResult.GetValue(outputDirOption);
            var silent = parseResult.GetValue(silentOption);
            var verbose = parseResult.GetValue(verboseOption);
            var tails = parseResult.GetValue(tailsOption);
            var tailsFilterErrorsOnly = parseResult.GetValue(tailsFilterErrorsOnlyOption);
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

    private Command BuildListCommand()
    {
        // Get default output directory from configuration
        var defaultOutputDir = GetDefaultOutputDirectory();

        var outputDirOption = new Option<string>("--output-dir")
        {
            Description = "Directory to list telemetry files from",
            DefaultValueFactory = _ => defaultOutputDir
        };
        outputDirOption.Aliases.Add("-o");

        var errorsOnlyOption = new Option<bool>("--errors-only")
        {
            Description = "Show only error files (*.errors.ndjson)",
            DefaultValueFactory = _ => false
        };

        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Show detailed file information (size, date)",
            DefaultValueFactory = _ => false
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


        var listCommand = new Command("list", "List telemetry data files\n\n" +
            "Scans the output directory for .ndjson telemetry files.\n" +
            "Works in standalone mode (no running instance required).\n\n" +
            "Options:\n" +
            "  --output-dir, -o <path>  Directory to list files from (default from appsettings.json)\n" +
            "  --errors-only            Show only error files (*.errors.ndjson)\n" +
            "  --verbose                Show detailed file information (size, date)\n" +
            "  --silent                 Suppress all output except errors\n" +
            "  --json                   Output results in JSON format")
        {
            outputDirOption,
            errorsOnlyOption,
            verboseOption,
            silentOption,
            jsonOption
        };

        listCommand.SetAction(parseResult =>
        {
            var outputDir = parseResult.GetValue(outputDirOption);
            var errorsOnly = parseResult.GetValue(errorsOnlyOption);
            var verbose = parseResult.GetValue(verboseOption);
            var silent = parseResult.GetValue(silentOption);
            var json = parseResult.GetValue(jsonOption);

            var command = new ListCommand();
            var result = command.ExecuteAsync(outputDir, defaultOutputDir, errorsOnly, verbose, silent, json).GetAwaiter().GetResult();

            // Command handles its own console output
            return result.ExitCode;
        });

        return listCommand;
    }

    /// <summary>
    /// Resolves the port to use for a command. If explicit port is provided, returns it.
    /// Otherwise, attempts to auto-resolve from PID file.
    /// </summary>
    /// <param name="explicitPort">Explicitly provided port, or null for auto-resolution</param>
    /// <param name="silent">Whether to suppress console output on error</param>
    /// <param name="jsonOutput">Whether to output errors in JSON format</param>
    /// <param name="allowFallback">Whether to fallback to default port on resolution failure (for standalone modes)</param>
    /// <returns>Tuple of (resolvedPort, shouldContinue). shouldContinue=false means an error was already reported.</returns>
    private (int port, bool shouldContinue) ResolvePortForCommand(
        int? explicitPort,
        bool silent = false,
        bool jsonOutput = false,
        bool allowFallback = false)
    {
        // If explicit port provided, use it
        if (explicitPort.HasValue)
            return (explicitPort.Value, true);

        // Attempt auto-resolution from PID file
        var tempServices = BuildServiceProvider(Otlp);
        var portResolver = tempServices.GetRequiredService<IPortResolver>();

        try
        {
            var resolvedPort = portResolver.ResolvePort(null);
            return (resolvedPort, true);
        }
        catch (InvalidOperationException ex)
        {
            // Auto-resolution failed
            if (allowFallback)
            {
                // Fallback to default port for standalone modes
                return (Otlp, true);
            }

            // Report error to user
            if (!silent && !jsonOutput)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            else if (jsonOutput)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    error = ex.Message
                }));
            }
            return (Otlp, false); // Return default port but signal not to continue
        }
    }

    private IServiceProvider BuildServiceProvider(int port)
    {
        var services = new ServiceCollection();

        // HttpClient for API communication
        services.AddHttpClient<IOpenTelWatcherApiClient, OpenTelWatcherApiClient>(client =>
        {
            client.BaseAddress = new Uri($"http://{ApiConstants.Network.LocalhostIp}:{port}");
            // Short timeout for CLI commands - if service doesn't respond quickly, it's likely not running
            client.Timeout = TimeSpan.FromSeconds(ApiConstants.Timeouts.ApiRequestSeconds);
        });

        // System abstraction services (for testability)
        services.AddSingleton<IEnvironment, EnvironmentAdapter>();
        services.AddSingleton<IProcessProvider, ProcessProvider>();
        services.AddSingleton<ITimeProvider, SystemTimeProvider>();

        // Web application host (production implementation)
        services.AddSingleton<IWebApplicationHost, WebApplicationHost>();

        // PID file service
        services.AddSingleton<IPidFileService, PidFileService>();

        // Port resolution service
        services.AddSingleton<IPortResolver, PortResolver>();

        // Logging
        services.AddLogging();

        // Command handlers
        services.AddTransient<StartCommand>();
        services.AddTransient<StopCommand>();
        services.AddTransient<StatusCommand>();
        services.AddTransient<ClearCommand>();

        return services.BuildServiceProvider();
    }
}

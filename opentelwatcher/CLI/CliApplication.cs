using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.CLI.Commands;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.CLI.Services;
using OpenTelWatcher.Hosting;
using CliLogLevel = OpenTelWatcher.CLI.Models.LogLevel;

namespace OpenTelWatcher.CLI;

/// <summary>
/// System.CommandLine-based CLI application orchestrator.
/// Follows System.CommandLine best practices: all argument parsing and routing
/// is handled by the framework, no manual argument inspection.
/// </summary>
public sealed class CliApplication
{
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
            "  opentelwatcher stats                              Show telemetry and file statistics\n" +
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
        rootCommand.Subcommands.Add(BuildStatsCommand());
        rootCommand.Subcommands.Add(BuildInfoCommand());
        rootCommand.Subcommands.Add(BuildListCommand());
        rootCommand.Subcommands.Add(BuildCheckCommand());
        rootCommand.Subcommands.Add(BuildClearCommand());

        return rootCommand;
    }

    /// <summary>
    /// Gets the default output directory from configuration.
    /// Reads from appsettings.json/appsettings.Development.json using standard .NET configuration system.
    /// </summary>
    private string GetDefaultOutputDirectory()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
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
            DefaultValueFactory = _ => 4318
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
            jsonOption
        };

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
                Verbose = verbose
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
            "Alias: shutdown\n\n" +
            "Options:\n" +
            "  --silent                 Suppress all output except errors\n" +
            "  --json                   Output results in JSON format")
        {
            silentOption,
            jsonOption
        };
        stopCommand.Aliases.Add("shutdown");

        stopCommand.SetAction(parseResult =>
        {
            var silent = parseResult.GetValue(silentOption);
            var json = parseResult.GetValue(jsonOption);
            var services = BuildServiceProvider(4318);
            var command = services.GetRequiredService<ShutdownCommand>();
            var result = command.ExecuteAsync(silent, json).GetAwaiter().GetResult();

            // Command handles its own console output
            return result.ExitCode;
        });

        return stopCommand;
    }

    private Command BuildStatusCommand()
    {
        var portOption = new Option<int>("--port")
        {
            Description = "Port number to query (default: 4318)",
            DefaultValueFactory = _ => 4318
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

        var statusCommand = new Command("status", "Quick health status summary\n\n" +
            "Provides a one-line summary of the watcher's health status.\n" +
            "Returns exit code 0 for healthy, 1 for unhealthy (errors detected),\n" +
            "and 2 if no instance is running.\n\n" +
            "Options:\n" +
            "  --port <number>          Port number to query (default: 4318)\n" +
            "  --silent                 Suppress all output except errors\n" +
            "  --json                   Output results in JSON format")
        {
            portOption,
            silentOption,
            jsonOption
        };

        statusCommand.SetAction(parseResult =>
        {
            var port = parseResult.GetValue(portOption);
            var silent = parseResult.GetValue(silentOption);
            var json = parseResult.GetValue(jsonOption);
            var services = BuildServiceProvider(port);
            var command = services.GetRequiredService<StatusCommand>();
            var result = command.ExecuteAsync(silent, json).GetAwaiter().GetResult();

            // Command handles its own console output
            return result.ExitCode;
        });

        return statusCommand;
    }

    private Command BuildStatsCommand()
    {
        var portOption = new Option<int>("--port")
        {
            Description = "Port number to query (default: 4318)",
            DefaultValueFactory = _ => 4318
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

        var statsCommand = new Command("stats", "Show telemetry and file statistics\n\n" +
            "Displays telemetry request counts (traces, logs, metrics),\n" +
            "file breakdown by type, and uptime.\n\n" +
            "Options:\n" +
            "  --port <number>          Port number to query (default: 4318)\n" +
            "  --silent                 Suppress all output except errors\n" +
            "  --json                   Output results in JSON format")
        {
            portOption,
            silentOption,
            jsonOption
        };

        statsCommand.SetAction(parseResult =>
        {
            var port = parseResult.GetValue(portOption);
            var silent = parseResult.GetValue(silentOption);
            var json = parseResult.GetValue(jsonOption);
            var services = BuildServiceProvider(port);
            var command = services.GetRequiredService<StatsCommand>();
            var result = command.ExecuteAsync(silent, json).GetAwaiter().GetResult();

            // Command handles its own console output
            return result.ExitCode;
        });

        return statsCommand;
    }

    private Command BuildInfoCommand()
    {
        var silentOption = new Option<bool>("--silent")
        {
            Description = "Suppress all console output except errors",
            DefaultValueFactory = _ => false
        };

        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Enable verbose output with detailed error information",
            DefaultValueFactory = _ => false
        };

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Output results in JSON format",
            DefaultValueFactory = _ => false
        };

        var infoCommand = new Command("info", "View application information from the running instance\n\n" +
            "Displays version, health status, telemetry statistics, and configuration.\n" +
            "Requires a running instance to connect to.\n\n" +
            "Options:\n" +
            "  --silent                 Suppress all output except errors (overrides --verbose)\n" +
            "  --verbose                Show detailed error information\n" +
            "  --json                   Output results in JSON format")
        {
            silentOption,
            verboseOption,
            jsonOption
        };

        infoCommand.SetAction(parseResult =>
        {
            var silent = parseResult.GetValue(silentOption);
            var verbose = parseResult.GetValue(verboseOption);
            var json = parseResult.GetValue(jsonOption);
            var services = BuildServiceProvider(4318);
            var command = services.GetRequiredService<InfoCommand>();
            var result = command.ExecuteAsync(verbose, silent, json).GetAwaiter().GetResult();

            // Command handles its own console output
            return result.ExitCode;
        });

        return infoCommand;
    }

    private Command BuildCheckCommand()
    {
        // Get default output directory from configuration
        var defaultOutputDir = GetDefaultOutputDirectory();

        var outputDirOption = new Option<string>("--output-dir")
        {
            Description = "Directory to check for error files",
            DefaultValueFactory = _ => defaultOutputDir
        };
        outputDirOption.Aliases.Add("-o");

        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Show detailed error file list",
            DefaultValueFactory = _ => false
        };

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Output results in JSON format",
            DefaultValueFactory = _ => false
        };

        var checkCommand = new Command("check", "Check for error files in telemetry output directory\n\n" +
            "Scans the output directory for .errors.ndjson files.\n" +
            "Returns exit code 0 if no errors found, 1 if errors detected.\n" +
            "Works in standalone mode (no running instance required).\n\n" +
            "Options:\n" +
            "  --output-dir, -o <path>  Directory to check (default from appsettings.json)\n" +
            "  --verbose                Show detailed error file list\n" +
            "  --json                   Output results in JSON format")
        {
            outputDirOption,
            verboseOption,
            jsonOption
        };

        checkCommand.SetAction(parseResult =>
        {
            var outputDir = parseResult.GetValue(outputDirOption);
            var verbose = parseResult.GetValue(verboseOption);
            var json = parseResult.GetValue(jsonOption);

            var options = new CheckOptions
            {
                OutputDir = outputDir ?? defaultOutputDir,
                Verbose = verbose,
                JsonOutput = json
            };

            var command = new CheckCommand();
            var result = command.ExecuteAsync(options).GetAwaiter().GetResult();

            // Handle output based on JSON flag
            if (json)
            {
                // JSON output
                var jsonData = System.Text.Json.JsonSerializer.Serialize(result.Data, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                Console.WriteLine(jsonData);
            }
            else
            {
                // Human-readable output
                Console.WriteLine(result.Message);
                if (verbose && result.Data != null && result.Data.ContainsKey("errorFiles"))
                {
                    var errorFiles = result.Data["errorFiles"] as List<string>;
                    if (errorFiles != null && errorFiles.Count > 0)
                    {
                        Console.WriteLine("\nError files:");
                        foreach (var file in errorFiles)
                        {
                            Console.WriteLine($"  - {file}");
                        }
                    }
                }
            }

            return result.ExitCode;
        });

        return checkCommand;
    }

    private Command BuildClearCommand()
    {
        // Get default output directory from configuration (for standalone mode)
        var defaultOutputDir = GetDefaultOutputDirectory();

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
            "  --output-dir, -o <path>  Directory to clear (validated against instance when running)\n" +
            "  --silent                 Suppress all output except errors (overrides --verbose)\n" +
            "  --verbose                Show detailed operation information\n" +
            "  --json                   Output results in JSON format")
        {
            outputDirOption,
            silentOption,
            verboseOption,
            jsonOption
        };

        clearCommand.SetAction(parseResult =>
        {
            var outputDir = parseResult.GetValue(outputDirOption);
            var silent = parseResult.GetValue(silentOption);
            var verbose = parseResult.GetValue(verboseOption);
            var json = parseResult.GetValue(jsonOption);
            var services = BuildServiceProvider(4318);
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

        // Web application host (production implementation)
        services.AddSingleton<IWebApplicationHost, WebApplicationHost>();

        // Command handlers
        services.AddTransient<StartCommand>();
        services.AddTransient<ShutdownCommand>();
        services.AddTransient<StatusCommand>();
        services.AddTransient<StatsCommand>();
        services.AddTransient<InfoCommand>();
        services.AddTransient<ClearCommand>();

        return services.BuildServiceProvider();
    }
}

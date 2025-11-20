using System.CommandLine;
using OpenTelWatcher.CLI.Builders;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.CLI;

/// <summary>
/// System.CommandLine-based CLI application orchestrator.
/// Follows System.CommandLine best practices: all argument parsing and routing
/// is handled by the framework, no manual argument inspection.
/// Delegates command building to specialized builder classes for better modularity.
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
            "  opentelwatcher start --tails                      Start with live telemetry output to stdout\n" +
            "  opentelwatcher start --tails --tails-filter-errors-only  Start with live error output only\n" +
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

        // Create command builders and add their commands to root
        var startBuilder = new StartCommandBuilder(_environment);
        var stopBuilder = new StopCommandBuilder(_environment);
        var statusBuilder = new StatusCommandBuilder(_environment);
        var listBuilder = new ListCommandBuilder(_environment);
        var clearBuilder = new ClearCommandBuilder(_environment);

        rootCommand.Subcommands.Add(startBuilder.Build());
        rootCommand.Subcommands.Add(stopBuilder.Build());
        rootCommand.Subcommands.Add(statusBuilder.Build());
        rootCommand.Subcommands.Add(listBuilder.Build());
        rootCommand.Subcommands.Add(clearBuilder.Build());

        return rootCommand;
    }
}

using System.CommandLine;
using OpenTelWatcher.CLI.Commands;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.CLI.Builders;

/// <summary>
/// Builds the 'opentelwatcher list' command for displaying telemetry files in output directory.
/// Constructs options for --output-dir path, --errors-only filtering, --verbose metadata display,
/// and --json structured output. Operates in standalone mode (no running instance required), scanning
/// filesystem directly for *.ndjson files. Supports sorting by modification time and pattern matching
/// for error files (*.errors.ndjson). ListCommand handles directory scanning and formatted display.
/// </summary>
/// <remarks>
/// Creates: System.CommandLine.Command → Executes: ListCommand.ExecuteAsync() → Result: File list displayed
/// </remarks>
public sealed class ListCommandBuilder : CommandBuilderBase
{
    public ListCommandBuilder(IEnvironment environment) : base(environment)
    {
    }

    public override Command Build()
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
}

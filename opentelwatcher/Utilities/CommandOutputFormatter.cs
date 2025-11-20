using System.Text.Json;

namespace OpenTelWatcher.Utilities;

/// <summary>
/// Provides shared output formatting logic for CLI commands.
/// Handles JSON serialization and silent mode, delegating text formatting to command-specific logic.
/// </summary>
public static class CommandOutputFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Outputs command result in either JSON or text format.
    /// </summary>
    /// <param name="result">Result dictionary to output</param>
    /// <param name="jsonOutput">If true, output as JSON</param>
    /// <param name="silent">If true, suppress all output</param>
    /// <param name="textFormatter">Action to format text output (called if not JSON and not silent)</param>
    public static void Output(
        Dictionary<string, object> result,
        bool jsonOutput,
        bool silent,
        Action<Dictionary<string, object>> textFormatter)
    {
        if (jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        if (silent)
        {
            return;
        }

        textFormatter(result);
    }
}

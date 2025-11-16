using OpenTelWatcher.CLI.Models;

namespace OpenTelWatcher.CLI.Commands;

/// <summary>
/// Check command - verifies if error files exist in telemetry output directory.
/// Returns exit code 0 if no errors, 1 if errors detected.
/// Works in standalone mode (no running instance required).
/// </summary>
public sealed class CheckCommand
{
    /// <summary>
    /// Executes the check command to detect error files.
    /// </summary>
    /// <param name="options">Command options</param>
    /// <returns>CommandResult with exit code 0 (no errors) or 1 (errors detected)</returns>
    public async Task<CommandResult> ExecuteAsync(CheckOptions options)
    {
        // Handle null or empty output directory
        var outputDir = string.IsNullOrWhiteSpace(options.OutputDir)
            ? "./telemetry-data"
            : options.OutputDir;

        // Check if directory exists
        if (!Directory.Exists(outputDir))
        {
            // Non-existent directory means no error files
            var noErrorsData = CreateResponseData(false, 0, Array.Empty<string>());
            return await Task.FromResult(CommandResult.Success("No errors", data: noErrorsData));
        }

        // Find all error files
        var errorFiles = Directory.GetFiles(outputDir, "*.errors.ndjson")
            .Select(Path.GetFileName)
            .Where(name => name != null)
            .Cast<string>()
            .OrderBy(name => name)
            .ToList();

        var hasErrors = errorFiles.Count > 0;

        // Create response data
        var data = CreateResponseData(hasErrors, errorFiles.Count, errorFiles);

        if (!hasErrors)
        {
            return CommandResult.Success("No errors", data: data);
        }

        return CommandResult.UserError("Errors detected", data: data);
    }

    /// <summary>
    /// Creates structured response data for the check command.
    /// </summary>
    private static Dictionary<string, object> CreateResponseData(
        bool hasErrors,
        int errorFileCount,
        IEnumerable<string> errorFiles)
    {
        return new Dictionary<string, object>
        {
            ["hasErrors"] = hasErrors,
            ["errorFileCount"] = errorFileCount,
            ["errorFiles"] = errorFiles.ToList()
        };
    }
}

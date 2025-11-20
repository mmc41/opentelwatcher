using System.Text.Json;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.Utilities;

namespace OpenTelWatcher.CLI.Commands;

/// <summary>
/// Executes file listing business logic for the 'opentelwatcher list' command in standalone mode.
/// Scans output directory for *.ndjson telemetry files (traces, logs, metrics), filtering by pattern
/// (--errors-only for *.errors.ndjson) and sorting by modification time. Displays file names, sizes,
/// timestamps (--verbose), and counts. Operates without requiring running instance, making it useful
/// for offline inspection. ListCommandBuilder creates CLI structure; this class handles filesystem scanning and formatting.
/// </summary>
/// <remarks>
/// Scope: Directory scanning, file filtering/sorting, metadata extraction, formatted display (text/JSON).
/// Builder: ListCommandBuilder provides options → This class: Scans filesystem and formats output
/// </remarks>
public sealed class ListCommand
{
    public async Task<CommandResult> ExecuteAsync(string? outputDir = null, string defaultOutputDir = "./telemetry-data", bool errorsOnly = false, bool verbose = false, bool silent = false, bool jsonOutput = false)
    {
        var result = new Dictionary<string, object>();
        outputDir ??= defaultOutputDir;

        // Step 1: Check if directory exists
        if (!Directory.Exists(outputDir))
        {
            return BuildDirectoryNotFoundResult(result, outputDir, silent, jsonOutput);
        }

        // Step 2: Get file list
        try
        {
            var files = GetFileList(outputDir, errorsOnly);
            return BuildSuccessResult(result, outputDir, files, errorsOnly, verbose, silent, jsonOutput);
        }
        catch (Exception ex)
        {
            return BuildFileListFailedResult(result, outputDir, ex.Message, silent, jsonOutput);
        }
    }

    private List<FileInfo> GetFileList(string outputDir, bool errorsOnly)
    {
        var directoryInfo = new DirectoryInfo(outputDir);
        var pattern = errorsOnly ? "*.errors.ndjson" : "*.ndjson";

        var files = directoryInfo.GetFiles(pattern, SearchOption.TopDirectoryOnly)
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToList();

        return files;
    }

    private CommandResult BuildDirectoryNotFoundResult(Dictionary<string, object> result, string outputDir, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Directory not found";
        result["outputDirectory"] = outputDir;
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Directory not found");
        return CommandResult.UserError("Directory not found");
    }

    private CommandResult BuildFileListFailedResult(Dictionary<string, object> result, string outputDir, string details, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Failed to list files";
        result["outputDirectory"] = outputDir;
        result["details"] = details;
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Failed to list files");
        return CommandResult.SystemError("Failed to list files");
    }

    private CommandResult BuildSuccessResult(Dictionary<string, object> result, string outputDir, List<FileInfo> files, bool errorsOnly, bool verbose, bool silent, bool jsonOutput)
    {
        result["success"] = true;
        result["outputDirectory"] = outputDir;
        result["fileCount"] = files.Count;
        result["errorsOnly"] = errorsOnly;

        // Build file list for JSON
        var fileList = files.Select(f => new Dictionary<string, object>
        {
            ["name"] = f.Name,
            ["sizeBytes"] = f.Length,
            ["lastModified"] = f.LastWriteTimeUtc.ToString("o")
        }).ToList();

        result["files"] = fileList;

        OutputResult(result, jsonOutput, silent, isError: false, files: files, verbose: verbose, errorsOnly: errorsOnly);
        return CommandResult.Success("Files listed", data: new Dictionary<string, object>
        {
            ["result"] = result
        });
    }

    private void OutputResult(Dictionary<string, object> result, bool jsonOutput, bool silent, bool isError, string? errorType = null, List<FileInfo>? files = null, bool verbose = false, bool errorsOnly = false)
    {
        CommandOutputFormatter.Output(result, jsonOutput, silent, _ =>
        {
            if (isError)
            {
                OutputErrorText(result, errorType!);
            }
            else
            {
                OutputSuccessText(result, files!, verbose, errorsOnly);
            }
        });
    }

    private void OutputErrorText(Dictionary<string, object> result, string errorType)
    {
        switch (errorType)
        {
            case "Directory not found":
                Console.WriteLine($"Error: Directory not found: {result["outputDirectory"]}.");
                break;

            case "Failed to list files":
                Console.WriteLine($"Error: Failed to list files from {result["outputDirectory"]}.");
                Console.WriteLine($"Details: {result["details"]}");
                break;
        }
    }

    private void OutputSuccessText(Dictionary<string, object> result, List<FileInfo> files, bool verbose, bool errorsOnly)
    {
        var outputDir = (string)result["outputDirectory"];
        var fileCount = (int)result["fileCount"];

        // Header
        Console.WriteLine("╔═══════════════════════════════════════╗");
        if (errorsOnly)
        {
            Console.WriteLine("║     ERROR FILES                       ║");
        }
        else
        {
            Console.WriteLine("║     TELEMETRY FILES                   ║");
        }
        Console.WriteLine("╚═══════════════════════════════════════╝");
        Console.WriteLine();

        Console.WriteLine($"Directory: {outputDir}");
        Console.WriteLine($"Files found: {fileCount}");
        Console.WriteLine();

        if (fileCount == 0)
        {
            if (errorsOnly)
            {
                Console.WriteLine("No error files found.");
            }
            else
            {
                Console.WriteLine("No files found.");
            }
            return;
        }

        // Display file list
        if (verbose)
        {
            // Verbose: Show size and date
            Console.WriteLine($"{"File Name",-50} {"Size",-12} {"Last Modified"}");
            Console.WriteLine(new string('-', 85));

            foreach (var file in files)
            {
                var size = NumberFormatter.FormatBytes(file.Length);
                var date = file.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                Console.WriteLine($"{file.Name,-50} {size,-12} {date}");
            }
        }
        else
        {
            // Simple: Just file names
            foreach (var file in files)
            {
                Console.WriteLine($"  {file.Name}");
            }
        }

        Console.WriteLine();
    }
}

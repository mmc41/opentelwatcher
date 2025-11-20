using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.CLI.Services;
using OpenTelWatcher.Utilities;

namespace OpenTelWatcher.CLI.Commands;

/// <summary>
/// Executes telemetry file deletion business logic for the 'opentelwatcher clear' command in dual modes.
/// Instance mode: Queries GET /api/status for output directory, validates --output-dir matches (if specified),
/// calls POST /api/clear. Standalone mode (no instance): Uses TelemetryCleaner utility for direct file deletion.
/// Both modes display before/after statistics (file counts, space freed). Prevents accidental data loss via
/// directory validation. ClearCommandBuilder creates CLI structure; this class handles mode detection and cleanup.
/// </summary>
/// <remarks>
/// Scope: Telemetry file deletion, directory validation, statistics display, dual-mode coordination.
/// Builder: ClearCommandBuilder resolves port/fallback → This class: Executes cleanup via API or TelemetryCleaner
/// </remarks>
public sealed class ClearCommand
{
    private readonly IOpenTelWatcherApiClient _apiClient;
    private readonly ILoggerFactory _loggerFactory;

    public ClearCommand(IOpenTelWatcherApiClient apiClient, ILoggerFactory loggerFactory)
    {
        _apiClient = apiClient;
        _loggerFactory = loggerFactory;
    }

    public async Task<CommandResult> ExecuteAsync(string? outputDir = null, string defaultOutputDir = "./telemetry-data", bool verbose = false, bool silent = false, bool jsonOutput = false)
    {
        var cliVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        var status = await _apiClient.GetInstanceStatusAsync(cliVersion);

        if (status.IsRunning)
        {
            return await ClearViaApiAsync(outputDir, verbose, silent, jsonOutput);
        }
        else
        {
            return await ClearDirectlyAsync(outputDir, defaultOutputDir, verbose, silent, jsonOutput);
        }
    }

    private async Task<CommandResult> ClearViaApiAsync(string? userProvidedOutputDir, bool verbose, bool silent, bool jsonOutput)
    {
        var result = new Dictionary<string, object>();

        // Step 1: Get instance status
        var infoBefore = await _apiClient.GetStatusAsync();
        if (infoBefore is null)
        {
            return BuildFailedToRetrieveInfoResult(result, silent, jsonOutput);
        }

        // Step 2: Validate output directory
        var instanceOutputDir = infoBefore.Configuration.OutputDirectory;
        if (!ValidateOutputDirectory(userProvidedOutputDir, instanceOutputDir, out var error))
        {
            return BuildDirectoryMismatchResult(result, userProvidedOutputDir!, instanceOutputDir, error!, silent, jsonOutput);
        }

        // Step 3: Clear files via API
        var filesBefore = infoBefore.Files.Count;
        var sizeBeforeBytes = infoBefore.Files.TotalSizeBytes;
        var clearResponse = await _apiClient.ClearAsync();

        if (clearResponse is null)
        {
            return BuildClearFailedResult(result, silent, jsonOutput);
        }

        // Step 4: Build success result
        return BuildClearSuccessResult(result, instanceOutputDir, filesBefore, clearResponse.FilesDeleted, sizeBeforeBytes, verbose, silent, jsonOutput);
    }

    private bool ValidateOutputDirectory(string? userProvidedOutputDir, string instanceOutputDir, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(userProvidedOutputDir))
        {
            return true;
        }

        var userDirFullPath = Path.GetFullPath(userProvidedOutputDir);
        var instanceDirFullPath = Path.GetFullPath(instanceOutputDir);

        if (!string.Equals(userDirFullPath, instanceDirFullPath, StringComparison.OrdinalIgnoreCase))
        {
            error = "The running instance is using a different output directory. Either omit --output-dir to use the instance's directory, or stop the instance and run clear in standalone mode.";
            return false;
        }

        return true;
    }

    private CommandResult BuildFailedToRetrieveInfoResult(Dictionary<string, object> result, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Failed to retrieve application information";
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Failed to retrieve info");
        return CommandResult.SystemError("Failed to retrieve info");
    }

    private CommandResult BuildDirectoryMismatchResult(Dictionary<string, object> result, string requestedDir, string instanceDir, string message, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Output directory mismatch";
        result["requestedDirectory"] = Path.GetFullPath(requestedDir);
        result["instanceDirectory"] = Path.GetFullPath(instanceDir);
        result["message"] = message;
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Directory mismatch");
        return CommandResult.UserError("Output directory mismatch");
    }

    private CommandResult BuildClearFailedResult(Dictionary<string, object> result, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Failed to clear telemetry data";
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Clear failed");
        return CommandResult.SystemError("Failed to clear data");
    }

    private CommandResult BuildClearSuccessResult(Dictionary<string, object> result, string outputDir, int filesBefore, int filesDeleted, long spaceFreedBytes, bool verbose, bool silent, bool jsonOutput)
    {
        result["success"] = true;
        result["outputDirectory"] = outputDir;
        result["filesBeforeCount"] = filesBefore;
        result["filesAfterCount"] = 0;
        result["filesDeleted"] = filesDeleted;
        result["spaceFreedBytes"] = spaceFreedBytes;

        OutputResult(result, jsonOutput, silent, isError: false, verbose: verbose);
        return CommandResult.Success("Telemetry data cleared", data: new Dictionary<string, object>
        {
            ["result"] = result
        });
    }

    private async Task<CommandResult> ClearDirectlyAsync(string? outputDir, string defaultOutputDir, bool verbose, bool silent, bool jsonOutput)
    {
        outputDir ??= defaultOutputDir;
        var result = new Dictionary<string, object>();

        // Step 1: Check if directory exists
        if (!Directory.Exists(outputDir))
        {
            return BuildDirectoryNotFoundResult(result, outputDir, verbose, silent, jsonOutput);
        }

        // Step 2: Clear files directly
        try
        {
            var clearResult = await TelemetryCleaner.ClearFilesAsync(_loggerFactory, outputDir, CancellationToken.None);
            return BuildClearSuccessResult(result, clearResult.DirectoryPath, clearResult.FilesBeforeCount, clearResult.FilesDeleted, clearResult.SpaceFreedBytes, verbose, silent, jsonOutput);
        }
        catch (Exception ex)
        {
            return BuildClearExceptionResult(result, ex.Message, silent, jsonOutput);
        }
    }

    private CommandResult BuildDirectoryNotFoundResult(Dictionary<string, object> result, string outputDir, bool verbose, bool silent, bool jsonOutput)
    {
        result["success"] = true;
        result["outputDirectory"] = outputDir;
        result["filesBeforeCount"] = 0;
        result["filesAfterCount"] = 0;
        result["filesDeleted"] = 0;
        result["spaceFreedBytes"] = 0L;
        result["message"] = "Directory not found";

        OutputResult(result, jsonOutput, silent, isError: false, messageType: "Directory not found");
        return CommandResult.Success("No files to clear", data: new Dictionary<string, object>
        {
            ["result"] = result
        });
    }

    private CommandResult BuildClearExceptionResult(Dictionary<string, object> result, string exceptionMessage, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Failed to clear telemetry data";
        result["details"] = exceptionMessage;
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Exception");
        return CommandResult.SystemError("Failed to clear data");
    }

    private void OutputResult(Dictionary<string, object> result, bool jsonOutput, bool silent, bool isError, string? errorType = null, string? messageType = null, bool verbose = false)
    {
        CommandOutputFormatter.Output(result, jsonOutput, silent, _ =>
        {
            if (isError)
            {
                OutputErrorText(result, errorType!);
            }
            else
            {
                OutputSuccessText(result, messageType, verbose);
            }
        });
    }

    private void OutputErrorText(Dictionary<string, object> result, string errorType)
    {
        switch (errorType)
        {
            case "Failed to retrieve info":
                Console.WriteLine($"Error: {result["error"]}.");
                break;

            case "Directory mismatch":
                Console.WriteLine($"Error: {result["error"]}.");
                Console.WriteLine($"  Requested directory: {result["requestedDirectory"]}");
                Console.WriteLine($"  Instance directory:  {result["instanceDirectory"]}");
                Console.WriteLine();
                Console.WriteLine("The running instance is using a different output directory.");
                Console.WriteLine("Either omit --output-dir to use the instance's directory,");
                Console.WriteLine("or stop the instance and run clear in standalone mode.");
                break;

            case "Clear failed":
                Console.WriteLine($"Error: {result["error"]}.");
                break;

            case "Exception":
                Console.WriteLine($"Error: Failed to clear telemetry data: {result["details"]}.");
                break;
        }
    }

    private void OutputSuccessText(Dictionary<string, object> result, string? messageType, bool verbose)
    {
        if (messageType == "Directory not found")
        {
            Console.WriteLine($"Directory not found: {result["outputDirectory"]}.");
            Console.WriteLine("No files to clear.");
        }
        else
        {
            DisplayClearResults(
                (string)result["outputDirectory"],
                (int)result["filesBeforeCount"],
                (int)result["filesAfterCount"],
                (int)result["filesDeleted"],
                (long)result["spaceFreedBytes"],
                verbose);
        }
    }

    private void DisplayClearResults(
        string outputDir,
        int filesBefore,
        int filesAfter,
        int filesDeleted,
        long spaceFreedBytes,
        bool verbose)
    {
        Console.WriteLine("╔═══════════════════════════════════════╗");
        Console.WriteLine("║     TELEMETRY DATA CLEARED            ║");
        Console.WriteLine("╚═══════════════════════════════════════╝");
        Console.WriteLine();

        Console.WriteLine($"Directory: {outputDir}");
        Console.WriteLine();

        Console.WriteLine($"Files before:  {filesBefore}");
        Console.WriteLine($"Files deleted: {filesDeleted}");
        Console.WriteLine($"Files after:   {filesAfter}");
        Console.WriteLine();

        var spaceFree = NumberFormatter.FormatBytes(spaceFreedBytes);
        Console.WriteLine($"Space freed: {spaceFree}");

        if (verbose && filesDeleted > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"All {filesDeleted} .ndjson file(s) have been successfully deleted.");
        }

        Console.WriteLine();
    }
}

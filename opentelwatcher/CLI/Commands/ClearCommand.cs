using Microsoft.Extensions.Logging;
using System.Reflection;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.CLI.Services;
using OpenTelWatcher.Utilities;

namespace OpenTelWatcher.CLI.Commands;

/// <summary>
/// Clear command - clears telemetry data files from running instance or directly
/// </summary>
public sealed class ClearCommand
{
    private readonly IOpenTelWatcherApiClient _apiClient;
    private readonly ILoggerFactory _loggerFactory;

    public ClearCommand(IOpenTelWatcherApiClient apiClient, ILoggerFactory loggerFactory)
    {
        _apiClient = apiClient;
        _loggerFactory = loggerFactory;
    }

    public async Task<CommandResult> ExecuteAsync(string? outputDir = null, string defaultOutputDir = "./telemetry-data", bool verbose = false, bool silent = false)
    {
        // Get CLI version
        var cliVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

        // Check if instance is running
        var status = await _apiClient.GetInstanceStatusAsync(cliVersion);

        if (status.IsRunning)
        {
            // Mode 1: Instance running - use API
            return await ClearViaApiAsync(outputDir, verbose, silent);
        }
        else
        {
            // Mode 2: No instance - clear directly
            return await ClearDirectlyAsync(outputDir, defaultOutputDir, verbose, silent);
        }
    }

    private async Task<CommandResult> ClearViaApiAsync(string? userProvidedOutputDir, bool verbose, bool silent)
    {
        // Get before stats from /api/info
        var infoBefore = await _apiClient.GetInfoAsync();

        if (infoBefore is null)
        {
            Console.WriteLine("Error: Failed to retrieve application information");
            return CommandResult.SystemError("Failed to retrieve info");
        }

        var instanceOutputDir = infoBefore.Configuration.OutputDirectory;

        // Validate output directory if user provided one
        if (!string.IsNullOrWhiteSpace(userProvidedOutputDir))
        {
            var userDirFullPath = Path.GetFullPath(userProvidedOutputDir);
            var instanceDirFullPath = Path.GetFullPath(instanceOutputDir);

            if (!string.Equals(userDirFullPath, instanceDirFullPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!silent)
                {
                    Console.WriteLine("Error: Output directory mismatch");
                    Console.WriteLine($"  Requested directory: {userDirFullPath}");
                    Console.WriteLine($"  Instance directory:  {instanceDirFullPath}");
                    Console.WriteLine();
                    Console.WriteLine("The running instance is using a different output directory.");
                    Console.WriteLine("Either omit --output-dir to use the instance's directory,");
                    Console.WriteLine("or stop the instance and run clear in standalone mode.");
                }
                return CommandResult.UserError("Output directory mismatch");
            }
        }

        var filesBefore = infoBefore.Files.Count;
        var sizeBeforeBytes = infoBefore.Files.TotalSizeBytes;
        var outputDir = infoBefore.Configuration.OutputDirectory;

        // Call /api/clear
        var clearResponse = await _apiClient.ClearAsync();

        if (clearResponse is null)
        {
            Console.WriteLine("Error: Failed to clear telemetry data");
            return CommandResult.SystemError("Failed to clear data");
        }

        // Display results
        if (!silent)
        {
            DisplayClearResults(
                outputDir,
                filesBefore,
                filesAfter: 0,
                clearResponse.FilesDeleted,
                sizeBeforeBytes,
                verbose);
        }

        return CommandResult.Success("Telemetry data cleared");
    }

    private async Task<CommandResult> ClearDirectlyAsync(string? outputDir, string defaultOutputDir, bool verbose, bool silent)
    {
        // Validate directory - use configuration-based default
        outputDir ??= defaultOutputDir;

        if (!Directory.Exists(outputDir))
        {
            if (!silent)
            {
                Console.WriteLine($"Directory not found: {outputDir}");
                Console.WriteLine("No files to clear.");
            }
            return CommandResult.Success("No files to clear");
        }

        try
        {
            // Call static utility
            var result = await TelemetryCleaner.ClearFilesAsync(_loggerFactory, outputDir, CancellationToken.None);

            // Display results
            if (!silent)
            {
                DisplayClearResults(
                    result.DirectoryPath,
                    result.FilesBeforeCount,
                    filesAfter: 0,
                    result.FilesDeleted,
                    result.SpaceFreedBytes,
                    verbose);
            }

            return CommandResult.Success("Telemetry data cleared");
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                Console.WriteLine($"Error clearing telemetry data: {ex.Message}");
            }
            return CommandResult.SystemError("Failed to clear data");
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

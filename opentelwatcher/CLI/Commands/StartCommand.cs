using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.CLI.Services;
using OpenTelWatcher.Hosting;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.CLI.Commands;

/// <summary>
/// Start command - launches watcher service.
/// Actually starts the web server using IWebApplicationHost.
/// </summary>
public sealed class StartCommand
{
    private readonly ILogger<StartCommand> _logger;
    private readonly IOpenTelWatcherApiClient _apiClient;
    private readonly IWebApplicationHost _webHost;
    private readonly IPidFileService _pidFileService;

    public StartCommand(IOpenTelWatcherApiClient apiClient, IWebApplicationHost webHost, IPidFileService pidFileService, ILogger<StartCommand> logger)
    {
        _apiClient = apiClient;
        _webHost = webHost;
        _pidFileService = pidFileService;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CommandResult> ExecuteAsync(CommandOptions options, bool jsonOutput = false)
    {
        var result = new Dictionary<string, object>
        {
            ["port"] = options.Port,
            ["outputDirectory"] = options.OutputDirectory,
            ["daemon"] = options.Daemon
        };

        var cliVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

        // Step 1: Check if instance already running via API
        var status = await _apiClient.GetInstanceStatusAsync(cliVersion);
        if (status.IsRunning && status.IsCompatible)
        {
            return BuildInstanceAlreadyRunningResult(result, options.Silent, jsonOutput);
        }

        if (status.IsRunning && !status.IsCompatible)
        {
            return BuildIncompatibleInstanceResult(result, status.IncompatibilityReason!, options.Silent, jsonOutput);
        }

        // Step 1b: Check PID file for instances on the same port and clean stale entries
        var existingEntry = _pidFileService.GetEntryByPort(options.Port);
        if (existingEntry != null && existingEntry.IsRunning())
        {
            return BuildPortInUseByPidFileResult(result, options.Port, existingEntry, options.Silent, jsonOutput);
        }

        // Clean stale entries while we're at it
        _pidFileService.CleanStaleEntries();

        // Step 2: Validate configuration
        var serverOptions = new ServerOptions
        {
            Port = options.Port,
            OutputDirectory = options.OutputDirectory,
            LogLevel = options.LogLevel.ToString(),
            Daemon = options.Daemon,
            Silent = options.Silent,
            Verbose = options.Verbose
        };

        var validationResult = _webHost.Validate(serverOptions);
        if (!validationResult.IsValid)
        {
            return BuildInvalidConfigurationResult(result, validationResult.Errors, options.Silent, jsonOutput);
        }

        // Step 3: Create output directory
        try
        {
            if (!Directory.Exists(options.OutputDirectory))
            {
                Directory.CreateDirectory(options.OutputDirectory);
                result["directoryCreated"] = true;
            }
        }
        catch (Exception ex)
        {
            return BuildDirectoryCreationFailedResult(result, options.OutputDirectory, ex.Message, options.Silent, jsonOutput);
        }

        // Step 4: Start server (daemon or normal mode)
        if (options.Daemon)
        {
            result["method"] = "daemon";
            return await ForkDaemonAndExitAsync(options, jsonOutput, result);
        }

        // Normal mode: start the server
        return await StartServerNormalModeAsync(result, serverOptions, options.Silent, jsonOutput);
    }

    private CommandResult BuildInstanceAlreadyRunningResult(Dictionary<string, object> result, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Instance already running";
        result["message"] = "Use 'opentelwatcher stop' to stop the running instance first.";
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Instance already running");
        return CommandResult.UserError("Instance already running");
    }

    private CommandResult BuildPortInUseByPidFileResult(Dictionary<string, object> result, int port, PidEntry entry, bool silent, bool jsonOutput)
    {
        var uptime = DateTime.UtcNow - entry.Timestamp;
        var uptimeStr = uptime.TotalHours >= 1
            ? $"{uptime.TotalHours:F1} hours ago"
            : uptime.TotalMinutes >= 1
                ? $"{uptime.TotalMinutes:F0} minutes ago"
                : "just now";

        result["success"] = false;
        result["error"] = "Port already in use";
        result["pid"] = entry.Pid;
        result["startTime"] = entry.Timestamp;
        result["uptime"] = uptimeStr;
        result["message"] = $"Instance already running on port {port} (PID: {entry.Pid}, started: {uptimeStr})";

        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Port in use");
        return CommandResult.UserError("Port already in use");
    }

    private CommandResult BuildIncompatibleInstanceResult(Dictionary<string, object> result, string reason, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Incompatible instance detected";
        result["reason"] = reason;
        result["message"] = "Stop the incompatible instance before starting a new one.";
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Incompatible instance");
        return CommandResult.SystemError("Incompatible instance detected");
    }

    private CommandResult BuildInvalidConfigurationResult(Dictionary<string, object> result, List<string> errors, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Invalid configuration";
        result["errors"] = errors;
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Invalid configuration");
        return CommandResult.UserError("Invalid configuration");
    }

    private CommandResult BuildDirectoryCreationFailedResult(Dictionary<string, object> result, string path, string details, bool silent, bool jsonOutput)
    {
        result["success"] = false;
        result["error"] = "Cannot create output directory";
        result["path"] = NormalizePathForDisplay(path);
        result["details"] = details;
        OutputResult(result, jsonOutput, silent, isError: true, errorType: "Directory creation failed");
        return CommandResult.SystemError("Cannot create output directory");
    }

    private async Task<CommandResult> StartServerNormalModeAsync(Dictionary<string, object> result, ServerOptions serverOptions, bool silent, bool jsonOutput)
    {
        result["method"] = "normal";

        try
        {
            var exitCode = await _webHost.RunAsync(serverOptions);
            result["success"] = exitCode == 0;
            result["exitCode"] = exitCode;
            result["message"] = exitCode == 0 ? "Server stopped gracefully" : $"Server exited with code {exitCode}";

            OutputResult(result, jsonOutput, silent, isError: exitCode != 0, errorType: exitCode != 0 ? "Server exited with error" : null);

            return exitCode == 0
                ? CommandResult.Success("Server stopped gracefully")
                : CommandResult.SystemError($"Server exited with code {exitCode}");
        }
        catch (Exception ex)
        {
            result["success"] = false;
            result["error"] = "Server failed to start";
            result["details"] = ex.Message;
            OutputResult(result, jsonOutput, silent, isError: true, errorType: "Server failed to start");
            return CommandResult.SystemError("Server failed to start");
        }
    }

    private void OutputResult(Dictionary<string, object> result, bool jsonOutput, bool silent, bool isError, string? errorType = null)
    {
        if (jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        if (silent)
        {
            return;
        }

        if (isError)
        {
            OutputErrorText(result, errorType!);
        }
        else
        {
            OutputSuccessText(result);
        }
    }

    private void OutputErrorText(Dictionary<string, object> result, string errorType)
    {
        switch (errorType)
        {
            case "Instance already running":
                Console.WriteLine($"Instance already running on port {result["port"]}");
                Console.WriteLine((string)result["message"]);
                break;

            case "Port in use":
                Console.WriteLine((string)result["message"]);
                Console.WriteLine("Use 'opentelwatcher stop' to stop the running instance first.");
                break;

            case "Incompatible instance":
                Console.WriteLine($"Incompatible instance detected on port {result["port"]}");
                Console.WriteLine($"  {result["reason"]}");
                Console.WriteLine();
                Console.WriteLine("Stop the incompatible instance before starting a new one.");
                break;

            case "Invalid configuration":
                var errors = (List<string>)result["errors"];
                foreach (var err in errors)
                {
                    Console.Error.WriteLine($"Configuration error: {err}");
                }
                break;

            case "Directory creation failed":
                Console.WriteLine("Error: Cannot create output directory");
                Console.WriteLine($"  Path: {result["path"]}");
                Console.WriteLine($"  Details: {result["details"]}");
                break;

            case "Server failed to start":
                Console.Error.WriteLine($"Error starting server: {result["details"]}");
                break;

            case "Server exited with error":
                Console.WriteLine((string)result["message"]);
                break;
        }
    }

    private void OutputSuccessText(Dictionary<string, object> result)
    {
        if (result.ContainsKey("directoryCreated") && (bool)result["directoryCreated"])
        {
            Console.WriteLine($"Created output directory: {NormalizePathForDisplay((string)result["outputDirectory"])}");
        }

        if (result.ContainsKey("exitCode"))
        {
            Console.WriteLine((string)result["message"]);
        }
    }

    /// <summary>
    /// Forks a daemon process and exits the parent.
    /// Moved from Program.cs to keep daemon logic with command handling.
    /// Note: Pre-flight checks are already performed by ExecuteAsync() before calling this method.
    /// </summary>
    private async Task<CommandResult> ForkDaemonAndExitAsync(CommandOptions options, bool jsonOutput, Dictionary<string, object> result)
    {
        // Ensure output directory exists
        var createDirResult = EnsureOutputDirectoryExists(options.OutputDirectory, jsonOutput, options.Silent);
        if (!createDirResult.IsSuccess)
            return createDirResult;

        // Build child process arguments (without --daemon flag)
        var childArgs = BuildChildProcessArgs(options);

        // Determine process execution info (path, whether we need dotnet)
        var execInfo = DetermineProcessExecutionInfo();
        if (!execInfo.IsValid)
        {
            result["success"] = false;
            result["error"] = execInfo.Error!;

            if (jsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (!options.Silent)
            {
                Console.WriteLine($"Error: {execInfo.Error}");
            }

            return CommandResult.SystemError(execInfo.Error!);
        }

        // Build platform-specific process start info
        var startInfo = BuildProcessStartInfo(execInfo, childArgs);
        if (startInfo == null)
        {
            result["success"] = false;
            result["error"] = "nohup command not found";

            if (jsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }
            // Error message already printed by BuildProcessStartInfo on non-Windows

            return CommandResult.SystemError("nohup command not found");
        }

        // Start the daemon process
        if (!jsonOutput && !options.Silent)
        {
            Console.WriteLine("Starting opentelwatcher in background...");
        }

        var process = Process.Start(startInfo);
        if (process == null)
        {
            result["success"] = false;
            result["error"] = "Failed to start process";

            if (jsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (!options.Silent)
            {
                Console.WriteLine("Error: Failed to start process");
            }

            return CommandResult.SystemError("Failed to start process");
        }

        // Verify daemon started successfully
        return await VerifyDaemonStartup(process, options, execInfo.IsUnixNohup, jsonOutput, result);
    }

    /// <summary>
    /// Ensures the output directory exists, creating it if necessary.
    /// </summary>
    private CommandResult EnsureOutputDirectoryExists(string outputDirectory, bool jsonOutput, bool silent)
    {
        if (Directory.Exists(outputDirectory))
            return CommandResult.Success("Directory exists");

        try
        {
            Directory.CreateDirectory(outputDirectory);
            if (!jsonOutput && !silent)
            {
                Console.WriteLine($"Created output directory: {NormalizePathForDisplay(outputDirectory)}");
            }
            return CommandResult.Success("Directory created");
        }
        catch (Exception ex)
        {
            var result = new Dictionary<string, object>
            {
                ["success"] = false,
                ["error"] = "Cannot create output directory",
                ["details"] = ex.Message
            };

            if (jsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (!silent)
            {
                Console.WriteLine($"Error: Cannot create output directory: {ex.Message}");
            }

            return CommandResult.SystemError("Cannot create output directory");
        }
    }

    /// <summary>
    /// Determines the process execution information (path, assembly path, whether dotnet is needed).
    /// </summary>
    private ProcessExecutionInfo DetermineProcessExecutionInfo()
    {
        var currentProcess = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentProcess))
        {
            Console.WriteLine("Error: Cannot determine process path");
            Console.WriteLine("This may indicate an unusual deployment scenario.");
            return ProcessExecutionInfo.Invalid("Cannot determine process path");
        }

#pragma warning disable IL3000 // Assembly.Location may be empty for single-file apps
        var assemblyPath = typeof(Program).Assembly.Location;
#pragma warning restore IL3000

        if (string.IsNullOrEmpty(assemblyPath))
        {
            // Single-file deployment: use ProcessPath directly
            return new ProcessExecutionInfo
            {
                IsValid = true,
                AssemblyPath = currentProcess,
                NeedsDotnet = false,
                IsUnixNohup = !OperatingSystem.IsWindows()
            };
        }

        // Regular deployment
        bool needsDotnet = assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        return new ProcessExecutionInfo
        {
            IsValid = true,
            AssemblyPath = assemblyPath,
            NeedsDotnet = needsDotnet,
            IsUnixNohup = !OperatingSystem.IsWindows()
        };
    }

    /// <summary>
    /// Builds platform-specific process start info for daemon mode.
    /// Returns null if nohup is not available on Unix systems.
    /// </summary>
    private ProcessStartInfo? BuildProcessStartInfo(ProcessExecutionInfo execInfo, string childArgs)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = execInfo.NeedsDotnet ? "dotnet" : execInfo.AssemblyPath,
                Arguments = execInfo.NeedsDotnet
                    ? $"\"{execInfo.AssemblyPath}\" {childArgs}"
                    : childArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                WorkingDirectory = Environment.CurrentDirectory,
                CreateNewProcessGroup = true
            };
        }
        else
        {
            // Linux/macOS: Use nohup for proper daemon behavior
            if (!IsNohupAvailable())
            {
                Console.WriteLine("Error: 'nohup' command not found");
                Console.WriteLine();
                Console.WriteLine("Daemon mode on Linux/macOS requires 'nohup' (from coreutils) to be installed.");
                Console.WriteLine();
                Console.WriteLine("Alternatively, run without --daemon flag for foreground mode.");
                return null;
            }

            var command = execInfo.NeedsDotnet
                ? $"dotnet \"{execInfo.AssemblyPath}\" {childArgs}"
                : $"\"{execInfo.AssemblyPath}\" {childArgs}";

            var shellPath = GetShellPath();
            return new ProcessStartInfo
            {
                FileName = shellPath,
                Arguments = $"-c \"nohup {command} >/dev/null 2>&1 &\"",
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                WorkingDirectory = Environment.CurrentDirectory
            };
        }
    }

    /// <summary>
    /// Verifies that the daemon started successfully via health checks.
    /// </summary>
    private async Task<CommandResult> VerifyDaemonStartup(Process process, CommandOptions options, bool isUnixNohup, bool jsonOutput, Dictionary<string, object> result)
    {
        bool healthy = await WaitForHealthCheckAsync(_apiClient, timeoutSeconds: ApiConstants.Timeouts.HealthCheckSeconds);

        if (healthy)
        {
            result["success"] = true;
            result["message"] = "Daemon started successfully";

            if (jsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (!options.Silent)
            {
                Console.WriteLine($"Watcher started successfully on port {options.Port}");
                Console.WriteLine($"Output directory: {NormalizePathForDisplay(options.OutputDirectory)}");
            }

            return CommandResult.Success("Daemon started");
        }

        // Health check failed
        result["success"] = false;
        result["error"] = "Daemon failed to start";

        var errorDetails = new List<string>();
        errorDetails.Add($"No response after {ApiConstants.Timeouts.HealthCheckSeconds} seconds");

        // On Windows, check if child process is still running
        if (!isUnixNohup)
        {
            try
            {
                process.Refresh();
                if (process.HasExited)
                {
                    errorDetails.Add($"Child process exited unexpectedly with code: {process.ExitCode}");
                }
                else
                {
                    errorDetails.Add("Child process is running but not responding to health checks");
                }
            }
            catch (Exception ex)
            {
                errorDetails.Add($"Could not check child process status: {ex.Message}");
            }
        }
        else
        {
            errorDetails.Add("The daemon may have failed to start or is taking longer than expected");
        }

        result["details"] = errorDetails;
        result["tip"] = "Run without --daemon to see detailed output and error messages";

        if (jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        else if (!options.Silent)
        {
            Console.WriteLine($"Error: Watcher failed to start (no response after {ApiConstants.Timeouts.HealthCheckSeconds} seconds)");

            // Display details
            foreach (var detail in errorDetails)
            {
                Console.WriteLine(detail);
            }

            Console.WriteLine("Tip: Run without --daemon to see detailed output and error messages.");
        }

        return CommandResult.SystemError("Daemon failed to start");
    }

    /// <summary>
    /// Information about process execution for daemon mode.
    /// </summary>
    private record ProcessExecutionInfo
    {
        public bool IsValid { get; init; }
        public string? AssemblyPath { get; init; }
        public bool NeedsDotnet { get; init; }
        public bool IsUnixNohup { get; init; }
        public string? Error { get; init; }

        public static ProcessExecutionInfo Invalid(string error) => new()
        {
            IsValid = false,
            Error = error
        };
    }

    /// <summary>
    /// Builds command-line arguments for child process (without --daemon flag).
    /// </summary>
    private string BuildChildProcessArgs(CommandOptions options)
    {
        var args = new List<string> { "start" };

        args.Add("--port");
        args.Add(options.Port.ToString());

        args.Add("--output-dir");
        args.Add(QuoteIfNeeded(options.OutputDirectory));

        args.Add("--log-level");
        args.Add(options.LogLevel.ToString());

        // Note: Daemon flag is intentionally excluded

        return string.Join(" ", args);
    }

    /// <summary>
    /// Quotes an argument if it contains spaces.
    /// </summary>
    private string QuoteIfNeeded(string arg)
    {
        return arg.Contains(" ") ? $"\"{arg}\"" : arg;
    }

    /// <summary>
    /// Waits for the watcher to respond to health checks.
    /// </summary>
    private async Task<bool> WaitForHealthCheckAsync(IOpenTelWatcherApiClient apiClient, int timeoutSeconds)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed.TotalSeconds < timeoutSeconds)
        {
            try
            {
                var info = await apiClient.GetStatusAsync();
                if (info != null)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                // Expected during startup - keep retrying
                _logger.LogDebug(ex, "Health check failed during daemon startup (elapsed: {ElapsedSeconds}s), will retry", stopwatch.Elapsed.TotalSeconds);
            }

            await Task.Delay(ApiConstants.Timeouts.HealthCheckPollIntervalMs);
        }
        return false;
    }

    /// <summary>
    /// Checks if the 'nohup' command is available on the system.
    /// Used on Linux/macOS for daemon mode implementation.
    /// </summary>
    /// <returns>True if nohup is available, false otherwise</returns>
    private static bool IsNohupAvailable()
    {
        try
        {
            var shellPath = GetShellPath();
            var checkProcess = new ProcessStartInfo
            {
                FileName = shellPath,
                Arguments = "-c \"command -v nohup\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(checkProcess);
            if (process == null)
            {
                return false;
            }

            process.WaitForExit(1000); // 1 second timeout
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            // If we can't check, assume it's not available
            // Cannot log from static method
            return false;
        }
    }

    /// <summary>
    /// Finds the path to a POSIX-compliant shell on Unix systems.
    /// Tries multiple common shell locations in order of preference.
    /// Works across different macOS versions (Zsh/Bash), Linux distributions, and BSD variants.
    /// </summary>
    /// <returns>Path to the shell executable</returns>
    /// <exception cref="PlatformNotSupportedException">Thrown when no compatible shell is found</exception>
    private static string GetShellPath()
    {
        // Try common shell locations in order of preference
        // Prioritize /bin/sh (POSIX standard) for maximum compatibility
        // macOS: /bin/sh, /bin/zsh (Catalina+), /bin/bash (Mojave and earlier)
        // Linux: /bin/sh, /bin/bash, /bin/dash (Debian/Ubuntu as /bin/sh)
        // Alpine: /bin/sh, /bin/ash
        var shellPaths = new[]
        {
            "/bin/sh",      // POSIX standard shell (all Unix systems, symlink to actual shell)
            "/bin/bash",    // Bourne Again Shell (macOS Mojave-, most Linux distros)
            "/bin/zsh",     // Z shell (macOS Catalina+ default, also available on Linux)
            "/bin/dash",    // Debian Almquist Shell (used as /bin/sh on Debian/Ubuntu)
            "/bin/ash",     // Almquist Shell (Alpine Linux)
            "/bin/fish",    // Friendly Interactive Shell (optional, user-installed)
            "/usr/bin/sh",  // Alternative location (some BSD variants)
            "/usr/bin/bash" // Alternative location (some systems)
        };

        foreach (var shellPath in shellPaths)
        {
            if (File.Exists(shellPath))
            {
                return shellPath;
            }
        }

        throw new PlatformNotSupportedException(
            "No compatible POSIX shell found. Daemon mode requires a shell (sh, bash, zsh, etc.).");
    }

    /// <summary>
    /// Normalizes a file path for display by converting backslashes to forward slashes.
    /// This provides consistent display across platforms (Windows paths will show with forward slashes).
    /// </summary>
    private static string NormalizePathForDisplay(string path)
    {
        return path.Replace('\\', '/');
    }
}

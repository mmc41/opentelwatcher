using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.CLI.Services;
using OpenTelWatcher.Hosting;

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

    public StartCommand(IOpenTelWatcherApiClient apiClient, IWebApplicationHost webHost, ILogger<StartCommand> logger)
    {
        _apiClient = apiClient;
        _webHost = webHost;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CommandResult> ExecuteAsync(CommandOptions options)
    {
        // Get CLI version
        var cliVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

        // Pre-flight check: is instance already running?
        var status = await _apiClient.GetInstanceStatusAsync(cliVersion);

        if (status.IsRunning && status.IsCompatible)
        {
            var config = new OpenTelWatcher.Utilities.ApplicationInfoConfig
            {
                Version = status.Version!.Version,
                Port = options.Port,
                OutputDirectory = options.OutputDirectory,
                Silent = options.Silent,
                ErrorMessage = $"Instance already running on port {options.Port}",
                ErrorDetails = "\nUse 'opentelwatcher stop' to stop the running instance first."
            };
            OpenTelWatcher.Utilities.ApplicationInfoDisplay.Display(OpenTelWatcher.Utilities.DisplayMode.Error, config);
            return CommandResult.UserError("Instance already running");
        }

        if (status.IsRunning && !status.IsCompatible)
        {
            var config = new OpenTelWatcher.Utilities.ApplicationInfoConfig
            {
                Version = status.Version!.Version,
                Port = options.Port,
                OutputDirectory = options.OutputDirectory,
                Silent = options.Silent,
                ErrorMessage = $"Incompatible instance detected on port {options.Port}",
                ErrorDetails = $"{status.IncompatibilityReason}\n\nStop the incompatible instance before starting a new one."
            };
            OpenTelWatcher.Utilities.ApplicationInfoDisplay.Display(OpenTelWatcher.Utilities.DisplayMode.Error, config);
            return CommandResult.SystemError("Incompatible instance detected");
        }

        // Convert to ServerOptions
        var serverOptions = new ServerOptions
        {
            Port = options.Port,
            OutputDirectory = options.OutputDirectory,
            LogLevel = options.LogLevel.ToString(),
            Daemon = options.Daemon,
            Silent = options.Silent,
            Verbose = options.Verbose
        };

        // Validate options
        var validationResult = _webHost.Validate(serverOptions);
        if (!validationResult.IsValid)
        {
            foreach (var error in validationResult.Errors)
            {
                Console.Error.WriteLine($"Configuration error: {error}");
            }
            return CommandResult.UserError("Invalid configuration");
        }

        // Create output directory if it doesn't exist
        try
        {
            if (!Directory.Exists(options.OutputDirectory))
            {
                Directory.CreateDirectory(options.OutputDirectory);
                Console.WriteLine($"Created output directory: {NormalizePathForDisplay(options.OutputDirectory)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: Cannot create output directory");
            Console.WriteLine($"  Path: {NormalizePathForDisplay(options.OutputDirectory)}");
            Console.WriteLine($"  Details: {ex.Message}");
            return CommandResult.SystemError("Cannot create output directory");
        }

        // Daemon mode: fork process and exit
        if (options.Daemon)
        {
            return await ForkDaemonAndExitAsync(options);
        }

        // Normal mode: start the server (THIS ACTUALLY STARTS THE SERVER!)
        try
        {
            var exitCode = await _webHost.RunAsync(serverOptions);
            return exitCode == 0
                ? CommandResult.Success("Server stopped gracefully")
                : CommandResult.SystemError($"Server exited with code {exitCode}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error starting server: {ex.Message}");
            return CommandResult.SystemError("Server failed to start");
        }
    }

    /// <summary>
    /// Forks a daemon process and exits the parent.
    /// Moved from Program.cs to keep daemon logic with command handling.
    /// Note: Pre-flight checks are already performed by ExecuteAsync() before calling this method.
    /// </summary>
    private async Task<CommandResult> ForkDaemonAndExitAsync(CommandOptions options)
    {
        // Ensure output directory exists
        var createDirResult = EnsureOutputDirectoryExists(options.OutputDirectory);
        if (!createDirResult.IsSuccess)
            return createDirResult;

        // Build child process arguments (without --daemon flag)
        var childArgs = BuildChildProcessArgs(options);

        // Determine process execution info (path, whether we need dotnet)
        var execInfo = DetermineProcessExecutionInfo();
        if (!execInfo.IsValid)
            return CommandResult.SystemError(execInfo.Error!);

        // Build platform-specific process start info
        var startInfo = BuildProcessStartInfo(execInfo, childArgs);
        if (startInfo == null)
            return CommandResult.SystemError("nohup command not found");

        // Start the daemon process
        Console.WriteLine("Starting opentelwatcher in background...");
        var process = Process.Start(startInfo);
        if (process == null)
        {
            Console.WriteLine("Error: Failed to start process");
            return CommandResult.SystemError("Failed to start process");
        }

        // Verify daemon started successfully
        return await VerifyDaemonStartup(process, options, execInfo.IsUnixNohup);
    }

    /// <summary>
    /// Ensures the output directory exists, creating it if necessary.
    /// </summary>
    private CommandResult EnsureOutputDirectoryExists(string outputDirectory)
    {
        if (Directory.Exists(outputDirectory))
            return CommandResult.Success("Directory exists");

        try
        {
            Directory.CreateDirectory(outputDirectory);
            Console.WriteLine($"Created output directory: {NormalizePathForDisplay(outputDirectory)}");
            return CommandResult.Success("Directory created");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: Cannot create output directory: {ex.Message}");
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
    private async Task<CommandResult> VerifyDaemonStartup(Process process, CommandOptions options, bool isUnixNohup)
    {
        bool healthy = await WaitForHealthCheckAsync(_apiClient, timeoutSeconds: ApiConstants.Timeouts.HealthCheckSeconds);

        if (healthy)
        {
            Console.WriteLine($"Watcher started successfully on port {options.Port}");
            Console.WriteLine($"Output directory: {NormalizePathForDisplay(options.OutputDirectory)}");
            return CommandResult.Success("Daemon started");
        }

        // Health check failed
        Console.WriteLine($"Error: Watcher failed to start (no response after {ApiConstants.Timeouts.HealthCheckSeconds} seconds)");

        // On Windows, check if child process is still running
        if (!isUnixNohup)
        {
            try
            {
                process.Refresh();
                if (process.HasExited)
                {
                    Console.WriteLine($"Child process exited unexpectedly with code: {process.ExitCode}");
                }
                else
                {
                    Console.WriteLine("Child process is running but not responding to health checks.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not check child process status: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("The daemon may have failed to start or is taking longer than expected.");
        }

        Console.WriteLine("Tip: Run without --daemon to see detailed output and error messages.");
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
                var info = await apiClient.GetInfoAsync();
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

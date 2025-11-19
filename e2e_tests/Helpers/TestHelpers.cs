using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelWatcher.Configuration;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// General-purpose helper utilities for E2E tests.
/// Provides methods for running CLI commands, managing server processes, health checks,
/// and common test infrastructure utilities.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Finds the solution root by looking for the project.root marker file.
    /// Uses AppContext.BaseDirectory for more reliable test discovery.
    /// </summary>
    /// <returns>The absolute path to the solution root directory</returns>
    private static string FindSolutionRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "project.root")))
            {
                return directory;
            }
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new InvalidOperationException(
            "Could not find solution root. Ensure project.root file exists at solution root.");
    }

    /// <summary>
    /// Solution root directory (absolute path).
    /// </summary>
    public static readonly string SolutionRoot = FindSolutionRoot();

    /// <summary>
    /// Base directory for all E2E test artifacts (absolute path).
    /// Located in artifacts/test-results/e2e/ per project structure.
    /// </summary>
    public static readonly string BaseTestOutputDir = Path.Combine(
        SolutionRoot, "artifacts", "test-results", "e2e");

    /// <summary>
    /// Default test port for E2E tests to avoid conflicts with production instances.
    /// </summary>
    public const int DefaultTestPort = 5318;

    /// <summary>
    /// Gets a test-specific output directory within the E2E test artifacts folder.
    /// Creates the directory if it doesn't exist.
    /// </summary>
    /// <param name="testName">Name of the test or test suite (e.g., "check-command")</param>
    /// <returns>Full absolute path to the test output directory</returns>
    public static string GetTestOutputDir(string testName)
    {
        var testDir = Path.Combine(BaseTestOutputDir, testName);
        Directory.CreateDirectory(testDir); // Ensure directory exists
        return testDir;
    }

    /// <summary>
    /// Gets a random port from the thread-safe allocator (6000-7000 range).
    /// IMPORTANT: Remember to release the port with PortAllocator.Release() when done!
    /// </summary>
    public static int GetRandomPort()
    {
        return PortAllocator.Allocate();
    }

    /// <summary>
    /// Runs a CLI command and returns its exit code.
    /// </summary>
    /// <param name="executablePath">Path to the watcher executable</param>
    /// <param name="arguments">Command arguments</param>
    /// <param name="solutionRoot">Solution root directory</param>
    /// <param name="timeoutSeconds">Timeout in seconds (default: 10)</param>
    /// <param name="logger">Optional logger for diagnostic output</param>
    /// <returns>The exit code of the command</returns>
    public static async Task<int> RunCliCommandAsync(
        string executablePath,
        string arguments,
        string solutionRoot,
        int timeoutSeconds = 10,
        ILogger? logger = null)
    {
        logger?.LogDebug("Running CLI command: {Executable} {Arguments}", executablePath, arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = solutionRoot
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            var error = "Failed to start CLI process";
            logger?.LogError(error);
            throw new InvalidOperationException(error);
        }

        logger?.LogDebug("CLI process started (PID: {ProcessId})", process.Id);

        // Wait for process to complete with timeout
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));

            logger?.LogDebug("CLI command completed with exit code {ExitCode}", process.ExitCode);
        }
        catch (TimeoutException)
        {
            logger?.LogWarning("CLI command timed out after {TimeoutSeconds}s, killing process {ProcessId}",
                timeoutSeconds, process.Id);
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"CLI command timed out after {timeoutSeconds} seconds");
        }

        return process.ExitCode;
    }

    /// <summary>
    /// Starts a server on the specified port and returns the process.
    /// </summary>
    /// <param name="executablePath">Path to the watcher executable</param>
    /// <param name="port">Port number to use</param>
    /// <param name="solutionRoot">Solution root directory</param>
    /// <returns>The started process</returns>
    public static async Task<Process> StartServerAsync(
        string executablePath,
        int port,
        string solutionRoot)
    {
        var outputDir = Path.Combine(solutionRoot, "artifacts", "test-telemetry", $"port-{port}");
        Directory.CreateDirectory(outputDir);

        return await StartServerWithOutputDirAsync(executablePath, port, solutionRoot, outputDir);
    }

    /// <summary>
    /// Starts a server on the specified port with a custom output directory and returns the process.
    /// </summary>
    /// <param name="executablePath">Path to the watcher executable</param>
    /// <param name="port">Port number to use</param>
    /// <param name="solutionRoot">Solution root directory</param>
    /// <param name="outputDir">Output directory for telemetry data</param>
    /// <returns>The started process</returns>
    public static async Task<Process> StartServerWithOutputDirAsync(
        string executablePath,
        int port,
        string solutionRoot,
        string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = $"start --port {port} --output-dir \"{outputDir}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = solutionRoot
        };

        var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start server process");
        }

        return process;
    }

    /// <summary>
    /// Waits for the server to become healthy by polling the health endpoint.
    /// </summary>
    /// <param name="port">Port number to check</param>
    /// <param name="maxAttempts">Maximum number of attempts (default: 15)</param>
    /// <param name="logger">Optional logger for diagnostic output</param>
    /// <returns>Task that completes when server is healthy</returns>
    public static async Task WaitForServerHealthyAsync(int port, int maxAttempts = 15, ILogger? logger = null)
    {
        logger?.LogDebug("Waiting for server on port {Port} to become healthy (max {MaxAttempts} attempts)",
            port, maxAttempts);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var url = $"http://{ApiConstants.Network.LocalhostIp}:{port}{E2EConstants.WebEndpoints.Health}";

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    logger?.LogDebug("Server on port {Port} is healthy (attempt {Attempt}/{MaxAttempts})",
                        port, attempt + 1, maxAttempts);
                    return;
                }
                logger?.LogDebug("Server returned {StatusCode} (attempt {Attempt}/{MaxAttempts})",
                    response.StatusCode, attempt + 1, maxAttempts);
            }
            catch (HttpRequestException ex)
            {
                // Expected while server is starting
                logger?.LogDebug("Connection failed (attempt {Attempt}/{MaxAttempts}): {Message}",
                    attempt + 1, maxAttempts, ex.Message);
            }
            catch (TaskCanceledException)
            {
                // Timeout - expected while server is starting
                logger?.LogDebug("Request timed out (attempt {Attempt}/{MaxAttempts})",
                    attempt + 1, maxAttempts);
            }

            await Task.Delay(E2EConstants.Delays.HealthCheckPollingMs);
        }

        var error = $"Server on port {port} did not become healthy within {maxAttempts} seconds";
        logger?.LogError(error);
        throw new TimeoutException(error);
    }

    /// <summary>
    /// Ensures no instance is running on the specified port.
    /// If an instance is running, it will be stopped.
    /// </summary>
    /// <param name="port">Port number to check</param>
    /// <param name="logger">Optional logger for diagnostic output</param>
    public static async Task EnsureNoInstanceRunningAsync(int port, ILogger? logger = null)
    {
        logger?.LogDebug("Checking if instance is running on port {Port}", port);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var url = $"http://{ApiConstants.Network.LocalhostIp}:{port}{E2EConstants.WebEndpoints.Health}";

        try
        {
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                // Instance is running - stop it
                logger?.LogInformation("Instance found running on port {Port}, stopping it", port);
                await StopServerOnPortAsync(port, logger);

                // Wait for server to stop by polling health endpoint (should return false)
                await PollingHelpers.WaitForConditionAsync(
                    conditionAsync: async () => !await CheckServerHealthAsync(port, logger),
                    timeoutMs: 5000,
                    pollingIntervalMs: 100,
                    cancellationToken: default,
                    logger: logger,
                    conditionDescription: $"server on port {port} to stop");

                logger?.LogDebug("Instance stopped on port {Port}", port);
            }
        }
        catch (HttpRequestException ex)
        {
            // No instance running - good
            logger?.LogDebug("No instance running on port {Port}: {Message}", port, ex.Message);
        }
        catch (TaskCanceledException)
        {
            // Timeout - no instance running - good
            logger?.LogDebug("No instance running on port {Port} (timeout)", port);
        }
        catch (OperationCanceledException)
        {
            // Canceled - no instance running - good
            logger?.LogDebug("No instance running on port {Port} (canceled)", port);
        }
    }

    /// <summary>
    /// Stops a server by sending shutdown request and optionally killing the process.
    /// </summary>
    /// <param name="port">Port number of the server</param>
    /// <param name="process">Optional process to kill if shutdown fails</param>
    /// <param name="logger">Optional logger for diagnostic output</param>
    public static async Task StopServerAsync(int port, Process? process, ILogger? logger = null)
    {
        logger?.LogDebug("Stopping server on port {Port} (PID: {ProcessId})", port, process?.Id);

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            await client.PostAsync($"http://{ApiConstants.Network.LocalhostIp}:{port}{E2EConstants.ApiEndpoints.Stop}", null);

            logger?.LogDebug("Shutdown request sent, waiting for graceful exit");

            // Wait for graceful shutdown
            if (process != null && !process.HasExited)
            {
                var exited = process.WaitForExit(5000);
                if (exited)
                {
                    logger?.LogDebug("Server exited gracefully");
                }
                else
                {
                    logger?.LogWarning("Server did not exit gracefully within 5 seconds");
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning("Error during shutdown request: {Message}", ex.Message);

            // If shutdown fails, kill the process
            if (process != null && !process.HasExited)
            {
                logger?.LogWarning("Forcefully killing process {ProcessId}", process.Id);
                process.Kill(entireProcessTree: true);
            }
        }
    }

    /// <summary>
    /// Stops a server on the specified port by sending a shutdown request.
    /// </summary>
    /// <param name="port">Port number of the server</param>
    /// <param name="logger">Optional logger for diagnostic output</param>
    public static async Task StopServerOnPortAsync(int port, ILogger? logger = null)
    {
        logger?.LogDebug("Sending stop request to server on port {Port}", port);

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            await client.PostAsync($"http://{ApiConstants.Network.LocalhostIp}:{port}{E2EConstants.ApiEndpoints.Stop}", null);

            // Wait for server to stop by polling health endpoint (should return false)
            await PollingHelpers.WaitForConditionAsync(
                conditionAsync: async () => !await CheckServerHealthAsync(port, logger),
                timeoutMs: 5000,
                pollingIntervalMs: 100,
                cancellationToken: default,
                logger: logger,
                conditionDescription: $"server on port {port} to stop");

            logger?.LogDebug("Stop request completed for port {Port}", port);
        }
        catch (Exception ex)
        {
            logger?.LogDebug("Error stopping server on port {Port}: {Message}", port, ex.Message);
            // Ignore errors - server may already be stopped
        }
    }

    /// <summary>
    /// Cleans up output directory.
    /// </summary>
    /// <param name="path">Path to the directory to delete</param>
    /// <param name="logger">Optional logger for diagnostic output</param>
    public static void CleanupOutputDirectory(string path, ILogger? logger = null)
    {
        try
        {
            if (Directory.Exists(path))
            {
                logger?.LogDebug("Cleaning up directory: {Path}", path);
                Directory.Delete(path, recursive: true);
                logger?.LogDebug("Directory cleaned up: {Path}", path);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning("Error cleaning up directory {Path}: {Message}", path, ex.Message);
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Gets the path to the watcher executable and sets up proper permissions on Unix.
    /// </summary>
    /// <param name="solutionRoot">Solution root directory</param>
    /// <returns>Path to the watcher executable</returns>
    public static string GetWatcherExecutablePath(string solutionRoot)
    {
        var binDir = Path.Combine(solutionRoot, "artifacts", "bin", "opentelwatcher", "Debug");

        // Use watcher.exe on Windows, watcher on Unix
        var executableName = OperatingSystem.IsWindows() ? "opentelwatcher.exe" : "opentelwatcher";
        var executablePath = Path.Combine(binDir, executableName);

        if (!File.Exists(executablePath))
        {
            // Fallback to DLL
            var dllPath = Path.Combine(binDir, "opentelwatcher.dll");
            if (!File.Exists(dllPath))
            {
                throw new FileNotFoundException(
                    $"Watcher executable not found at {executablePath} or {dllPath}. " +
                    "Please run 'dotnet build' before running E2E tests.");
            }
        }

        // Ensure executable permission on Unix
        if (!OperatingSystem.IsWindows() && File.Exists(executablePath))
        {
            try
            {
                var chmod = Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{executablePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                chmod?.WaitForExit();
            }
            catch (Exception ex)
            {
                // Log permission errors to help debug Unix-specific issues
                var logger = TestLoggerFactory.CreateLogger(typeof(TestHelpers));
                logger.LogWarning(ex, "Failed to set executable permission on {ExecutablePath} (file might already be executable)", executablePath);
            }
        }

        return executablePath;
    }

    /// <summary>
    /// Gets the path to the opentelwatcher.csproj project file.
    /// </summary>
    /// <returns>Absolute path to the project file</returns>
    public static string GetProjectPath()
    {
        return Path.Combine(SolutionRoot, "opentelwatcher", "opentelwatcher.csproj");
    }

    /// <summary>
    /// Runs a CLI command and returns the result with captured output.
    /// </summary>
    /// <param name="commandArgs">Command arguments (e.g., "check --output-dir ./data")</param>
    /// <returns>CommandResult with exit code, stdout, and stderr</returns>
    public static async Task<CommandResult> RunCliCommandWithOutputAsync(string commandArgs)
    {
        var projectPath = GetProjectPath();
        var arguments = $"run --project {projectPath} -- {commandArgs}";

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new CommandResult
        {
            ExitCode = process.ExitCode,
            Output = output,
            Error = error
        };
    }

    /// <summary>
    /// Checks if a server is healthy on the specified port.
    /// Returns true if the health endpoint responds with success, false otherwise.
    /// </summary>
    /// <param name="port">Port number to check</param>
    /// <param name="logger">Optional logger for diagnostic output</param>
    /// <returns>True if server is healthy, false otherwise</returns>
    public static async Task<bool> CheckServerHealthAsync(int port, ILogger? logger = null)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var url = $"http://{ApiConstants.Network.LocalhostIp}:{port}{E2EConstants.WebEndpoints.Health}";
            var response = await client.GetAsync(url);

            logger?.LogDebug("Health check for port {Port}: {StatusCode}", port, response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger?.LogDebug("Health check failed for port {Port}: {Message}", port, ex.Message);
            return false;
        }
    }
}

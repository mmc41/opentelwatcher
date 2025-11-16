using System.Diagnostics;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.E2ETests.Helpers;

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
    /// Gets a random port in the range 6000-7000 to avoid conflicts.
    /// </summary>
    public static int GetRandomPort()
    {
        return Random.Shared.Next(6000, 7000);
    }

    /// <summary>
    /// Runs a CLI command and returns its exit code.
    /// </summary>
    /// <param name="executablePath">Path to the watcher executable</param>
    /// <param name="arguments">Command arguments</param>
    /// <param name="solutionRoot">Solution root directory</param>
    /// <param name="timeoutSeconds">Timeout in seconds (default: 10)</param>
    /// <returns>The exit code of the command</returns>
    public static async Task<int> RunCliCommandAsync(
        string executablePath,
        string arguments,
        string solutionRoot,
        int timeoutSeconds = 10)
    {
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
            throw new InvalidOperationException("Failed to start CLI process");
        }

        // Wait for process to complete with timeout
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
        }
        catch (TimeoutException)
        {
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

        // Give server time to start
        await Task.Delay(2000);

        return process;
    }

    /// <summary>
    /// Waits for the server to become healthy by polling the health endpoint.
    /// </summary>
    /// <param name="port">Port number to check</param>
    /// <param name="maxAttempts">Maximum number of attempts (default: 15)</param>
    /// <returns>Task that completes when server is healthy</returns>
    public static async Task WaitForServerHealthyAsync(int port, int maxAttempts = 15)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var url = $"http://{ApiConstants.Network.LocalhostIp}:{port}/healthz";

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Expected while server is starting
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"Server on port {port} did not become healthy within {maxAttempts} seconds");
    }

    /// <summary>
    /// Ensures no instance is running on the specified port.
    /// If an instance is running, it will be stopped.
    /// </summary>
    /// <param name="port">Port number to check</param>
    public static async Task EnsureNoInstanceRunningAsync(int port)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var url = $"http://{ApiConstants.Network.LocalhostIp}:{port}/healthz";

        try
        {
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                // Instance is running - stop it
                await StopServerOnPortAsync(port);
                await Task.Delay(2000); // Wait for shutdown
            }
        }
        catch (HttpRequestException)
        {
            // No instance running - good
        }
        catch (TaskCanceledException)
        {
            // Timeout - no instance running - good
        }
        catch (OperationCanceledException)
        {
            // Canceled - no instance running - good
        }
    }

    /// <summary>
    /// Stops a server by sending shutdown request and optionally killing the process.
    /// </summary>
    /// <param name="port">Port number of the server</param>
    /// <param name="process">Optional process to kill if shutdown fails</param>
    public static async Task StopServerAsync(int port, Process? process)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            await client.PostAsync($"http://{ApiConstants.Network.LocalhostIp}:{port}/api/stop", null);

            // Wait for graceful shutdown
            if (process != null && !process.HasExited)
            {
                process.WaitForExit(5000);
            }
        }
        catch
        {
            // If shutdown fails, kill the process
            if (process != null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    /// <summary>
    /// Stops a server on the specified port by sending a shutdown request.
    /// </summary>
    /// <param name="port">Port number of the server</param>
    public static async Task StopServerOnPortAsync(int port)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            await client.PostAsync($"http://{ApiConstants.Network.LocalhostIp}:{port}/api/stop", null);
            await Task.Delay(2000); // Wait for shutdown
        }
        catch
        {
            // Ignore errors
        }
    }

    /// <summary>
    /// Cleans up output directory.
    /// </summary>
    /// <param name="path">Path to the directory to delete</param>
    public static void CleanupOutputDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
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
            catch
            {
                // Ignore - file might already be executable
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
}

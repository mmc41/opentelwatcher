using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelWatcher.Configuration;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// Base class for WatcherServer fixtures that provides shared functionality.
/// Implements IAsyncLifetime for proper async startup/shutdown lifecycle management.
/// </summary>
public abstract class OpenTelWatcherServerFixtureBase : IAsyncLifetime, IDisposable
{
    protected Process? _watcherProcess;
    private HttpClient? _client;
    protected readonly int _port;
    private readonly string _baseUrl;
    private bool _disposed;
    protected readonly ILogger<OpenTelWatcherServerFixtureBase> _logger;

    /// <summary>
    /// Gets the HTTP client configured to communicate with the watcher subprocess.
    /// </summary>
    public HttpClient Client => _client ?? throw new InvalidOperationException("Fixture not initialized");

    /// <summary>
    /// Gets the port number the watcher is running on.
    /// </summary>
    public int Port => _port;

    /// <summary>
    /// Gets the base URL of the watcher (e.g., http://ApiConstants.Network.LocalhostIp:5432).
    /// </summary>
    public string BaseUrl => _baseUrl;

    protected OpenTelWatcherServerFixtureBase()
    {
        // Use a random port to avoid conflicts with other test runs
        _port = Random.Shared.Next(5000, 6000);
        _baseUrl = $"http://{ApiConstants.Network.LocalhostIp}:{_port}";

        // Get logger from TestLoggerFactory
        _logger = TestLoggerFactory.CreateLogger<OpenTelWatcherServerFixtureBase>();

        _logger.LogInformation("Fixture {FixtureName} created with port {Port}", GetType().Name, _port);
    }

    public async ValueTask InitializeAsync()
    {
        _logger.LogInformation("Initializing Watcher subprocess on port {0}", _port);

        _client = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Start the watcher subprocess (implementation-specific)
        _watcherProcess = await StartWatcherSubprocessAsync();

        // Wait for the watcher to be ready
        await WaitForWatcherReadyAsync();

        _logger.LogInformation("Watcher subprocess ready on port {0}", _port);
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing Watcher subprocess (graceful shutdown)");

        if (_watcherProcess?.HasExited == false)
        {
            try
            {
                // Try graceful shutdown first
                await _client!.PostAsync("/api/stop", null).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Graceful shutdown failed");
            }

            // Wait a bit for graceful shutdown
            if (!_watcherProcess.WaitForExit(5000))
            {
                _logger.LogWarning("Graceful shutdown timeout, will force kill in Dispose");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            // Force kill if still running
            if (_watcherProcess?.HasExited == false)
            {
                _logger.LogWarning("Force killing Watcher subprocess (PID: {0})", _watcherProcess.Id);
                _watcherProcess.Kill(entireProcessTree: true);
                _watcherProcess.WaitForExit();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during force cleanup");
        }
        finally
        {
            _watcherProcess?.Dispose();
            _client?.Dispose();
            _disposed = true;
            _logger.LogInformation("Fixture {0} disposed", GetType().Name);
        }
    }

    /// <summary>
    /// Starts the watcher subprocess. Implementation-specific.
    /// </summary>
    /// <returns>The started process.</returns>
    protected abstract Task<Process> StartWatcherSubprocessAsync();

    /// <summary>
    /// Finds the solution root by looking for the project.root marker file.
    /// </summary>
    public static string FindSolutionRoot()
    {
        return TestHelpers.SolutionRoot;
    }

    /// <summary>
    /// Gets the path to the watcher executable or DLL.
    /// </summary>
    protected static (string fileName, string arguments, string solutionRoot) GetWatcherExecutableInfo(int port, bool useDaemonFlag = false)
    {
        var solutionRoot = TestHelpers.SolutionRoot;
        var binDir = Path.Combine(solutionRoot, "artifacts", "bin", "opentelwatcher", "Debug");

        // Determine the executable name based on the platform
        string executableName;
        bool isWindows = OperatingSystem.IsWindows();

        if (isWindows)
        {
            executableName = "opentelwatcher.exe";
        }
        else
        {
            executableName = "opentelwatcher"; // Unix (Linux/macOS)
        }

        var executablePath = Path.Combine(binDir, executableName);

        // Build command arguments
        var daemonFlag = useDaemonFlag ? "--daemon " : "";

        // Fallback to DLL if native executable not found (e.g., framework-dependent builds)
        string fileName;
        string arguments;

        if (File.Exists(executablePath))
        {
            // Use native executable directly
            fileName = executablePath;
            arguments = $"start {daemonFlag}--port {port}";

            // Ensure executable permission on Unix systems
            if (!isWindows)
            {
                try
                {
                    var chmodProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x \"{executablePath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    chmodProcess?.WaitForExit();
                }
                catch
                {
                    // Ignore chmod errors - file might already be executable
                }
            }
        }
        else
        {
            // Fall back to using dotnet with DLL (cross-platform)
            var dllPath = Path.Combine(binDir, "opentelwatcher.dll");
            if (!File.Exists(dllPath))
            {
                throw new FileNotFoundException(
                    $"Watcher executable not found at {executablePath} or {dllPath}. " +
                    "Please run 'dotnet build' before running E2E tests.");
            }
            fileName = "dotnet";
            arguments = $"\"{dllPath}\" start {daemonFlag}--port {port}";
        }

        return (fileName, arguments, solutionRoot);
    }

    /// <summary>
    /// Sets up output redirection for a process.
    /// </summary>
    protected static void SetupProcessOutputCapture(Process process, ILogger<OpenTelWatcherServerFixtureBase> logger)
    {
        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                logger.LogInformation("[WATCHER OUTPUT] {Output}", e.Data);
            }
        };
        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                logger.LogWarning("[WATCHER ERROR] {Error}", e.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    /// <summary>
    /// Waits for the watcher to be ready by polling the /api/status endpoint.
    /// </summary>
    private async Task WaitForWatcherReadyAsync()
    {
        var maxAttempts = 30; // 30 seconds total
        var delay = TimeSpan.FromSeconds(1);

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var response = await _client!.GetAsync("/api/status", TestContext.Current.CancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Watcher ready after {0} attempts", attempt + 1);
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Expected while service is starting up
            }

            if (_watcherProcess?.HasExited == true)
            {
                throw new InvalidOperationException(
                    $"Watcher subprocess exited prematurely with code {_watcherProcess.ExitCode}");
            }

            await Task.Delay(delay, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("Watcher failed to start within 30 seconds");
    }
}

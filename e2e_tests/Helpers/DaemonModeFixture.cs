using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelWatcher.Configuration;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// xUnit collection fixture that manages a Watcher subprocess for black-box E2E tests
/// by using the built-in daemon mode (--daemon flag).
///
/// This fixture uses the watcher's daemon mode to start a background process.
/// The parent process exits quickly after spawning the daemon child process.
///
/// Cross-platform support:
/// - Windows: Uses watcher.exe directly
/// - Linux/macOS: Uses watcher executable (with chmod +x)
/// - Fallback: Uses 'dotnet watcher.dll' if native executable not found
///
/// Lifecycle:
/// 1. Constructor - synchronous initialization
/// 2. InitializeAsync - starts daemon subprocess and waits for health check
/// 3. [All tests in collection execute]
/// 4. DisposeAsync - graceful shutdown via /api/stop
/// 5. Dispose - force kill via PID file if still running
/// </summary>
public class DaemonModeFixture : OpenTelWatcherServerFixtureBase
{
    private int? _daemonPid;

    /// <summary>
    /// Starts the watcher subprocess using daemon mode (--daemon flag).
    /// The parent process exits after spawning the daemon, so we track the daemon PID.
    /// </summary>
    protected override async Task<Process> StartWatcherSubprocessAsync()
    {
        var (fileName, arguments, solutionRoot) = GetWatcherExecutableInfo(_port, useDaemonFlag: true);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = solutionRoot
        };

        var parentProcess = Process.Start(startInfo);
        if (parentProcess == null)
        {
            throw new InvalidOperationException("Failed to start watcher daemon parent process");
        }

        // Capture output from the parent process (daemon spawner)
        SetupProcessOutputCapture(parentProcess, _logger);

        _logger.LogInformation("Started Watcher daemon parent process (PID: {0})", parentProcess.Id);

        // Wait for parent process to exit (it spawns daemon and exits)
        await Task.Run(() => parentProcess.WaitForExit(15000));

        if (!parentProcess.HasExited)
        {
            parentProcess.Kill();
            throw new InvalidOperationException("Daemon parent process did not exit within timeout");
        }

        _logger.LogInformation("Daemon parent process exited with code {0}", parentProcess.ExitCode);

        if (parentProcess.ExitCode != 0)
        {
            throw new InvalidOperationException($"Daemon parent process failed with exit code {parentProcess.ExitCode}");
        }

        // Get the daemon PID by querying the /api/status endpoint on our specific port
        // This is more reliable than reading the PID file because:
        // 1. Each test fixture uses a unique port
        // 2. Only our daemon will be listening on that port
        // 3. The API returns the exact process ID
        // We wait for the daemon to be ready (already done in base class WaitForWatcherReadyAsync)
        // but we do it here explicitly to get the PID

        int daemonPid = 0;
        var maxAttempts = 30;
        var delay = TimeSpan.FromSeconds(1);

        using var httpClient = new HttpClient { BaseAddress = new Uri($"http://{ApiConstants.Network.LocalhostIp}:{_port}") };

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var response = await httpClient.GetAsync("/api/status");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var statusDoc = System.Text.Json.JsonDocument.Parse(json);

                    if (statusDoc.RootElement.TryGetProperty("processId", out var pidElement))
                    {
                        daemonPid = pidElement.GetInt32();
                        _logger.LogInformation("Retrieved daemon PID {0} from /api/status endpoint", daemonPid);
                        break;
                    }
                }
            }
            catch (HttpRequestException)
            {
                // Expected while daemon is starting up
            }

            await Task.Delay(delay);
        }

        if (daemonPid == 0)
        {
            throw new InvalidOperationException("Failed to retrieve daemon PID from /api/status endpoint");
        }

        _daemonPid = daemonPid;

        // Get a reference to the daemon process for monitoring
        Process? daemonProcess = null;
        try
        {
            daemonProcess = Process.GetProcessById(daemonPid);
            _logger.LogInformation("Daemon child process running (PID: {0})", daemonPid);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException($"Daemon process (PID: {daemonPid}) is not running");
        }

        // Return the daemon process (not the parent)
        // Note: This process object is just for monitoring - the daemon is detached
        return daemonProcess;
    }

    public new void Dispose()
    {
        try
        {
            // If we have a daemon PID, try to kill it
            if (_daemonPid.HasValue)
            {
                try
                {
                    var daemonProcess = Process.GetProcessById(_daemonPid.Value);
                    if (!daemonProcess.HasExited)
                    {
                        _logger.LogWarning("Force killing daemon process (PID: {0})", _daemonPid.Value);
                        daemonProcess.Kill(entireProcessTree: true);
                        daemonProcess.WaitForExit();
                    }
                }
                catch (ArgumentException)
                {
                    // Process already exited
                    _logger.LogInformation("Daemon process (PID: {0}) already exited", _daemonPid.Value);
                }
            }
        }
        finally
        {
            // Call base dispose
            base.Dispose();
        }
    }

}

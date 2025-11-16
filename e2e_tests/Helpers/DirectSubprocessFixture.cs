using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// xUnit collection fixture that manages a Watcher subprocess for black-box E2E tests
/// by starting the process directly (not in daemon mode).
///
/// This fixture starts the watcher process and keeps it attached to the test process,
/// allowing direct control over the subprocess lifecycle.
///
/// Cross-platform support:
/// - Windows: Uses watcher.exe directly
/// - Linux/macOS: Uses watcher executable (with chmod +x)
/// - Fallback: Uses 'dotnet watcher.dll' if native executable not found
///
/// Lifecycle:
/// 1. Constructor - synchronous initialization
/// 2. InitializeAsync - starts subprocess directly and waits for health check
/// 3. [All tests in collection execute]
/// 4. DisposeAsync - graceful shutdown via /api/stop
/// 5. Dispose - force kill if still running
/// </summary>
public class DirectSubprocessFixture : OpenTelWatcherServerFixtureBase
{
    /// <summary>
    /// Starts the watcher subprocess directly (without daemon mode).
    /// </summary>
    protected override Task<Process> StartWatcherSubprocessAsync()
    {
        var (fileName, arguments, solutionRoot) = GetWatcherExecutableInfo(_port, useDaemonFlag: false);

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

        var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start watcher subprocess");
        }

        // Capture output for debugging if test fails
        SetupProcessOutputCapture(process, _logger);

        _logger.LogInformation("Started Watcher subprocess directly (PID: {0})", process.Id);

        return Task.FromResult(process);
    }
}

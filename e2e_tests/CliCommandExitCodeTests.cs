using System.Diagnostics;
using Microsoft.Extensions.Logging;
using FluentAssertions;
using Xunit;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// E2E tests for CLI command exit codes.
/// Verifies that all CLI commands return correct exit codes for both success and failure scenarios.
///
/// Exit code conventions:
/// - 0: Success
/// - 1: User error (e.g., invalid arguments, instance already running)
/// - 2: System error (e.g., failed to start server, incompatible version)
/// </summary>
public class CliCommandExitCodeTests : IDisposable
{
    private readonly string _executablePath;
    private readonly string _solutionRoot;
    private readonly List<int> _portsToRelease = new();
    private readonly ILogger<CliCommandExitCodeTests> _logger;

    public CliCommandExitCodeTests()
    {
        _solutionRoot = TestHelpers.SolutionRoot;
        _executablePath = TestHelpers.GetWatcherExecutablePath(_solutionRoot);
        _logger = TestLoggerFactory.CreateLogger<CliCommandExitCodeTests>();
    }

    #region Stop Command Tests

    [Fact]
    public async Task StopCommand_WhenNoInstanceRunning_ReturnsExitCode1()
    {
        // Arrange - ensure no instance is running
        _logger.LogInformation("Ensuring no instance running on port 4318");
        await TestHelpers.EnsureNoInstanceRunningAsync(4318);

        // Act
        _logger.LogInformation("Running 'stop' command, expecting exit code 1");
        var exitCode = await TestHelpers.RunCliCommandAsync(_executablePath, "stop", _solutionRoot);

        // Assert
        _logger.LogInformation("Stop command returned exit code {ExitCode}", exitCode);
        exitCode.Should().Be(1, "stop command should return exit code 1 when no instance is running");
    }

    [Fact]
    public async Task StopCommand_WhenInstanceRunning_ReturnsExitCode0()
    {
        // Arrange - start an instance on default port
        using var serverProcess = await TestHelpers.StartServerAsync(_executablePath, 4318, _solutionRoot);
        await TestHelpers.WaitForServerHealthyAsync(4318);

        // Act
        var exitCode = await TestHelpers.RunCliCommandAsync(_executablePath, "stop", _solutionRoot);

        // Assert
        exitCode.Should().Be(0, "stop command should return exit code 0 when successfully stopping instance");

        // Cleanup - wait for server to stop
        serverProcess.WaitForExit(5000);
    }

    [Fact]
    public async Task StopCommand_WithSilentFlag_WhenInstanceRunning_ReturnsExitCode0()
    {
        // Arrange - start an instance on default port
        using var serverProcess = await TestHelpers.StartServerAsync(_executablePath, 4318, _solutionRoot);
        await TestHelpers.WaitForServerHealthyAsync(4318);

        // Act
        var exitCode = await TestHelpers.RunCliCommandAsync(_executablePath, "stop --silent", _solutionRoot);

        // Assert
        exitCode.Should().Be(0, "silent mode should not affect exit code");

        // Cleanup
        serverProcess.WaitForExit(5000);
    }

    #endregion

    #region Start Command Tests

    [Fact]
    public async Task StartCommand_WhenInstanceAlreadyRunning_ReturnsExitCode1()
    {
        // Arrange - start an instance
        var port = TestHelpers.GetRandomPort();
        _portsToRelease.Add(port);
        using var serverProcess = await TestHelpers.StartServerAsync(_executablePath, port, _solutionRoot);
        await TestHelpers.WaitForServerHealthyAsync(port);

        try
        {
            // Act - try to start another instance on same port
            var exitCode = await TestHelpers.RunCliCommandAsync(_executablePath, $"start --port {port}", _solutionRoot, timeoutSeconds: 5);

            // Assert
            exitCode.Should().Be(1, "start command should return exit code 1 when instance already running");
        }
        finally
        {
            await TestHelpers.StopServerAsync(port, serverProcess);
        }
    }

    [Fact]
    public async Task StartCommand_WithInvalidPort_ReturnsExitCode1()
    {
        // Act
        var exitCode = await TestHelpers.RunCliCommandAsync(_executablePath, "start --port 99999", _solutionRoot, timeoutSeconds: 5);

        // Assert
        exitCode.Should().Be(1, "start command should return exit code 1 for invalid port");
    }

    [Fact]
    public async Task StartCommand_WithInvalidOutputDirectory_ReturnsExitCode1()
    {
        // Arrange - create a path with non-existent parent
        var invalidPath = Path.Combine(_solutionRoot, "nonexistent_parent", "output");

        // Act
        var exitCode = await TestHelpers.RunCliCommandAsync(_executablePath, $"start --port {TestHelpers.GetRandomPort()} --output-dir \"{invalidPath}\"", _solutionRoot, timeoutSeconds: 5);

        // Assert
        exitCode.Should().Be(1, "start command should return exit code 1 when parent directory does not exist");
    }

    [Fact]
    public async Task StartCommand_DaemonMode_WhenSuccessful_ReturnsExitCode0()
    {
        // Arrange
        var port = TestHelpers.GetRandomPort();
        _portsToRelease.Add(port);
        _logger.LogInformation("Testing daemon mode on port {Port}", port);

        var outputDir = Path.Combine(_solutionRoot, "artifacts", "test-telemetry", "daemon");

        try
        {
            // Act - start in daemon mode
            _logger.LogInformation("Starting daemon mode with output dir {OutputDir}", outputDir);
            var exitCode = await TestHelpers.RunCliCommandAsync(_executablePath, $"start --daemon --port {port} --output-dir \"{outputDir}\"", _solutionRoot, timeoutSeconds: 15);

            // Assert
            _logger.LogInformation("Daemon start returned exit code {ExitCode}", exitCode);
            exitCode.Should().Be(0, "daemon mode start should return exit code 0 on success");

            // Verify server is running
            _logger.LogInformation("Waiting for server to become healthy");
            await TestHelpers.WaitForServerHealthyAsync(port);
            _logger.LogInformation("Server is healthy");
        }
        finally
        {
            // Cleanup - stop the daemon
            _logger.LogInformation("Stopping daemon on port {Port}", port);
            await TestHelpers.StopServerOnPortAsync(port);
            TestHelpers.CleanupOutputDirectory(outputDir);
        }
    }

    public void Dispose()
    {
        // Release all allocated ports back to the pool
        foreach (var port in _portsToRelease)
        {
            PortAllocator.Release(port);
        }
    }

    #endregion
}

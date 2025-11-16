using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using OpenTelWatcher.E2ETests.Helpers;
using OpenTelWatcher.Tests.E2E;
using Xunit;

namespace OpenTelWatcher.E2ETests;

/// <summary>
/// E2E tests for the 'status' CLI command.
/// Tests the command's ability to provide quick health status summary.
/// </summary>
public class StatusCommandTests : IAsyncLifetime
{
    private Process? _serverProcess;
    private int _currentTestPort;

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        // Stop server if running
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            await TestHelpers.StopServerAsync(_currentTestPort, _serverProcess);
        }
    }

    private int GetRandomPort()
    {
        return Random.Shared.Next(6000, 7000);
    }

    private async Task<Process> StartServerForTestAsync(string testDir)
    {
        _currentTestPort = GetRandomPort();
        await TestHelpers.EnsureNoInstanceRunningAsync(_currentTestPort);

        var process = await TestHelpers.StartServerWithOutputDirAsync(
            TestHelpers.GetWatcherExecutablePath(TestHelpers.SolutionRoot),
            _currentTestPort,
            TestHelpers.SolutionRoot,
            testDir);

        await TestHelpers.WaitForServerHealthyAsync(_currentTestPort);
        return process;
    }

    private async Task StopServerAfterTestAsync()
    {
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            await TestHelpers.StopServerAsync(_currentTestPort, _serverProcess);
            _serverProcess = null;
        }
    }

    [Fact]
    public async Task Status_ReturnsExitCode2_WhenNoInstanceRunning()
    {
        // Arrange - ensure no instance running on a random port
        _currentTestPort = GetRandomPort();
        await TestHelpers.EnsureNoInstanceRunningAsync(_currentTestPort);

        // Act
        var result = await RunStatusCommand();

        // Assert
        result.ExitCode.Should().Be(2, "instance not running should return exit code 2");
        result.Output.Should().Contain("No OpenTelWatcher instance is currently running", "should indicate instance is not running");
    }

    [Fact]
    public async Task Status_ReturnsHealthy_WhenNoErrorFiles()
    {
        try
        {
            // Arrange - start server with test directory
            var testDir = TestHelpers.GetTestOutputDir("status-command/healthy");
            await File.WriteAllTextAsync(
                Path.Combine(testDir, "traces.20251116_143022_456.ndjson"),
                "{\"resourceSpans\":[]}\n",
                CancellationToken.None);

            _serverProcess = await StartServerForTestAsync(testDir);

            // Act
            var result = await RunStatusCommand();

            // Assert
            result.ExitCode.Should().Be(0, "healthy status should return exit code 0");
            result.Output.Should().ContainAny("Healthy", "✓", "healthy", "should indicate healthy status");
            result.Output.Should().Contain("No errors", "should indicate no errors found");
        }
        finally
        {
            await StopServerAfterTestAsync();
        }
    }

    [Fact]
    public async Task Status_ReturnsUnhealthy_WhenErrorFilesPresent()
    {
        try
        {
            // Arrange - start server and create error files
            var testDir = TestHelpers.GetTestOutputDir("status-command/unhealthy");
            await File.WriteAllTextAsync(
                Path.Combine(testDir, "traces.20251116_143022_456.ndjson"),
                "{\"resourceSpans\":[]}\n",
                CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(testDir, "traces.20251116_143022_456.errors.ndjson"),
                "{\"resourceSpans\":[]}\n",
                CancellationToken.None);

            _serverProcess = await StartServerForTestAsync(testDir);

            // Act
            var result = await RunStatusCommand();

            // Assert
            result.ExitCode.Should().Be(1, "unhealthy status should return exit code 1");
            result.Output.Should().ContainAny("Unhealthy", "✗", "ERROR", "should indicate unhealthy status");
            result.Output.Should().MatchRegex(@"\d+\s+(ERROR|error)", "should show error file count");
        }
        finally
        {
            await StopServerAfterTestAsync();
        }
    }

    [Fact]
    public async Task Status_ShowsFileCount_WhenHealthy()
    {
        try
        {
            // Arrange - start server with multiple files
            var testDir = TestHelpers.GetTestOutputDir("status-command/file-count");
            await File.WriteAllTextAsync(
                Path.Combine(testDir, "traces.20251116_143022_456.ndjson"),
                "{\"resourceSpans\":[]}\n",
                CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(testDir, "logs.20251116_143022_456.ndjson"),
                "{\"resourceLogs\":[]}\n",
                CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(testDir, "metrics.20251116_143022_456.ndjson"),
                "{\"resourceMetrics\":[]}\n",
                CancellationToken.None);

            _serverProcess = await StartServerForTestAsync(testDir);

            // Act
            var result = await RunStatusCommand();

            // Assert
            result.ExitCode.Should().Be(0, "should be healthy");
            result.Output.Should().MatchRegex(@"\d+\s+file", "should show file count");
            result.Output.Should().MatchRegex(@"\d+(\.\d+)?\s*(B|KB|MB)", "should show total size");
        }
        finally
        {
            await StopServerAfterTestAsync();
        }
    }

    [Fact]
    public async Task Status_WithJsonFlag_ReturnsStructuredHealthData()
    {
        try
        {
            // Arrange - start server with error file
            var testDir = TestHelpers.GetTestOutputDir("status-command/json-unhealthy");
            await File.WriteAllTextAsync(
                Path.Combine(testDir, "traces.20251116_143022_456.errors.ndjson"),
                "{\"resourceSpans\":[]}\n",
                CancellationToken.None);

            _serverProcess = await StartServerForTestAsync(testDir);

            // Act
            var result = await RunStatusCommand("--json");

            // Assert
            result.ExitCode.Should().Be(1, "error files should make status unhealthy");

            // Parse JSON output
            var jsonOutput = JsonDocument.Parse(result.Output);
            jsonOutput.RootElement.TryGetProperty("healthy", out var healthy)
                .Should().BeTrue("JSON should contain healthy property");
            healthy.GetBoolean().Should().BeFalse("should indicate unhealthy");

            jsonOutput.RootElement.TryGetProperty("fileCount", out var fileCount)
                .Should().BeTrue("JSON should contain fileCount property");
            fileCount.GetInt32().Should().BeGreaterThan(0, "should report file count");

            jsonOutput.RootElement.TryGetProperty("errorFileCount", out var errorFileCount)
                .Should().BeTrue("JSON should contain errorFileCount property");
            errorFileCount.GetInt32().Should().BeGreaterThan(0, "should report error file count");
        }
        finally
        {
            await StopServerAfterTestAsync();
        }
    }

    [Fact]
    public async Task Status_WithJsonFlag_WhenHealthy_ReturnsStructuredData()
    {
        try
        {
            // Arrange - start server with no errors
            var testDir = TestHelpers.GetTestOutputDir("status-command/json-healthy");
            await File.WriteAllTextAsync(
                Path.Combine(testDir, "traces.20251116_143022_456.ndjson"),
                "{\"resourceSpans\":[]}\n",
                CancellationToken.None);

            _serverProcess = await StartServerForTestAsync(testDir);

            // Act
            var result = await RunStatusCommand("--json");

            // Assert
            result.ExitCode.Should().Be(0, "should be healthy");

            // Parse JSON output
            var jsonOutput = JsonDocument.Parse(result.Output);
            jsonOutput.RootElement.GetProperty("healthy").GetBoolean()
                .Should().BeTrue("should indicate healthy");
            jsonOutput.RootElement.GetProperty("errorFileCount").GetInt32()
                .Should().Be(0, "should report zero error files");
        }
        finally
        {
            await StopServerAfterTestAsync();
        }
    }

    [Fact]
    public async Task Status_WithJsonFlag_WhenNotRunning_ReturnsStructuredError()
    {
        // Arrange - ensure no instance running on random port
        _currentTestPort = GetRandomPort();
        await TestHelpers.EnsureNoInstanceRunningAsync(_currentTestPort);

        // Act
        var result = await RunStatusCommand("--json");

        // Assert
        result.ExitCode.Should().Be(2, "instance not running should return exit code 2");

        // Parse JSON output
        var jsonOutput = JsonDocument.Parse(result.Output);
        jsonOutput.RootElement.TryGetProperty("success", out var success)
            .Should().BeTrue("JSON should contain success property");
        success.GetBoolean().Should().BeFalse("success should be false");

        jsonOutput.RootElement.TryGetProperty("error", out var error)
            .Should().BeTrue("JSON should contain error property");
    }

    [Fact]
    public async Task Status_ShowsOneLinerSummary()
    {
        try
        {
            // Arrange - start server
            var testDir = TestHelpers.GetTestOutputDir("status-command/one-liner");
            await File.WriteAllTextAsync(
                Path.Combine(testDir, "traces.20251116_143022_456.ndjson"),
                "{\"resourceSpans\":[]}\n",
                CancellationToken.None);

            _serverProcess = await StartServerForTestAsync(testDir);

            // Act
            var result = await RunStatusCommand();

            // Assert
            result.ExitCode.Should().Be(0, "should be healthy");

            // Should be a one-liner (not verbose like info command)
            var lines = result.Output.Trim().Split('\n');
            lines.Length.Should().BeLessThanOrEqualTo(2, "status should be concise, not verbose");
        }
        finally
        {
            await StopServerAfterTestAsync();
        }
    }

    // Helper Methods

    private async Task<CommandResult> RunStatusCommand(params string[] additionalArgs)
    {
        var args = new List<string> { "status", "--port", _currentTestPort.ToString() };
        args.AddRange(additionalArgs);
        var commandArgs = string.Join(" ", args);
        return await TestHelpers.RunCliCommandWithOutputAsync(commandArgs);
    }
}

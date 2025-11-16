using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using OpenTelWatcher.E2ETests.Helpers;
using OpenTelWatcher.Tests.E2E;
using Xunit;

namespace OpenTelWatcher.E2ETests;

/// <summary>
/// E2E tests for the 'stats' CLI command.
/// Tests the command's ability to display telemetry statistics.
/// </summary>
public class StatsCommandTests : IAsyncLifetime
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
    public async Task Stats_ReturnsExitCode2_WhenNoInstanceRunning()
    {
        // Arrange - ensure no instance running on a random port
        _currentTestPort = GetRandomPort();
        await TestHelpers.EnsureNoInstanceRunningAsync(_currentTestPort);

        // Act
        var result = await RunStatsCommand();

        // Assert
        result.ExitCode.Should().Be(2, "instance not running should return exit code 2");
        result.Output.Should().Contain("No OpenTelWatcher instance is currently running", "should indicate instance is not running");
    }

    [Fact]
    public async Task Stats_ShowsTelemetryStatistics_WhenServerRunning()
    {
        try
        {
            // Arrange - start server
            var testDir = TestHelpers.GetTestOutputDir("stats-command/basic");
            _serverProcess = await StartServerForTestAsync(testDir);

            // Act
            var result = await RunStatsCommand();

            // Assert
            result.ExitCode.Should().Be(0, "should return exit code 0 when running");
            result.Output.Should().Contain("Telemetry Statistics", "should show statistics header");
            result.Output.Should().MatchRegex(@"Traces received:\s+\d+", "should show traces count");
            result.Output.Should().MatchRegex(@"Logs received:\s+\d+", "should show logs count");
            result.Output.Should().MatchRegex(@"Metrics received:\s+\d+", "should show metrics count");
        }
        finally
        {
            await StopServerAfterTestAsync();
        }
    }

    [Fact]
    public async Task Stats_ShowsFileStatistics_WhenFilesExist()
    {
        try
        {
            // Arrange - start server with test files
            var testDir = TestHelpers.GetTestOutputDir("stats-command/files");
            await File.WriteAllTextAsync(
                Path.Combine(testDir, "traces.20251116_143022_456.ndjson"),
                "{\"resourceSpans\":[]}\n",
                CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(testDir, "logs.20251116_143022_456.ndjson"),
                "{\"resourceLogs\":[]}\n",
                CancellationToken.None);

            _serverProcess = await StartServerForTestAsync(testDir);

            // Act
            var result = await RunStatsCommand();

            // Assert
            result.ExitCode.Should().Be(0, "should succeed");
            result.Output.Should().MatchRegex(@"Files:\s+\d+", "should show file count");
            result.Output.Should().MatchRegex(@"traces:\s+\d+\s+file", "should show traces file count");
            result.Output.Should().MatchRegex(@"logs:\s+\d+\s+file", "should show logs file count");
        }
        finally
        {
            await StopServerAfterTestAsync();
        }
    }

    [Fact]
    public async Task Stats_ShowsUptime_WhenServerRunning()
    {
        try
        {
            // Arrange - start server
            var testDir = TestHelpers.GetTestOutputDir("stats-command/uptime");
            _serverProcess = await StartServerForTestAsync(testDir);

            // Act
            var result = await RunStatsCommand();

            // Assert
            result.ExitCode.Should().Be(0, "should succeed");
            result.Output.Should().MatchRegex(@"Uptime:\s+(\d+[dhms]\s*)+", "should show uptime");
        }
        finally
        {
            await StopServerAfterTestAsync();
        }
    }

    [Fact]
    public async Task Stats_WithJsonFlag_ReturnsStructuredData()
    {
        try
        {
            // Arrange - start server
            var testDir = TestHelpers.GetTestOutputDir("stats-command/json");
            _serverProcess = await StartServerForTestAsync(testDir);

            // Act
            var result = await RunStatsCommand("--json");

            // Assert
            result.ExitCode.Should().Be(0, "should succeed");

            // Parse JSON output
            var jsonOutput = JsonDocument.Parse(result.Output);
            jsonOutput.RootElement.TryGetProperty("telemetry", out var telemetry)
                .Should().BeTrue("JSON should contain telemetry property");

            telemetry.TryGetProperty("traces", out var traces)
                .Should().BeTrue("telemetry should contain traces");
            traces.TryGetProperty("requests", out var tracesRequests)
                .Should().BeTrue("traces should contain requests count");

            telemetry.TryGetProperty("logs", out var logs)
                .Should().BeTrue("telemetry should contain logs");
            logs.TryGetProperty("requests", out var logsRequests)
                .Should().BeTrue("logs should contain requests count");

            telemetry.TryGetProperty("metrics", out var metrics)
                .Should().BeTrue("telemetry should contain metrics");
            metrics.TryGetProperty("requests", out var metricsRequests)
                .Should().BeTrue("metrics should contain requests count");

            jsonOutput.RootElement.TryGetProperty("files", out var files)
                .Should().BeTrue("JSON should contain files property");

            jsonOutput.RootElement.TryGetProperty("uptimeSeconds", out var uptimeSeconds)
                .Should().BeTrue("JSON should contain uptimeSeconds property");
        }
        finally
        {
            await StopServerAfterTestAsync();
        }
    }

    [Fact]
    public async Task Stats_WithJsonFlag_WhenNotRunning_ReturnsStructuredError()
    {
        // Arrange - ensure no instance running on random port
        _currentTestPort = GetRandomPort();
        await TestHelpers.EnsureNoInstanceRunningAsync(_currentTestPort);

        // Act
        var result = await RunStatsCommand("--json");

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
    public async Task Stats_ShowsFileBreakdown_ByTelemetryType()
    {
        try
        {
            // Arrange - start server with multiple file types
            var testDir = TestHelpers.GetTestOutputDir("stats-command/breakdown");
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
            var result = await RunStatsCommand();

            // Assert
            result.ExitCode.Should().Be(0, "should succeed");
            result.Output.Should().Contain("traces:", "should show traces breakdown");
            result.Output.Should().Contain("logs:", "should show logs breakdown");
            result.Output.Should().Contain("metrics:", "should show metrics breakdown");
        }
        finally
        {
            await StopServerAfterTestAsync();
        }
    }

    [Fact]
    public async Task Stats_ShowsZeroCounts_WhenNoTelemetryReceived()
    {
        try
        {
            // Arrange - start server (no telemetry sent)
            var testDir = TestHelpers.GetTestOutputDir("stats-command/zero");
            _serverProcess = await StartServerForTestAsync(testDir);

            // Act
            var result = await RunStatsCommand();

            // Assert
            result.ExitCode.Should().Be(0, "should succeed even with zero telemetry");
            result.Output.Should().Contain("Telemetry Statistics", "should show statistics section");
            result.Output.Should().MatchRegex(@"Traces received:\s+0", "should show zero traces");
            result.Output.Should().MatchRegex(@"Logs received:\s+0", "should show zero logs");
            result.Output.Should().MatchRegex(@"Metrics received:\s+0", "should show zero metrics");
        }
        finally
        {
            await StopServerAfterTestAsync();
        }
    }

    // Helper Methods

    private async Task<CommandResult> RunStatsCommand(params string[] additionalArgs)
    {
        var args = new List<string> { "stats", "--port", _currentTestPort.ToString() };
        args.AddRange(additionalArgs);
        var commandArgs = string.Join(" ", args);
        return await TestHelpers.RunCliCommandWithOutputAsync(commandArgs);
    }
}

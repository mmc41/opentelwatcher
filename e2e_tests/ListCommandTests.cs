using System.Text.Json;
using Microsoft.Extensions.Logging;
using FluentAssertions;
using Xunit;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// E2E tests for the 'list' CLI command.
/// Tests the command's ability to list telemetry files with various filters and formats.
/// </summary>
public class ListCommandTests : IAsyncLifetime
{
    private const int TestPort = TestHelpers.DefaultTestPort;
    private readonly ILogger<ListCommandTests> _logger;

    public ListCommandTests()
    {
        _logger = TestLoggerFactory.CreateLogger<ListCommandTests>();
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task List_ShowsAllFiles_WhenDirectoryHasFiles()
    {
        // Arrange - create test files
        var testDir = TestHelpers.GetTestOutputDir("list-command/has-files");
        _logger.LogInformation("Creating test files in {TestDir}", testDir);
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.ndjson"),
            "{\"resourceSpans\":[]}\n",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "logs.20251116_143022_456.ndjson"),
            "{\"resourceLogs\":[]}\n",
            TestContext.Current.CancellationToken);

        // Act
        _logger.LogInformation("Running list command");
        var result = await RunListCommand(testDir);

        // Assert
        _logger.LogInformation("List command returned exit code {ExitCode}", result.ExitCode);
        _logger.LogDebug("Output: {Output}", result.Output);
        result.ExitCode.Should().Be(0, "command should succeed");
        result.Output.Should().Contain("traces.20251116_143022_456.ndjson", "should list trace file");
        result.Output.Should().Contain("logs.20251116_143022_456.ndjson", "should list log file");
    }

    [Fact]
    public async Task List_ShowsNoFiles_WhenDirectoryIsEmpty()
    {
        // Arrange - empty directory
        var testDir = TestHelpers.GetTestOutputDir("list-command/empty");

        // Act
        var result = await RunListCommand(testDir);

        // Assert
        result.ExitCode.Should().Be(0, "command should succeed even with no files");
        result.Output.Should().ContainAny("No files", "0 files", "empty",
            "should indicate no files found");
    }

    [Fact]
    public async Task List_WithErrorsOnlyFlag_ShowsOnlyErrorFiles()
    {
        // Arrange - mix of normal and error files
        var testDir = TestHelpers.GetTestOutputDir("list-command/errors-only");
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.ndjson"),
            "{\"resourceSpans\":[]}\n",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.errors.ndjson"),
            "{\"resourceSpans\":[]}\n",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "logs.20251116_143022_456.errors.ndjson"),
            "{\"resourceLogs\":[]}\n",
            TestContext.Current.CancellationToken);

        // Act
        var result = await RunListCommand(testDir, "--errors-only");

        // Assert
        result.ExitCode.Should().Be(0, "command should succeed");
        result.Output.Should().Contain(".errors.ndjson", "should show error files");
        result.Output.Should().NotContain("traces.20251116_143022_456.ndjson",
            "should not show normal files when --errors-only is specified");
    }

    [Fact]
    public async Task List_WithJsonFlag_ReturnsStructuredFileList()
    {
        // Arrange - create test files
        var testDir = TestHelpers.GetTestOutputDir("list-command/json-output");
        var traceFile = "traces.20251116_143022_456.ndjson";
        await File.WriteAllTextAsync(
            Path.Combine(testDir, traceFile),
            "{\"resourceSpans\":[]}\n",
            TestContext.Current.CancellationToken);

        // Act
        var result = await RunListCommand(testDir, "--json");

        // Assert
        result.ExitCode.Should().Be(0, "command should succeed");

        // Parse JSON output
        var jsonOutput = JsonDocument.Parse(result.Output);
        jsonOutput.RootElement.TryGetProperty("success", out var success)
            .Should().BeTrue("JSON should contain success property");
        success.GetBoolean().Should().BeTrue("success should be true");

        jsonOutput.RootElement.TryGetProperty("files", out var files)
            .Should().BeTrue("JSON should contain files array");
        files.GetArrayLength().Should().BeGreaterThan(0, "should list files");

        var firstFile = files.EnumerateArray().First();
        firstFile.TryGetProperty("name", out var fileName).Should().BeTrue("file should have name");
        fileName.GetString().Should().Be(traceFile, "should match created file");
    }

    [Fact]
    public async Task List_WithJsonFlag_WhenEmpty_ReturnsEmptyArray()
    {
        // Arrange - empty directory
        var testDir = TestHelpers.GetTestOutputDir("list-command/json-empty");

        // Act
        var result = await RunListCommand(testDir, "--json");

        // Assert
        result.ExitCode.Should().Be(0, "command should succeed");

        var jsonOutput = JsonDocument.Parse(result.Output);
        jsonOutput.RootElement.GetProperty("success").GetBoolean()
            .Should().BeTrue("success should be true");
        jsonOutput.RootElement.GetProperty("files").GetArrayLength()
            .Should().Be(0, "should return empty file array");
    }

    [Fact]
    public async Task List_WithVerboseFlag_ShowsFileSizesAndDates()
    {
        // Arrange - create test file
        var testDir = TestHelpers.GetTestOutputDir("list-command/verbose");
        var testFile = Path.Combine(testDir, "traces.20251116_143022_456.ndjson");
        await File.WriteAllTextAsync(testFile, "{\"resourceSpans\":[]}\n", TestContext.Current.CancellationToken);

        // Act
        var result = await RunListCommand(testDir, "--verbose");

        // Assert
        result.ExitCode.Should().Be(0, "command should succeed");
        result.Output.Should().Contain("traces.20251116_143022_456.ndjson", "should list file");
        result.Output.Should().MatchRegex(@"\d+\s*(B|KB|MB)", "should show file size in verbose mode");
    }

    [Fact]
    public async Task List_WorksWithoutRunningInstance()
    {
        // This test verifies list command works in standalone mode
        // (doesn't require the watcher instance to be running)

        // Arrange - create test file
        var testDir = TestHelpers.GetTestOutputDir("list-command/standalone");
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.ndjson"),
            "{\"resourceSpans\":[]}\n",
            TestContext.Current.CancellationToken);

        // Act - run list without starting watcher instance
        var result = await RunListCommand(testDir);

        // Assert - should still work
        result.ExitCode.Should().Be(0, "list should work standalone");
        result.Output.Should().Contain("traces.20251116_143022_456.ndjson", "should list files");
    }

    [Fact]
    public async Task List_ShowsMultipleFiles_InSortedOrder()
    {
        // Arrange - create multiple files
        var testDir = TestHelpers.GetTestOutputDir("list-command/multiple");
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_100000_000.ndjson"),
            "{\"resourceSpans\":[]}\n",
            TestContext.Current.CancellationToken);
        await Task.Delay(E2EConstants.Delays.TimestampDifferentiationMs, TestContext.Current.CancellationToken); // Ensure different timestamps
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "logs.20251116_110000_000.ndjson"),
            "{\"resourceLogs\":[]}\n",
            TestContext.Current.CancellationToken);
        await Task.Delay(E2EConstants.Delays.TimestampDifferentiationMs, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "metrics.20251116_120000_000.ndjson"),
            "{\"resourceMetrics\":[]}\n",
            TestContext.Current.CancellationToken);

        // Act
        var result = await RunListCommand(testDir);

        // Assert
        result.ExitCode.Should().Be(0, "command should succeed");
        result.Output.Should().Contain("traces.20251116_100000_000.ndjson", "should list trace file");
        result.Output.Should().Contain("logs.20251116_110000_000.ndjson", "should list log file");
        result.Output.Should().Contain("metrics.20251116_120000_000.ndjson", "should list metrics file");
    }

    // Helper Methods

    private static async Task<CommandResult> RunListCommand(string testDir, params string[] additionalArgs)
    {
        var args = new List<string> { "list", "--output-dir", testDir };
        args.AddRange(additionalArgs);
        var commandArgs = string.Join(" ", args);
        return await TestHelpers.RunCliCommandWithOutputAsync(commandArgs);
    }
}

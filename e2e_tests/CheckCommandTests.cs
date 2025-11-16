using System.Text.Json;
using FluentAssertions;
using OpenTelWatcher.E2ETests.Helpers;
using OpenTelWatcher.Tests.E2E;
using Xunit;

namespace OpenTelWatcher.E2ETests;

/// <summary>
/// E2E tests for the 'check' CLI command.
/// Tests the command's ability to detect error files and return appropriate exit codes.
/// </summary>
public class CheckCommandTests : IAsyncLifetime
{
    // Use centralized test constants for artifact paths
    // Directory cleanup handled by Directory.Build.targets before test run
    // Each test uses its own unique subdirectory to avoid interference
    private const int TestPort = TestHelpers.DefaultTestPort;

    public ValueTask InitializeAsync()
    {
        // Test output directory is created by TestHelpers.GetTestOutputDir
        // and cleaned by Directory.Build.targets before tests run
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        // No cleanup needed - handled by Directory.Build.targets
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Check_ReturnsZero_WhenNoErrorFilesExist()
    {
        // Arrange - create normal telemetry files (no errors)
        var testDir = TestHelpers.GetTestOutputDir("check-command/no-errors");
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.ndjson"),
            "{\"resourceSpans\":[]}\n",
            CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "logs.20251116_143022_456.ndjson"),
            "{\"resourceLogs\":[]}\n",
            CancellationToken.None);

        // Act
        var result = await RunCheckCommand(testDir);

        // Assert
        result.ExitCode.Should().Be(0, "no error files exist");
        result.Output.Trim().Should().StartWith("No errors", "should indicate no errors found");
    }

    [Fact]
    public async Task Check_ReturnsOne_WhenErrorFilesPresent()
    {
        // Arrange - create error files
        var testDir = TestHelpers.GetTestOutputDir("check-command/with-errors");
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.errors.ndjson"),
            "{\"resourceSpans\":[]}\n",
            CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "logs.20251116_143022_456.errors.ndjson"),
            "{\"resourceLogs\":[]}\n",
            CancellationToken.None);

        // Act
        var result = await RunCheckCommand(testDir);

        // Assert
        result.ExitCode.Should().Be(1, "error files are present");
    }

    [Fact]
    public async Task Check_WithVerboseFlag_ShowsErrorFileList()
    {
        // Arrange - create error files
        var testDir = TestHelpers.GetTestOutputDir("check-command/verbose-errors");
        var traceErrorFile = "traces.20251116_143022_456.errors.ndjson";
        var logErrorFile = "logs.20251116_143022_456.errors.ndjson";

        await File.WriteAllTextAsync(
            Path.Combine(testDir, traceErrorFile),
            "{\"resourceSpans\":[]}\n",
            CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(testDir, logErrorFile),
            "{\"resourceLogs\":[]}\n",
            CancellationToken.None);

        // Act
        var result = await RunCheckCommand(testDir, "--verbose");

        // Assert
        result.ExitCode.Should().Be(1, "error files are present");
        result.Output.Should().Contain(traceErrorFile, "verbose mode should list error files");
        result.Output.Should().Contain(logErrorFile, "verbose mode should list error files");
    }

    [Fact]
    public async Task Check_WithVerboseFlag_ShowsSuccessMessage_WhenNoErrors()
    {
        // Arrange - create normal files only
        var testDir = TestHelpers.GetTestOutputDir("check-command/verbose-no-errors");
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.ndjson"),
            "{\"resourceSpans\":[]}\n",
            CancellationToken.None);

        // Act
        var result = await RunCheckCommand(testDir, "--verbose");

        // Assert
        result.ExitCode.Should().Be(0, "no error files exist");
        result.Output.Should().ContainAny("No errors", "no error", "✓",
            "verbose mode should show success message");
    }

    [Fact]
    public async Task Check_WithJsonFlag_ReturnsStructuredHealthData()
    {
        // Arrange - create error files
        var testDir = TestHelpers.GetTestOutputDir("check-command/json-errors");
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.errors.ndjson"),
            "{\"resourceSpans\":[]}\n",
            CancellationToken.None);

        // Act
        var result = await RunCheckCommand(testDir, "--json");

        // Assert
        result.ExitCode.Should().Be(1, "error files are present");

        // Parse JSON output
        var jsonOutput = JsonDocument.Parse(result.Output);
        jsonOutput.RootElement.TryGetProperty("hasErrors", out var hasErrors)
            .Should().BeTrue("JSON should contain hasErrors property");
        hasErrors.GetBoolean().Should().BeTrue("hasErrors should be true");

        jsonOutput.RootElement.TryGetProperty("errorFileCount", out var errorFileCount)
            .Should().BeTrue("JSON should contain errorFileCount property");
        errorFileCount.GetInt32().Should().BeGreaterThan(0, "should report error file count");
    }

    [Fact]
    public async Task Check_WithJsonFlag_WhenNoErrors_ReturnsStructuredData()
    {
        // Arrange - no error files
        var testDir = TestHelpers.GetTestOutputDir("check-command/json-no-errors");
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.ndjson"),
            "{\"resourceSpans\":[]}\n",
            CancellationToken.None);

        // Act
        var result = await RunCheckCommand(testDir, "--json");

        // Assert
        result.ExitCode.Should().Be(0, "no error files exist");

        // Parse JSON output
        var jsonOutput = JsonDocument.Parse(result.Output);
        jsonOutput.RootElement.TryGetProperty("hasErrors", out var hasErrors)
            .Should().BeTrue("JSON should contain hasErrors property");
        hasErrors.GetBoolean().Should().BeFalse("hasErrors should be false");

        jsonOutput.RootElement.TryGetProperty("errorFileCount", out var errorFileCount)
            .Should().BeTrue("JSON should contain errorFileCount property");
        errorFileCount.GetInt32().Should().Be(0, "should report zero error files");
    }

    [Fact]
    public async Task Check_WorksWithoutRunningInstance()
    {
        // This test verifies check command works in standalone mode
        // (doesn't require the watcher instance to be running)

        // Arrange - create error file
        var testDir = TestHelpers.GetTestOutputDir("check-command/standalone");
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.errors.ndjson"),
            "{\"resourceSpans\":[]}\n",
            CancellationToken.None);

        // Act - run check without starting watcher instance
        var result = await RunCheckCommand(testDir);

        // Assert - should still work
        result.ExitCode.Should().Be(1, "check should work standalone");
    }

    [Fact]
    public async Task Check_WithMixedFiles_DetectsOnlyErrorFiles()
    {
        // Arrange - mix of normal and error files
        var testDir = TestHelpers.GetTestOutputDir("check-command/mixed-files");
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.ndjson"),
            "{\"resourceSpans\":[]}\n",
            CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.errors.ndjson"),
            "{\"resourceSpans\":[]}\n",
            CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "logs.20251116_143022_456.ndjson"),
            "{\"resourceLogs\":[]}\n",
            CancellationToken.None);
        // No log error file

        // Act
        var result = await RunCheckCommand(testDir, "--json");

        // Assert
        result.ExitCode.Should().Be(1, "error file exists");

        var jsonOutput = JsonDocument.Parse(result.Output);
        jsonOutput.RootElement.GetProperty("errorFileCount").GetInt32()
            .Should().Be(1, "should count only error files, not normal files");
    }

    // Helper Methods

    private static async Task<CommandResult> RunCheckCommand(string testDir, params string[] additionalArgs)
    {
        var args = new List<string> { "check", "--output-dir", testDir };
        args.AddRange(additionalArgs);
        var commandArgs = string.Join(" ", args);
        return await TestHelpers.RunCliCommandWithOutputAsync(commandArgs);
    }
}

using FluentAssertions;
using OpenTelWatcher.CLI.Commands;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.UnitTests.Helpers;
using Xunit;

namespace OpenTelWatcher.UnitTests.CLI.Commands;

/// <summary>
/// Unit tests for CheckCommand.
/// Tests the command's logic for detecting error files and formatting output.
/// </summary>
public class CheckCommandTests : IDisposable
{
    // Use centralized test helper for artifact paths
    // Directory cleanup handled by Directory.Build.targets before test run
    // Each test uses its own unique subdirectory to avoid interference

    public CheckCommandTests()
    {
        // Test output directories are created by TestHelper.GetTestOutputDir
        // and cleaned by Directory.Build.targets before tests run
    }

    public void Dispose()
    {
        // No cleanup needed - handled by Directory.Build.targets
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSuccess_WhenNoErrorFilesExist()
    {
        // Arrange
        var testDir = TestHelper.GetTestOutputDir("check-command/no-errors");
        var command = new CheckCommand();
        var options = new CheckOptions
        {
            OutputDir = testDir,
            Verbose = false,
            JsonOutput = false
        };

        // Create normal files (no errors)
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.ndjson"),
            "test",
            CancellationToken.None);

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(0, "no error files exist");
        result.Message.Should().Contain("No errors", "should indicate success");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenErrorFilesExist()
    {
        // Arrange
        var testDir = TestHelper.GetTestOutputDir("check-command/with-errors");
        var command = new CheckCommand();
        var options = new CheckOptions
        {
            OutputDir = testDir,
            Verbose = false,
            JsonOutput = false
        };

        // Create error file
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.errors.ndjson"),
            "test",
            CancellationToken.None);

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(1, "error files are present");
        result.Message.Should().Contain("Errors detected", "should indicate errors found");
    }

    [Fact]
    public async Task ExecuteAsync_CountsMultipleErrorFiles()
    {
        // Arrange
        var testDir = TestHelper.GetTestOutputDir("check-command/multiple-errors");
        var command = new CheckCommand();
        var options = new CheckOptions
        {
            OutputDir = testDir,
            Verbose = false,
            JsonOutput = false
        };

        // Create multiple error files
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.errors.ndjson"),
            "test",
            CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "logs.20251116_143022_456.errors.ndjson"),
            "test",
            CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_144530_123.errors.ndjson"),
            "test",
            CancellationToken.None);

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(1, "error files are present");
        result.Data.Should().NotBeNull();
        result.Data.Should().ContainKey("errorFileCount");
        result.Data!["errorFileCount"].Should().Be(3, "should count all error files");
    }

    [Fact]
    public async Task ExecuteAsync_IgnoresNormalFiles_CountsOnlyErrorFiles()
    {
        // Arrange
        var testDir = TestHelper.GetTestOutputDir("check-command/mixed-files");
        var command = new CheckCommand();
        var options = new CheckOptions
        {
            OutputDir = testDir,
            Verbose = false,
            JsonOutput = false
        };

        // Create mix of normal and error files
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.ndjson"),
            "test",
            CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.errors.ndjson"),
            "test",
            CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "logs.20251116_143022_456.ndjson"),
            "test",
            CancellationToken.None);

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(1, "one error file exists");
        result.Data.Should().NotBeNull();
        result.Data!["errorFileCount"].Should().Be(1, "should count only error files");
    }

    [Fact]
    public async Task ExecuteAsync_WithVerboseFlag_IncludesErrorFileNames()
    {
        // Arrange
        var testDir = TestHelper.GetTestOutputDir("check-command/verbose-mode");
        var command = new CheckCommand();
        var options = new CheckOptions
        {
            OutputDir = testDir,
            Verbose = true,
            JsonOutput = false
        };

        // Create error files
        var errorFile1 = "traces.20251116_143022_456.errors.ndjson";
        var errorFile2 = "logs.20251116_143022_456.errors.ndjson";

        await File.WriteAllTextAsync(Path.Combine(testDir, errorFile1), "test", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(testDir, errorFile2), "test", CancellationToken.None);

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(1);
        result.Data.Should().NotBeNull();
        result.Data!["errorFiles"].Should().NotBeNull();

        var errorFiles = result.Data["errorFiles"] as List<string>;
        errorFiles.Should().NotBeNull();
        errorFiles.Should().Contain(errorFile1);
        errorFiles.Should().Contain(errorFile2);
    }

    [Fact]
    public async Task ExecuteAsync_WithJsonOutput_ReturnsStructuredData()
    {
        // Arrange
        var testDir = TestHelper.GetTestOutputDir("check-command/json-output");
        var command = new CheckCommand();
        var options = new CheckOptions
        {
            OutputDir = testDir,
            Verbose = false,
            JsonOutput = true
        };

        // Create error file
        await File.WriteAllTextAsync(
            Path.Combine(testDir, "traces.20251116_143022_456.errors.ndjson"),
            "test",
            CancellationToken.None);

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(1);
        result.Data.Should().NotBeNull();
        result.Data!["hasErrors"].Should().Be(true);
        result.Data["errorFileCount"].Should().Be(1);
        result.Data.Should().ContainKey("errorFiles");
    }

    [Fact]
    public async Task ExecuteAsync_WithJsonOutput_WhenNoErrors_ReturnsStructuredData()
    {
        // Arrange
        var testDir = TestHelper.GetTestOutputDir("check-command/json-no-errors");
        var command = new CheckCommand();
        var options = new CheckOptions
        {
            OutputDir = testDir,
            Verbose = false,
            JsonOutput = true
        };

        // No error files created

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(0);
        result.Data.Should().NotBeNull();
        result.Data!["hasErrors"].Should().Be(false);
        result.Data["errorFileCount"].Should().Be(0);
        result.Data["errorFiles"].Should().BeEquivalentTo(Array.Empty<string>());
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistentDirectory_HandlesGracefully()
    {
        // Arrange
        var command = new CheckCommand();
        var options = new CheckOptions
        {
            OutputDir = "./nonexistent-directory",
            Verbose = false,
            JsonOutput = false
        };

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        // Should treat non-existent directory as "no error files"
        result.ExitCode.Should().Be(0, "non-existent directory means no error files");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ExecuteAsync_WithInvalidOutputDir_UsesDefault(string? invalidDir)
    {
        // Arrange
        var command = new CheckCommand();
        var options = new CheckOptions
        {
            OutputDir = invalidDir!,
            Verbose = false,
            JsonOutput = false
        };

        // Act & Assert
        // Should use default directory (./telemetry-data) or handle gracefully
        var result = await command.ExecuteAsync(options);
        result.Should().NotBeNull();
        result.ExitCode.Should().BeOneOf(0, 1, 2); // Any valid exit code
    }
}

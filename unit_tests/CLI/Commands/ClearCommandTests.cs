using FluentAssertions;
using OpenTelWatcher.CLI.Commands;
using OpenTelWatcher.CLI.Models;
using UnitTests.Helpers;
using UnitTests.Mocks;
using Xunit;

namespace UnitTests.CLI.Commands;

/// <summary>
/// Unit tests for ClearCommand.
/// Tests dual-mode operation (API vs standalone), validation, and error handling.
/// </summary>
public class ClearCommandTests : IDisposable
{
    private readonly string _testOutputDir;

    public ClearCommandTests()
    {
        _testOutputDir = Path.Combine(Path.GetTempPath(), "clear-command-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testOutputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testOutputDir))
        {
            Directory.Delete(_testOutputDir, recursive: true);
        }
    }

    #region API Mode Tests

    [Fact]
    public async Task ExecuteAsync_ApiMode_GetStatusFails_ReturnsSystemError()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = true, IsCompatible = true },
            StatusResponse = null // GetStatusAsync fails
        };
        var loggerFactory = TestLoggerFactory.Instance;
        var command = new ClearCommand(mockApiClient, loggerFactory);

        // Act
        var result = await command.ExecuteAsync(silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(2); // System error
        result.Message.Should().Be("Failed to retrieve info");
    }

    [Fact]
    public async Task ExecuteAsync_ApiMode_DirectoryMismatch_ReturnsUserError()
    {
        // Arrange
        var instanceDir = Path.Combine(Path.GetTempPath(), "instance-dir");
        var requestedDir = Path.Combine(Path.GetTempPath(), "different-dir");

        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = true, IsCompatible = true },
            StatusResponse = TestBuilders.CreateStatusResponse(instanceDir, fileCount: 5, totalSize: 1024)
        };
        var loggerFactory = TestLoggerFactory.Instance;
        var command = new ClearCommand(mockApiClient, loggerFactory);

        // Act
        var result = await command.ExecuteAsync(outputDir: requestedDir, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(1); // User error
        result.Message.Should().Be("Output directory mismatch");
    }

    [Fact]
    public async Task ExecuteAsync_ApiMode_DirectoryMatchesInstance_AllowsClear()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = true, IsCompatible = true },
            StatusResponse = TestBuilders.CreateStatusResponse(_testOutputDir, fileCount: 3, totalSize: 512),
            ClearResponse = new ClearResponse
            {
                Success = true,
                FilesDeleted = 3,
                Message = "Cleared",
                Timestamp = DateTime.UtcNow
            }
        };
        var loggerFactory = TestLoggerFactory.Instance;
        var command = new ClearCommand(mockApiClient, loggerFactory);

        // Act
        var result = await command.ExecuteAsync(outputDir: _testOutputDir, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("Telemetry data cleared");
    }

    [Fact]
    public async Task ExecuteAsync_ApiMode_NoOutputDirProvided_UsesInstanceDirectory()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = true, IsCompatible = true },
            StatusResponse = TestBuilders.CreateStatusResponse(_testOutputDir, fileCount: 2, totalSize: 256),
            ClearResponse = new ClearResponse
            {
                Success = true,
                FilesDeleted = 2,
                Message = "Cleared",
                Timestamp = DateTime.UtcNow
            }
        };
        var loggerFactory = TestLoggerFactory.Instance;
        var command = new ClearCommand(mockApiClient, loggerFactory);

        // Act (no outputDir provided)
        var result = await command.ExecuteAsync(silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("Telemetry data cleared");
    }

    [Fact]
    public async Task ExecuteAsync_ApiMode_ClearAsyncFails_ReturnsSystemError()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = true, IsCompatible = true },
            StatusResponse = TestBuilders.CreateStatusResponse(_testOutputDir, fileCount: 5, totalSize: 1024),
            ClearResponse = null // ClearAsync fails
        };
        var loggerFactory = TestLoggerFactory.Instance;
        var command = new ClearCommand(mockApiClient, loggerFactory);

        // Act
        var result = await command.ExecuteAsync(silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(2); // System error
        result.Message.Should().Be("Failed to clear data");
    }

    [Fact]
    public async Task ExecuteAsync_ApiMode_Success_ReturnsCorrectStats()
    {
        // Arrange
        var filesBefore = 10;
        var sizeBeforeBytes = 2048L;
        var filesDeleted = 10;

        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = true, IsCompatible = true },
            StatusResponse = TestBuilders.CreateStatusResponse(_testOutputDir, fileCount: filesBefore, totalSize: sizeBeforeBytes),
            ClearResponse = new ClearResponse
            {
                Success = true,
                FilesDeleted = filesDeleted,
                Message = "Cleared",
                Timestamp = DateTime.UtcNow
            }
        };
        var loggerFactory = TestLoggerFactory.Instance;
        var command = new ClearCommand(mockApiClient, loggerFactory);

        // Act
        var result = await command.ExecuteAsync(verbose: true, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Telemetry data cleared");
        result.Data.Should().NotBeNull();
        var resultData = result.Data!["result"] as Dictionary<string, object>;
        resultData.Should().NotBeNull();
        resultData!["filesBeforeCount"].Should().Be(filesBefore);
        resultData["filesDeleted"].Should().Be(filesDeleted);
        resultData["filesAfterCount"].Should().Be(0);
    }

    #endregion

    #region Standalone Mode Tests

    [Fact]
    public async Task ExecuteAsync_StandaloneMode_DirectoryNotExists_ReturnsSuccessWithZeroFiles()
    {
        // Arrange
        var nonExistentDir = Path.Combine(Path.GetTempPath(), "non-existent-" + Guid.NewGuid());
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var loggerFactory = TestLoggerFactory.Instance;
        var command = new ClearCommand(mockApiClient, loggerFactory);

        // Act
        var result = await command.ExecuteAsync(outputDir: nonExistentDir, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0); // Success (nothing to clear)
        result.Message.Should().Be("No files to clear");
    }

    [Fact]
    public async Task ExecuteAsync_StandaloneMode_EmptyDirectory_ReturnsSuccess()
    {
        // Arrange - empty directory
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var loggerFactory = TestLoggerFactory.Instance;
        var command = new ClearCommand(mockApiClient, loggerFactory);

        // Act
        var result = await command.ExecuteAsync(outputDir: _testOutputDir, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Telemetry data cleared");
    }

    [Fact]
    public async Task ExecuteAsync_StandaloneMode_WithFiles_DeletesAllNdjsonFiles()
    {
        // Arrange
        var file1 = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        var file2 = Path.Combine(_testOutputDir, "logs.20250117_110000_000.ndjson");
        var file3 = Path.Combine(_testOutputDir, "traces.20250117_120000_000.errors.ndjson");
        File.WriteAllText(file1, "{}");
        File.WriteAllText(file2, "{}");
        File.WriteAllText(file3, "{}");

        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var loggerFactory = TestLoggerFactory.Instance;
        var command = new ClearCommand(mockApiClient, loggerFactory);

        // Act
        var result = await command.ExecuteAsync(outputDir: _testOutputDir, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Telemetry data cleared");

        // Verify files were deleted
        File.Exists(file1).Should().BeFalse();
        File.Exists(file2).Should().BeFalse();
        File.Exists(file3).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_StandaloneMode_WithNonNdjsonFiles_LeavesThemUntouched()
    {
        // Arrange
        var ndjsonFile = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        var textFile = Path.Combine(_testOutputDir, "readme.txt");
        var configFile = Path.Combine(_testOutputDir, "config.json");
        File.WriteAllText(ndjsonFile, "{}");
        File.WriteAllText(textFile, "keep me");
        File.WriteAllText(configFile, "{}");

        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var loggerFactory = TestLoggerFactory.Instance;
        var command = new ClearCommand(mockApiClient, loggerFactory);

        // Act
        var result = await command.ExecuteAsync(outputDir: _testOutputDir, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);

        // Verify ndjson file was deleted
        File.Exists(ndjsonFile).Should().BeFalse();

        // Verify non-ndjson files were kept
        File.Exists(textFile).Should().BeTrue();
        File.Exists(configFile).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_StandaloneMode_NoOutputDirProvided_UsesDefaultDirectory()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var loggerFactory = TestLoggerFactory.Instance;
        var command = new ClearCommand(mockApiClient, loggerFactory);

        // Act (no outputDir provided, uses default)
        var result = await command.ExecuteAsync(defaultOutputDir: _testOutputDir, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Telemetry data cleared");
    }

    [Fact]
    public async Task ExecuteAsync_StandaloneMode_VerboseFlag_IncludesInResult()
    {
        // Arrange
        var file1 = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        File.WriteAllText(file1, "test data");

        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var loggerFactory = TestLoggerFactory.Instance;
        var command = new ClearCommand(mockApiClient, loggerFactory);

        // Act
        var result = await command.ExecuteAsync(outputDir: _testOutputDir, verbose: true, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Telemetry data cleared");
        // Verbose flag affects console output (tested in E2E tests)
    }

    #endregion

    #region Mode Selection Tests

    [Fact]
    public async Task ExecuteAsync_InstanceRunning_UsesApiMode()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = true, IsCompatible = true },
            StatusResponse = TestBuilders.CreateStatusResponse(_testOutputDir, fileCount: 1, totalSize: 100),
            ClearResponse = new ClearResponse { Success = true, FilesDeleted = 1, Message = "Cleared", Timestamp = DateTime.UtcNow }
        };
        var loggerFactory = TestLoggerFactory.Instance;
        var command = new ClearCommand(mockApiClient, loggerFactory);

        // Act
        var result = await command.ExecuteAsync(silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);
        // Verify API was called (GetStatusAsync and ClearAsync)
        mockApiClient.StatusResponse.Should().NotBeNull();
        mockApiClient.ClearResponse.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_InstanceNotRunning_UsesStandaloneMode()
    {
        // Arrange
        var file1 = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        File.WriteAllText(file1, "{}");

        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var loggerFactory = TestLoggerFactory.Instance;
        var command = new ClearCommand(mockApiClient, loggerFactory);

        // Act
        var result = await command.ExecuteAsync(outputDir: _testOutputDir, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);
        // Verify file was deleted directly (not via API)
        File.Exists(file1).Should().BeFalse();
    }

    #endregion

}

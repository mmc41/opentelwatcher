using FluentAssertions;
using OpenTelWatcher.CLI.Commands;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.Services;
using UnitTests.Helpers;
using UnitTests.Mocks;
using Xunit;

namespace UnitTests.CLI.Commands;

/// <summary>
/// Unit tests for StatusCommand.
/// Tests all display modes, error handling, and dual-mode operation (API vs filesystem).
/// </summary>
public class StatusCommandTests : IDisposable
{
    private readonly string _testOutputDir;

    public StatusCommandTests()
    {
        _testOutputDir = Path.Combine(Path.GetTempPath(), "status-command-test-" + Guid.NewGuid());
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
    public async Task ExecuteAsync_WhenInstanceNotRunning_ReturnsSystemError()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var command = new StatusCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StatusCommand>(), new ErrorFileScanner());

        // Act
        var result = await command.ExecuteAsync(new StatusOptions { Port = null, Quiet = true }, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(2); // System error
        result.Message.Should().Be("No instance running");
    }

    [Fact]
    public async Task ExecuteAsync_WhenGetStatusFails_ReturnsSystemError()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = true, IsCompatible = true },
            StatusResponse = null // API call fails
        };
        var command = new StatusCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StatusCommand>(), new ErrorFileScanner());

        // Act
        var result = await command.ExecuteAsync(new StatusOptions { Port = null, Quiet = true }, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(2); // System error
        result.Message.Should().Be("Failed to retrieve info");
    }

    [Fact]
    public async Task ExecuteAsync_FullMode_WhenHealthy_ReturnsSuccess()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus
            {
                IsRunning = true,
                IsCompatible = true,
                Version = new VersionResponse
                {
                    Application = "OpenTelWatcher",
                    Version = "1.0.0",
                    VersionComponents = new VersionComponents { Major = 1, Minor = 0, Patch = 0 },
                    ProcessId = 1234
                }
            },
            StatusResponse = TestBuilders.CreateStatusResponse(_testOutputDir)
        };
        var command = new StatusCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StatusCommand>(), new ErrorFileScanner());

        // Act
        var result = await command.ExecuteAsync(new StatusOptions { Port = null, Quiet = true }, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("Healthy");
    }

    [Fact]
    public async Task ExecuteAsync_FullMode_WhenUnhealthy_ReturnsUserError()
    {
        // Arrange
        var errorFile = Path.Combine(_testOutputDir, "traces.20250117_100000_000.errors.ndjson");
        File.WriteAllText(errorFile, "{}\n");

        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = true, IsCompatible = true },
            StatusResponse = TestBuilders.CreateStatusResponse(_testOutputDir)
        };
        var command = new StatusCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StatusCommand>(), new ErrorFileScanner());

        // Act
        var result = await command.ExecuteAsync(new StatusOptions { Port = null, Quiet = true }, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(1); // User error (unhealthy)
        result.Message.Should().Be("Unhealthy");
    }

    [Fact]
    public async Task ExecuteAsync_ErrorsOnlyMode_WithoutVerbose_ReturnsErrorCount()
    {
        // Arrange
        var errorFile = Path.Combine(_testOutputDir, "logs.20250117_100000_000.errors.ndjson");
        File.WriteAllText(errorFile, "{}\n");

        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = true, IsCompatible = true },
            StatusResponse = TestBuilders.CreateStatusResponse(_testOutputDir)
        };
        var command = new StatusCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StatusCommand>(), new ErrorFileScanner());

        // Act
        var result = await command.ExecuteAsync(new StatusOptions { Port = null, ErrorsOnly = true, Quiet = true }, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(1); // Errors detected
        result.Message.Should().Be("Errors detected");
    }

    [Fact]
    public async Task ExecuteAsync_ErrorsOnlyMode_WithVerbose_IncludesErrorFileList()
    {
        // Arrange
        var errorFile1 = Path.Combine(_testOutputDir, "traces.20250117_100000_000.errors.ndjson");
        var errorFile2 = Path.Combine(_testOutputDir, "logs.20250117_110000_000.errors.ndjson");
        File.WriteAllText(errorFile1, "{}\n");
        File.WriteAllText(errorFile2, "{}\n");

        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = true, IsCompatible = true },
            StatusResponse = TestBuilders.CreateStatusResponse(_testOutputDir)
        };
        var command = new StatusCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StatusCommand>(), new ErrorFileScanner());

        // Act
        var result = await command.ExecuteAsync(new StatusOptions { Port = null, ErrorsOnly = true, Verbose = true, Quiet = true }, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(1); // Errors detected
        result.Message.Should().Be("Errors detected");
        // Verbose mode includes error file list in output (tested via console output in integration tests)
    }

    [Fact]
    public async Task ExecuteAsync_ErrorsOnlyMode_NoErrors_ReturnsSuccess()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = true, IsCompatible = true },
            StatusResponse = TestBuilders.CreateStatusResponse(_testOutputDir)
        };
        var command = new StatusCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StatusCommand>(), new ErrorFileScanner());

        // Act
        var result = await command.ExecuteAsync(new StatusOptions { Port = null, ErrorsOnly = true, Quiet = true }, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("No errors");
    }

    [Fact]
    public async Task ExecuteAsync_StatsOnlyMode_ReturnsSuccess()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = true, IsCompatible = true },
            StatusResponse = TestBuilders.CreateStatusResponse(_testOutputDir)
        };
        var command = new StatusCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StatusCommand>(), new ErrorFileScanner());

        // Act
        var result = await command.ExecuteAsync(new StatusOptions { Port = null, StatsOnly = true, Quiet = true }, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("Stats retrieved");
    }

    [Fact]
    public async Task ExecuteAsync_VerboseMode_WhenHealthy_ReturnsSuccess()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = true, IsCompatible = true },
            StatusResponse = TestBuilders.CreateStatusResponse(_testOutputDir)
        };
        var command = new StatusCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StatusCommand>(), new ErrorFileScanner());

        // Act
        var result = await command.ExecuteAsync(new StatusOptions { Port = null, Verbose = true, Quiet = true }, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("Healthy");
    }

    [Fact]
    public async Task ExecuteAsync_VerboseMode_WhenUnhealthy_ReturnsUserError()
    {
        // Arrange
        var errorFile = Path.Combine(_testOutputDir, "metrics.20250117_100000_000.errors.ndjson");
        File.WriteAllText(errorFile, "{}\n");

        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = true, IsCompatible = true },
            StatusResponse = TestBuilders.CreateStatusResponse(_testOutputDir)
        };
        var command = new StatusCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StatusCommand>(), new ErrorFileScanner());

        // Act
        var result = await command.ExecuteAsync(new StatusOptions { Port = null, Verbose = true, Quiet = true }, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(1); // User error
        result.Message.Should().Be("Unhealthy");
    }

    [Fact]
    public async Task ExecuteAsync_WithIncompatibleVersion_StillReturnsStatus()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus
            {
                IsRunning = true,
                IsCompatible = false,
                IncompatibilityReason = "Version mismatch: CLI is 2.0.0, server is 1.0.0"
            },
            StatusResponse = TestBuilders.CreateStatusResponse(_testOutputDir)
        };
        var command = new StatusCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StatusCommand>(), new ErrorFileScanner());

        // Act
        var result = await command.ExecuteAsync(new StatusOptions { Port = null, Quiet = true }, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0); // Success (still returns status despite incompatibility)
        result.Message.Should().Be("Healthy");
    }

    #endregion

    #region Filesystem Mode Tests

    [Fact]
    public async Task ExecuteAsync_FilesystemMode_DirectoryNotExists_ReturnsSystemError()
    {
        // Arrange
        var nonExistentDir = Path.Combine(Path.GetTempPath(), "non-existent-" + Guid.NewGuid());
        var mockApiClient = new MockOpenTelWatcherApiClient();
        var command = new StatusCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StatusCommand>(), new ErrorFileScanner());

        // Act
        var result = await command.ExecuteAsync(new StatusOptions { Port = null, OutputDir = nonExistentDir, Quiet = true }, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(2); // System error
        result.Message.Should().Contain("Output directory does not exist");
    }

    [Fact]
    public async Task ExecuteAsync_FilesystemMode_NoErrorFiles_ReturnsSuccess()
    {
        // Arrange
        // Create some normal files (not error files)
        File.WriteAllText(Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson"), "{}\n");
        File.WriteAllText(Path.Combine(_testOutputDir, "logs.20250117_100000_000.ndjson"), "{}\n");

        var mockApiClient = new MockOpenTelWatcherApiClient();
        var command = new StatusCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StatusCommand>(), new ErrorFileScanner());

        // Act
        var result = await command.ExecuteAsync(new StatusOptions { Port = null, OutputDir = _testOutputDir, Quiet = true }, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("No errors");
    }

    [Fact]
    public async Task ExecuteAsync_FilesystemMode_WithErrorFiles_ReturnsUserError()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testOutputDir, "traces.20250117_100000_000.errors.ndjson"), "{}\n");
        File.WriteAllText(Path.Combine(_testOutputDir, "logs.20250117_110000_000.errors.ndjson"), "{}\n");

        var mockApiClient = new MockOpenTelWatcherApiClient();
        var command = new StatusCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StatusCommand>(), new ErrorFileScanner());

        // Act
        var result = await command.ExecuteAsync(new StatusOptions { Port = null, OutputDir = _testOutputDir, Quiet = true }, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(1); // User error (errors detected)
        result.Message.Should().Be("Errors detected");
    }

    [Fact]
    public async Task ExecuteAsync_FilesystemMode_EmptyDirectory_ReturnsSuccess()
    {
        // Arrange - empty directory
        var mockApiClient = new MockOpenTelWatcherApiClient();
        var command = new StatusCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StatusCommand>(), new ErrorFileScanner());

        // Act
        var result = await command.ExecuteAsync(new StatusOptions { Port = null, OutputDir = _testOutputDir, Quiet = true }, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("No errors");
    }

    #endregion

    #region Mode Selection Tests

    [Fact]
    public async Task ExecuteAsync_WithOutputDir_ForcesFilesystemMode()
    {
        // Arrange
        // Even if instance is running, --output-dir forces filesystem mode
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = true, IsCompatible = true },
            StatusResponse = TestBuilders.CreateStatusResponse(_testOutputDir)
        };
        var command = new StatusCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StatusCommand>(), new ErrorFileScanner());

        // Act
        var result = await command.ExecuteAsync(new StatusOptions { Port = null, OutputDir = _testOutputDir, Quiet = true }, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("No errors");
        // Verify API was NOT called (filesystem mode)
        mockApiClient.GetInstanceStatusCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutOutputDir_TriesApiModeFirst()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = true, IsCompatible = true },
            StatusResponse = TestBuilders.CreateStatusResponse(_testOutputDir)
        };
        var command = new StatusCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StatusCommand>(), new ErrorFileScanner());

        // Act
        var result = await command.ExecuteAsync(new StatusOptions { Port = null, Quiet = true }, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("Healthy");
        // Verify API was called
        mockApiClient.GetInstanceStatusCalls.Should().HaveCount(1);
    }

    #endregion

}

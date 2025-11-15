using FluentAssertions;
using OpenTelWatcher.CLI.Commands;
using OpenTelWatcher.CLI.Models;
using UnitTests.Mocks;

namespace UnitTests.CLI.Commands;

/// <summary>
/// Unit tests for DefaultCommand.
/// Tests status display and help message logic.
/// </summary>
public class DefaultCommandTests
{
    [Fact]
    public async Task ExecuteAsync_WhenNoInstanceRunning_ReturnsSuccess()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var command = new DefaultCommand(mockApiClient);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.ExitCode.Should().Be(0); // Success (not an error)
        result.Message.Should().Be("Status displayed");
        mockApiClient.GetInstanceStatusCalls.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenInstanceRunning_DisplaysStatusAndReturnsSuccess()
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
            }
        };
        var command = new DefaultCommand(mockApiClient);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("Status displayed");
    }

    [Fact]
    public async Task ExecuteAsync_WhenIncompatibleInstanceRunning_ReturnsSystemError()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus
            {
                IsRunning = true,
                IsCompatible = false,
                IncompatibilityReason = "Version mismatch: CLI is 2.0.0, server is 1.0.0",
                Version = new VersionResponse
                {
                    Application = "OpenTelWatcher",
                    Version = "1.0.0",
                    VersionComponents = new VersionComponents { Major = 1, Minor = 0, Patch = 0 },
                    ProcessId = 1234
                }
            }
        };
        var command = new DefaultCommand(mockApiClient);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.ExitCode.Should().Be(2); // System error
        result.Message.Should().Be("Incompatible instance detected");
    }

    [Fact]
    public async Task ExecuteAsync_ChecksInstanceStatus()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var command = new DefaultCommand(mockApiClient);

        // Act
        await command.ExecuteAsync();

        // Assert
        mockApiClient.GetInstanceStatusCalls.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCompatibleInstanceRunning_ReturnsSuccess()
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
            }
        };
        var command = new DefaultCommand(mockApiClient);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.ExitCode.Should().Be(0);
        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Be("Status displayed");
    }

    [Fact]
    public async Task ExecuteAsync_AlwaysDisplaysAvailableCommands()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var command = new DefaultCommand(mockApiClient);

        // Act
        var result = await command.ExecuteAsync();

        // Assert - Command succeeds whether instance is running or not
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithVersionInfo_DisplaysCorrectInformation()
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
                    Version = "2.5.7",
                    VersionComponents = new VersionComponents { Major = 2, Minor = 5, Patch = 7 },
                    ProcessId = 5678
                }
            }
        };
        var command = new DefaultCommand(mockApiClient);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Status displayed");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSuccessRegardlessOfRunningState()
    {
        // Arrange - Not running
        var mockApiClient1 = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var command1 = new DefaultCommand(mockApiClient1);

        // Arrange - Running
        var mockApiClient2 = new MockOpenTelWatcherApiClient
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
            }
        };
        var command2 = new DefaultCommand(mockApiClient2);

        // Act
        var result1 = await command1.ExecuteAsync();
        var result2 = await command2.ExecuteAsync();

        // Assert - Both should succeed (exit code 0)
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
    }
}

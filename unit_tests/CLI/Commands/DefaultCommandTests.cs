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
}

using FluentAssertions;
using OpenTelWatcher.CLI.Commands;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.CLI.Services;
using OpenTelWatcher.Services.Interfaces;
using UnitTests.Helpers;
using UnitTests.Mocks;

namespace UnitTests.CLI.Commands;

/// <summary>
/// Unit tests for StopCommand.
/// Tests stop logic without actual API calls.
/// </summary>
public class StopCommandTests
{
    [Fact]
    public async Task ExecuteAsync_WhenNoInstanceRunning_ReturnsUserError()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var command = new StopCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StopCommand>());

        // Act
        var result = await command.ExecuteAsync(new CommandOptions { Port = null, Silent = false });

        // Assert
        result.ExitCode.Should().Be(1); // User error
        result.Message.Should().Be("No instance running");
        mockApiClient.GetInstanceStatusCalls.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenInstanceRunning_SendsStopRequest()
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
            StopSuccess = true,
            WaitForStopResult = true
        };
        var command = new StopCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StopCommand>());

        // Act
        var result = await command.ExecuteAsync(new CommandOptions { Port = null, Silent = false });

        // Assert
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("Service stopped");
    }

    [Fact]
    public async Task ExecuteAsync_WhenStopRequestFails_ReturnsSystemError()
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
            StopSuccess = false // Stop request fails
        };
        var command = new StopCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StopCommand>());

        // Act
        var result = await command.ExecuteAsync(new CommandOptions { Port = null, Silent = false });

        // Assert
        result.ExitCode.Should().Be(2); // System error
        result.Message.Should().Be("Failed to send shutdown request");
    }

    [Fact]
    public async Task ExecuteAsync_WhenStopSucceeds_ReturnsSuccess()
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
            StopSuccess = true,
            WaitForStopResult = true
        };
        var command = new StopCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StopCommand>());

        // Act
        var result = await command.ExecuteAsync(new CommandOptions { Port = null, Silent = false });

        // Assert
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("Service stopped");
    }

    [Fact]
    public async Task ExecuteAsync_WithIncompatibleVersion_AttemptsStopAnyway()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus
            {
                IsRunning = true,
                IsCompatible = false,
                IncompatibilityReason = "Version mismatch",
                Version = new VersionResponse
                {
                    Application = "OpenTelWatcher",
                    Version = "1.0.0",
                    VersionComponents = new VersionComponents { Major = 1, Minor = 0, Patch = 0 },
                    ProcessId = 1234
                }
            },
            StopSuccess = true,
            WaitForStopResult = true
        };
        var command = new StopCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StopCommand>());

        // Act
        var result = await command.ExecuteAsync(new CommandOptions { Port = null, Silent = false });

        // Assert - Should still succeed despite incompatibility warning
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("Service stopped");
    }

    [Fact]
    public async Task ExecuteAsync_WhenStopTimesOut_ReturnsSystemError()
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
                },
                Pid = null // No PID available
            },
            StopSuccess = true,
            WaitForStopResult = false // Timeout
        };
        var command = new StopCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StopCommand>());

        // Act
        var result = await command.ExecuteAsync(new CommandOptions { Port = null, Silent = false });

        // Assert
        result.ExitCode.Should().Be(2); // System error
        result.Message.Should().Be("Shutdown timeout");
    }

    [Fact]
    public async Task ExecuteAsync_ChecksInstanceStatus_BeforeStop()
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
            StopSuccess = true,
            WaitForStopResult = true
        };
        var command = new StopCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StopCommand>());

        // Act
        await command.ExecuteAsync(new CommandOptions { Port = null, Silent = false });

        // Assert
        mockApiClient.GetInstanceStatusCalls.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_WaitsForStop_AfterSendingRequest()
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
            StopSuccess = true,
            WaitForStopResult = true
        };
        var command = new StopCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StopCommand>());

        // Act
        var result = await command.ExecuteAsync(new CommandOptions { Port = null, Silent = false });

        // Assert
        result.IsSuccess.Should().BeTrue();
        // If stop succeeded, it means WaitForStopAsync was called and returned true
    }

    // Note: Force kill tests require actual process management and are better suited for E2E tests.
    // The current implementation uses Process.GetProcessById() which requires real PIDs.

    [Fact]
    public async Task ExecuteAsync_WithSilentFlag_ReturnsSuccess()
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
            StopSuccess = true,
            WaitForStopResult = true
        };
        var command = new StopCommand(mockApiClient, new MockPortResolver(), TestLoggerFactory.CreateLogger<StopCommand>());

        // Act
        var result = await command.ExecuteAsync(new CommandOptions { Port = null, Silent = true });

        // Assert
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Service stopped");
        // Silent mode suppresses output but command still succeeds
    }

    // Note: Port resolution tests have been removed from StopCommand unit tests.
    // Port resolution now happens in CliApplication before StopCommand is invoked,
    // ensuring the HttpClient is configured with the correct port.
    // Port resolution logic is tested in PortResolverTests.
}

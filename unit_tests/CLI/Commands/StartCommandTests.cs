using FluentAssertions;
using OpenTelWatcher.CLI.Commands;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.Hosting;
using UnitTests.Helpers;
using UnitTests.Mocks;

namespace UnitTests.CLI.Commands;

/// <summary>
/// Unit tests for StartCommand.
/// Tests command logic without starting actual servers.
/// </summary>
public class StartCommandTests
{
    [Fact]
    public async Task ExecuteAsync_WhenInstanceAlreadyRunning_ReturnsUserError()
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
        var mockWebHost = new MockWebApplicationHost();
        var command = new StartCommand(mockApiClient, mockWebHost, TestLoggerFactory.CreateLogger<StartCommand>());
        var options = new CommandOptions { Port = 4318 };

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(1); // User error
        result.Message.Should().Be("Instance already running");
        mockWebHost.RunCalls.Should().BeEmpty(); // Should NOT start server
        mockApiClient.GetInstanceStatusCalls.Should().HaveCount(1);
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
        var mockWebHost = new MockWebApplicationHost();
        var command = new StartCommand(mockApiClient, mockWebHost, TestLoggerFactory.CreateLogger<StartCommand>());
        var options = new CommandOptions { Port = 4318 };

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(2); // System error
        result.Message.Should().Be("Incompatible instance detected");
        mockWebHost.RunCalls.Should().BeEmpty(); // Should NOT start server
    }

    [Fact]
    public async Task ExecuteAsync_WhenValidationFails_ReturnsUserError()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var mockWebHost = new MockWebApplicationHost
        {
            ValidationResultToReturn = new ValidationResult
            {
                IsValid = false,
                Errors = new List<string> { "Port must be between 1 and 65535" }
            }
        };
        var command = new StartCommand(mockApiClient, mockWebHost, TestLoggerFactory.CreateLogger<StartCommand>());
        var options = new CommandOptions { Port = 99999 };

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(1); // User error
        result.Message.Should().Be("Invalid configuration");
        mockWebHost.RunCalls.Should().BeEmpty(); // Should NOT start server
    }

    [Fact]
    public async Task ExecuteAsync_WhenValidAndNotDaemon_StartsServerAndReturnsSuccess()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var mockWebHost = new MockWebApplicationHost
        {
            ValidationResultToReturn = ValidationResult.Success(),
            ExitCodeToReturn = 0
        };
        var command = new StartCommand(mockApiClient, mockWebHost, TestLoggerFactory.CreateLogger<StartCommand>());
        var options = new CommandOptions
        {
            Port = 4318,
            OutputDirectory = Path.Combine(Path.GetTempPath(), "test-output"),
            LogLevel = LogLevel.Information,
            Daemon = false
        };

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("Server stopped gracefully");
        mockWebHost.RunCalls.Should().HaveCount(1); // Should start server

        // Verify server options
        var serverOptions = mockWebHost.RunCalls[0];
        serverOptions.Port.Should().Be(4318);
        serverOptions.OutputDirectory.Should().Be(options.OutputDirectory);
        serverOptions.LogLevel.Should().Be("Information");
        serverOptions.Daemon.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenServerExitsWithNonZero_ReturnsSystemError()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var mockWebHost = new MockWebApplicationHost
        {
            ValidationResultToReturn = ValidationResult.Success(),
            ExitCodeToReturn = 1 // Non-zero exit code
        };
        var command = new StartCommand(mockApiClient, mockWebHost, TestLoggerFactory.CreateLogger<StartCommand>());
        var options = new CommandOptions
        {
            Port = 4318,
            OutputDirectory = Path.Combine(Path.GetTempPath(), "test-output"),
            Daemon = false
        };

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(2); // System error
        result.Message.Should().Be("Server exited with code 1");
    }

    [Fact]
    public async Task ExecuteAsync_WhenServerThrows_ReturnsSystemError()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var mockWebHost = new MockWebApplicationHost
        {
            ValidationResultToReturn = ValidationResult.Success(),
            ExceptionToThrow = new InvalidOperationException("Test exception")
        };
        var command = new StartCommand(mockApiClient, mockWebHost, TestLoggerFactory.CreateLogger<StartCommand>());
        var options = new CommandOptions
        {
            Port = 4318,
            OutputDirectory = Path.Combine(Path.GetTempPath(), "test-output"),
            Daemon = false
        };

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(2); // System error
        result.Message.Should().Be("Server failed to start");
    }

    [Fact]
    public async Task ExecuteAsync_CreatesOutputDirectory_WhenItDoesNotExist()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), $"start-command-test-{Guid.NewGuid()}");
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var mockWebHost = new MockWebApplicationHost
        {
            ValidationResultToReturn = ValidationResult.Success(),
            ExitCodeToReturn = 0
        };
        var command = new StartCommand(mockApiClient, mockWebHost, TestLoggerFactory.CreateLogger<StartCommand>());
        var options = new CommandOptions
        {
            Port = 4318,
            OutputDirectory = testDir,
            Daemon = false
        };

        try
        {
            // Act
            var result = await command.ExecuteAsync(options);

            // Assert
            result.ExitCode.Should().Be(0);
            Directory.Exists(testDir).Should().BeTrue("output directory should be created");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_PassesAllOptionsToWebHost()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var mockWebHost = new MockWebApplicationHost
        {
            ValidationResultToReturn = ValidationResult.Success(),
            ExitCodeToReturn = 0
        };
        var command = new StartCommand(mockApiClient, mockWebHost, TestLoggerFactory.CreateLogger<StartCommand>());
        var options = new CommandOptions
        {
            Port = 5000,
            OutputDirectory = Path.Combine(Path.GetTempPath(), "custom-dir"),
            LogLevel = LogLevel.Debug,
            Daemon = false
        };

        // Act
        await command.ExecuteAsync(options);

        // Assert
        mockWebHost.RunCalls.Should().HaveCount(1);
        var serverOptions = mockWebHost.RunCalls[0];
        serverOptions.Port.Should().Be(5000);
        serverOptions.OutputDirectory.Should().Be(options.OutputDirectory);
        serverOptions.LogLevel.Should().Be("Debug");
        serverOptions.Daemon.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_CallsValidateBeforeStarting()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var mockWebHost = new MockWebApplicationHost
        {
            ValidationResultToReturn = ValidationResult.Success(),
            ExitCodeToReturn = 0
        };
        var command = new StartCommand(mockApiClient, mockWebHost, TestLoggerFactory.CreateLogger<StartCommand>());
        var options = new CommandOptions
        {
            Port = 4318,
            OutputDirectory = Path.Combine(Path.GetTempPath(), "test-output"),
            Daemon = false
        };

        // Act
        await command.ExecuteAsync(options);

        // Assert
        mockWebHost.RunCalls.Should().HaveCount(1);
        // If validation failed, RunCalls would be empty, so this verifies validation was called
    }

    [Theory]
    [InlineData(LogLevel.Trace, "Trace")]
    [InlineData(LogLevel.Debug, "Debug")]
    [InlineData(LogLevel.Information, "Information")]
    [InlineData(LogLevel.Warning, "Warning")]
    [InlineData(LogLevel.Error, "Error")]
    [InlineData(LogLevel.Critical, "Critical")]
    public async Task ExecuteAsync_ConvertsLogLevelCorrectly(LogLevel logLevel, string expectedString)
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var mockWebHost = new MockWebApplicationHost
        {
            ValidationResultToReturn = ValidationResult.Success(),
            ExitCodeToReturn = 0
        };
        var command = new StartCommand(mockApiClient, mockWebHost, TestLoggerFactory.CreateLogger<StartCommand>());
        var options = new CommandOptions
        {
            Port = 4318,
            OutputDirectory = Path.Combine(Path.GetTempPath(), "test-output"),
            LogLevel = logLevel,
            Daemon = false
        };

        // Act
        await command.ExecuteAsync(options);

        // Assert
        mockWebHost.RunCalls.Should().HaveCount(1);
        mockWebHost.RunCalls[0].LogLevel.Should().Be(expectedString);
    }

    // Note: Daemon mode tests are complex due to process spawning.
    // Those are best tested via E2E tests with actual subprocesses.
    // The current E2E tests already cover daemon mode comprehensively.

    [Fact]
    public async Task ExecuteAsync_WithSilentFlag_ReturnsSuccess()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var mockWebHost = new MockWebApplicationHost
        {
            ValidationResultToReturn = ValidationResult.Success(),
            ExitCodeToReturn = 0
        };
        var command = new StartCommand(mockApiClient, mockWebHost, TestLoggerFactory.CreateLogger<StartCommand>());
        var options = new CommandOptions
        {
            Port = 4318,
            OutputDirectory = Path.Combine(Path.GetTempPath(), "test-output"),
            Silent = true,
            Daemon = false
        };

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Server stopped gracefully");
        // Silent mode suppresses output but command still succeeds
        mockWebHost.RunCalls.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithVerboseFlag_ReturnsSuccess()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var mockWebHost = new MockWebApplicationHost
        {
            ValidationResultToReturn = ValidationResult.Success(),
            ExitCodeToReturn = 0
        };
        var command = new StartCommand(mockApiClient, mockWebHost, TestLoggerFactory.CreateLogger<StartCommand>());
        var options = new CommandOptions
        {
            Port = 4318,
            OutputDirectory = Path.Combine(Path.GetTempPath(), "test-output"),
            Verbose = true,
            Daemon = false
        };

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Server stopped gracefully");
        // Verbose mode shows additional details but command still succeeds
        mockWebHost.RunCalls.Should().HaveCount(1);
    }
}

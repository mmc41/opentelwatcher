using FluentAssertions;
using OpenTelWatcher.CLI.Commands;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.Hosting;
using OpenTelWatcher.Services.Interfaces;
using UnitTests.Helpers;
using UnitTests.Mocks;

namespace UnitTests.CLI.Commands;

/// <summary>
/// Unit tests for StartCommand.
/// Tests command logic without starting actual servers.
/// </summary>
public class StartCommandTests
{
    private readonly MockOpenTelWatcherApiClient _mockApiClient;
    private readonly MockWebApplicationHost _mockWebHost;
    private readonly MockProcessProvider _mockProcessProvider;
    private readonly MockTimeProvider _mockTimeProvider;
    private readonly MockPidFileService _mockPidFileService;

    public StartCommandTests()
    {
        // Create common mocks used by most tests
        _mockApiClient = new MockOpenTelWatcherApiClient();
        _mockWebHost = new MockWebApplicationHost();
        _mockProcessProvider = new MockProcessProvider();
        _mockTimeProvider = new MockTimeProvider();
        _mockPidFileService = new MockPidFileService(_mockProcessProvider);
    }

    private StartCommand CreateCommand()
    {
        return new StartCommand(
            _mockApiClient,
            _mockWebHost,
            _mockPidFileService,
            _mockProcessProvider,
            _mockTimeProvider,
            TestLoggerFactory.CreateLogger<StartCommand>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenInstanceAlreadyRunning_ReturnsUserError()
    {
        // Arrange
        _mockApiClient.InstanceStatus = new InstanceStatus
        {
            IsRunning = true,
            IsCompatible = true,
            Version = new VersionResponse
            {
                Application = "OpenTelWatcher",
                Version = TestConstants.Versions.VersionString,
                VersionComponents = new VersionComponents
                {
                    Major = TestConstants.Versions.MajorVersion,
                    Minor = TestConstants.Versions.MinorVersion,
                    Patch = TestConstants.Versions.PatchVersion
                },
                ProcessId = TestConstants.ProcessIds.DefaultTestPid
            }
        };
        var command = CreateCommand();
        var options = new CommandOptions { Port = TestConstants.Network.DefaultPort };

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(1); // User error
        result.Message.Should().Be("Instance already running");
        _mockWebHost.RunCalls.Should().BeEmpty(); // Should NOT start server
        _mockApiClient.GetInstanceStatusCalls.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenIncompatibleInstanceRunning_ReturnsSystemError()
    {
        // Arrange
        _mockApiClient.InstanceStatus = new InstanceStatus
        {
            IsRunning = true,
            IsCompatible = false,
            IncompatibilityReason = "Version mismatch: CLI is 2.0.0, server is 1.0.0",
            Version = new VersionResponse
            {
                Application = "OpenTelWatcher",
                Version = TestConstants.Versions.VersionString,
                VersionComponents = new VersionComponents
                {
                    Major = TestConstants.Versions.MajorVersion,
                    Minor = TestConstants.Versions.MinorVersion,
                    Patch = TestConstants.Versions.PatchVersion
                },
                ProcessId = TestConstants.ProcessIds.DefaultTestPid
            }
        };
        var command = CreateCommand();
        var options = new CommandOptions { Port = TestConstants.Network.DefaultPort };

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(2); // System error
        result.Message.Should().Be("Incompatible instance detected");
        _mockWebHost.RunCalls.Should().BeEmpty(); // Should NOT start server
    }

    [Fact]
    public async Task ExecuteAsync_WhenValidationFails_ReturnsUserError()
    {
        // Arrange
        _mockApiClient.InstanceStatus = new InstanceStatus { IsRunning = false };
        _mockWebHost.ValidationResultToReturn = new ValidationResult
        {
            IsValid = false,
            Errors = new List<string> { "Port must be between 1 and 65535" }
        };
        var command = CreateCommand();
        var options = new CommandOptions { Port = 99999 };

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(1); // User error
        result.Message.Should().Be("Invalid configuration");
        _mockWebHost.RunCalls.Should().BeEmpty(); // Should NOT start server
    }

    [Fact]
    public async Task ExecuteAsync_WhenValidAndNotDaemon_StartsServerAndReturnsSuccess()
    {
        // Arrange
        _mockApiClient.InstanceStatus = new InstanceStatus { IsRunning = false };
        _mockWebHost.ValidationResultToReturn = ValidationResult.Success();
        _mockWebHost.ExitCodeToReturn = 0;
        var command = CreateCommand();
        var options = new CommandOptions
        {
            Port = TestConstants.Network.DefaultPort,
            OutputDirectory = Path.Combine(Path.GetTempPath(), "test-output"),
            LogLevel = LogLevel.Information,
            Daemon = false
        };

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("Server stopped gracefully");
        _mockWebHost.RunCalls.Should().HaveCount(1); // Should start server

        // Verify server options
        var serverOptions = _mockWebHost.RunCalls[0];
        serverOptions.Port.Should().Be(TestConstants.Network.DefaultPort);
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
        var mockProcessProvider = new MockProcessProvider();
        var mockTimeProvider = new MockTimeProvider();
        var mockPidFileService = new MockPidFileService(mockProcessProvider);
        var command = new StartCommand(mockApiClient, mockWebHost, mockPidFileService, mockProcessProvider, mockTimeProvider, TestLoggerFactory.CreateLogger<StartCommand>());
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
        var mockProcessProvider = new MockProcessProvider();
        var mockTimeProvider = new MockTimeProvider();
        var mockPidFileService = new MockPidFileService(mockProcessProvider);
        var command = new StartCommand(mockApiClient, mockWebHost, mockPidFileService, mockProcessProvider, mockTimeProvider, TestLoggerFactory.CreateLogger<StartCommand>());
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
        var mockProcessProvider = new MockProcessProvider();
        var mockTimeProvider = new MockTimeProvider();
        var mockPidFileService = new MockPidFileService(mockProcessProvider);
        var command = new StartCommand(mockApiClient, mockWebHost, mockPidFileService, mockProcessProvider, mockTimeProvider, TestLoggerFactory.CreateLogger<StartCommand>());
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
        var mockProcessProvider = new MockProcessProvider();
        var mockTimeProvider = new MockTimeProvider();
        var mockPidFileService = new MockPidFileService(mockProcessProvider);
        var command = new StartCommand(mockApiClient, mockWebHost, mockPidFileService, mockProcessProvider, mockTimeProvider, TestLoggerFactory.CreateLogger<StartCommand>());
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
        _mockApiClient.InstanceStatus = new InstanceStatus { IsRunning = false };
        _mockWebHost.ValidationResultToReturn = ValidationResult.Success();
        _mockWebHost.ExitCodeToReturn = 0;
        var command = CreateCommand();
        var options = new CommandOptions
        {
            Port = TestConstants.Network.DefaultPort,
            OutputDirectory = Path.Combine(Path.GetTempPath(), "test-output"),
            Daemon = false
        };

        // Act
        await command.ExecuteAsync(options);

        // Assert
        _mockWebHost.ValidateCalls.Should().HaveCount(1, "Validate should be called before starting");
        _mockWebHost.ValidateCalls[0].Port.Should().Be(TestConstants.Network.DefaultPort);
        _mockWebHost.RunCalls.Should().HaveCount(1, "Server should start after successful validation");
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
        var mockProcessProvider = new MockProcessProvider();
        var mockTimeProvider = new MockTimeProvider();
        var mockPidFileService = new MockPidFileService(mockProcessProvider);
        var command = new StartCommand(mockApiClient, mockWebHost, mockPidFileService, mockProcessProvider, mockTimeProvider, TestLoggerFactory.CreateLogger<StartCommand>());
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
        var mockProcessProvider = new MockProcessProvider();
        var mockTimeProvider = new MockTimeProvider();
        var mockPidFileService = new MockPidFileService(mockProcessProvider);
        var command = new StartCommand(mockApiClient, mockWebHost, mockPidFileService, mockProcessProvider, mockTimeProvider, TestLoggerFactory.CreateLogger<StartCommand>());
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
        var mockProcessProvider = new MockProcessProvider();
        var mockTimeProvider = new MockTimeProvider();
        var mockPidFileService = new MockPidFileService(mockProcessProvider);
        var command = new StartCommand(mockApiClient, mockWebHost, mockPidFileService, mockProcessProvider, mockTimeProvider, TestLoggerFactory.CreateLogger<StartCommand>());
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

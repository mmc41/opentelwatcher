using FluentAssertions;
using OpenTelWatcher.CLI.Commands;
using OpenTelWatcher.CLI.Models;
using UnitTests.Mocks;

namespace UnitTests.CLI.Commands;

/// <summary>
/// Unit tests for InfoCommand.
/// Tests information display logic without actual API calls.
/// </summary>
public class InfoCommandTests
{
    [Fact]
    public async Task ExecuteAsync_WhenNoInstanceRunning_ReturnsUserError()
    {
        // Arrange
        var mockApiClient = new MockOpenTelWatcherApiClient
        {
            InstanceStatus = new InstanceStatus { IsRunning = false }
        };
        var command = new InfoCommand(mockApiClient);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.ExitCode.Should().Be(1); // User error
        result.Message.Should().Be("No instance running");
        mockApiClient.GetInstanceStatusCalls.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenInstanceRunning_GetsInfo()
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
            InfoResponse = new InfoResponse
            {
                Application = "OpenTelWatcher",
                Version = "1.0.0",
                VersionComponents = new VersionComponents { Major = 1, Minor = 0, Patch = 0 },
                ProcessId = 1234,
            Port = 4318,
                Health = new DiagnoseHealth
                {
                    Status = "Healthy",
                    ConsecutiveErrors = 0,
                    RecentErrors = new List<string>()
                },
                Files = new FileStatistics
                {
                    Count = 0,
                    TotalSizeBytes = 0
                },
                Configuration = new DiagnoseConfiguration
                {
                    OutputDirectory = "./telemetry-data"
                }
            }
        };
        var command = new InfoCommand(mockApiClient);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("Information displayed");
    }

    [Fact]
    public async Task ExecuteAsync_WhenInfoFails_ReturnsSystemError()
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
            InfoResponse = null // Failed to retrieve
        };
        var command = new InfoCommand(mockApiClient);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.ExitCode.Should().Be(2); // System error
        result.Message.Should().Be("Failed to retrieve info");
    }

    [Fact]
    public async Task ExecuteAsync_WithIncompatibleVersion_StillAttemptsInfo()
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
            InfoResponse = new InfoResponse
            {
                Application = "OpenTelWatcher",
                Version = "1.0.0",
                VersionComponents = new VersionComponents { Major = 1, Minor = 0, Patch = 0 },
                ProcessId = 1234,
            Port = 4318,
                Health = new DiagnoseHealth
                {
                    Status = "Healthy",
                    ConsecutiveErrors = 0,
                    RecentErrors = new List<string>()
                },
                Files = new FileStatistics
                {
                    Count = 0,
                    TotalSizeBytes = 0
                },
                Configuration = new DiagnoseConfiguration
                {
                    OutputDirectory = "./telemetry-data"
                }
            }
        };
        var command = new InfoCommand(mockApiClient);

        // Act
        var result = await command.ExecuteAsync();

        // Assert - Should display info despite incompatibility warning
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("Information displayed");
    }

    [Fact]
    public async Task ExecuteAsync_WithHealthyStatus_ReturnsSuccess()
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
            InfoResponse = new InfoResponse
            {
                Application = "OpenTelWatcher",
                Version = "1.0.0",
                VersionComponents = new VersionComponents { Major = 1, Minor = 0, Patch = 0 },
                ProcessId = 1234,
            Port = 4318,
                Health = new DiagnoseHealth
                {
                    Status = "Healthy",
                    ConsecutiveErrors = 0,
                    RecentErrors = new List<string>()
                },
                Files = new FileStatistics
                {
                    Count = 1,
                    TotalSizeBytes = 1024
                },
                Configuration = new DiagnoseConfiguration
                {
                    OutputDirectory = "./telemetry-data"
                }
            }
        };
        var command = new InfoCommand(mockApiClient);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Information displayed");
    }

    [Fact]
    public async Task ExecuteAsync_WithErrors_StillReturnsSuccess()
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
            InfoResponse = new InfoResponse
            {
                Application = "OpenTelWatcher",
                Version = "1.0.0",
                VersionComponents = new VersionComponents { Major = 1, Minor = 0, Patch = 0 },
                ProcessId = 1234,
            Port = 4318,
                Health = new DiagnoseHealth
                {
                    Status = "Degraded",
                    ConsecutiveErrors = 3,
                    RecentErrors = new List<string>
                    {
                        "Error 1: Disk full",
                        "Error 2: Permission denied",
                        "Error 3: File locked"
                    }
                },
                Files = new FileStatistics
                {
                    Count = 0,
                    TotalSizeBytes = 0
                },
                Configuration = new DiagnoseConfiguration
                {
                    OutputDirectory = "./telemetry-data"
                }
            }
        };
        var command = new InfoCommand(mockApiClient);

        // Act
        var result = await command.ExecuteAsync();

        // Assert - Command succeeds even if service has errors
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Information displayed");
    }

    [Fact]
    public async Task ExecuteAsync_WithManyFiles_StillReturnsSuccess()
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
            InfoResponse = new InfoResponse
            {
                Application = "OpenTelWatcher",
                Version = "1.0.0",
                VersionComponents = new VersionComponents { Major = 1, Minor = 0, Patch = 0 },
                ProcessId = 1234,
            Port = 4318,
                Health = new DiagnoseHealth
                {
                    Status = "Healthy",
                    ConsecutiveErrors = 0,
                    RecentErrors = new List<string>()
                },
                Files = new FileStatistics
                {
                    Count = 50,
                    TotalSizeBytes = 50 * 1024 * 1024 // 50 MB
                },
                Configuration = new DiagnoseConfiguration
                {
                    OutputDirectory = "./telemetry-data"
                }
            }
        };
        var command = new InfoCommand(mockApiClient);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Information displayed");
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyInfo_StillReturnsSuccess()
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
            InfoResponse = new InfoResponse
            {
                Application = "OpenTelWatcher",
                Version = "1.0.0",
                VersionComponents = new VersionComponents { Major = 1, Minor = 0, Patch = 0 },
                ProcessId = 1234,
            Port = 4318,
                Health = new DiagnoseHealth
                {
                    Status = "Healthy",
                    ConsecutiveErrors = 0,
                    RecentErrors = new List<string>()
                },
                Files = new FileStatistics
                {
                    Count = 0,
                    TotalSizeBytes = 0
                },
                Configuration = new DiagnoseConfiguration
                {
                    OutputDirectory = "./telemetry-data"
                }
            }
        };
        var command = new InfoCommand(mockApiClient);

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Information displayed");
    }

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
            InfoResponse = new InfoResponse
            {
                Application = "OpenTelWatcher",
                Version = "1.0.0",
                VersionComponents = new VersionComponents { Major = 1, Minor = 0, Patch = 0 },
                ProcessId = 1234,
            Port = 4318,
                Health = new DiagnoseHealth
                {
                    Status = "Healthy",
                    ConsecutiveErrors = 0,
                    RecentErrors = new List<string>()
                },
                Files = new FileStatistics
                {
                    Count = 5,
                    TotalSizeBytes = 10240
                },
                Configuration = new DiagnoseConfiguration
                {
                    OutputDirectory = "./telemetry-data"
                }
            }
        };
        var command = new InfoCommand(mockApiClient);

        // Act
        var result = await command.ExecuteAsync(verbose: false, silent: true);

        // Assert
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Information displayed");
        // Silent mode suppresses output but command still succeeds
    }

    [Fact]
    public async Task ExecuteAsync_WithVerboseFlag_ReturnsSuccess()
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
            InfoResponse = new InfoResponse
            {
                Application = "OpenTelWatcher",
                Version = "1.0.0",
                VersionComponents = new VersionComponents { Major = 1, Minor = 0, Patch = 0 },
                ProcessId = 1234,
            Port = 4318,
                Health = new DiagnoseHealth
                {
                    Status = "Healthy",
                    ConsecutiveErrors = 0,
                    RecentErrors = new List<string>()
                },
                Files = new FileStatistics
                {
                    Count = 5,
                    TotalSizeBytes = 10240
                },
                Configuration = new DiagnoseConfiguration
                {
                    OutputDirectory = "./telemetry-data"
                }
            }
        };
        var command = new InfoCommand(mockApiClient);

        // Act
        var result = await command.ExecuteAsync(verbose: true, silent: false);

        // Assert
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Information displayed");
        // Verbose mode shows additional details but command still succeeds
    }
}

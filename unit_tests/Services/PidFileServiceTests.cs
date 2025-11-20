using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenTelWatcher.Services;
using OpenTelWatcher.Services.Interfaces;
using UnitTests.Helpers;

namespace UnitTests.Services;

/// <summary>
/// Unit tests for PidFileService.
/// Uses REAL file system (temp directories) to test file locking and catch subtle file system errors.
/// </summary>
public class PidFileServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly MockEnvironment _mockEnvironment;
    private readonly MockProcessProvider _mockProcessProvider;
    private readonly MockTimeProvider _mockTimeProvider;
    private readonly ILogger<PidFileService> _logger;

    public PidFileServiceTests()
    {
        // Create unique temp directory for each test
        _testDirectory = Path.Combine(Path.GetTempPath(), $"pidfile-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);

        _mockEnvironment = new MockEnvironment
        {
            CurrentProcessId = Environment.ProcessId,
            BaseDirectory = _testDirectory, // Use test directory (not artifacts)
            TempPath = _testDirectory
        };

        _mockProcessProvider = new MockProcessProvider();
        _mockTimeProvider = new MockTimeProvider();
        _logger = TestLoggerFactory.CreateLogger<PidFileService>();
    }

    public void Dispose()
    {
        // Cleanup test directory
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch (Exception ex)
            {
                // Log cleanup errors but don't throw to prevent test failures during disposal
                _logger.LogWarning(ex, "Failed to cleanup test directory {TestDirectory}", _testDirectory);
            }
        }
    }

    [Fact]
    public void Constructor_CreatesPidFilePathInTempDirectory()
    {
        // Arrange & Act
        var service = new PidFileService(_mockEnvironment, _mockProcessProvider, _mockTimeProvider, _logger);

        // Assert
        service.PidFilePath.Should().StartWith(_testDirectory);
        service.PidFilePath.Should().EndWith(TestConstants.FileNames.PidFileName);
    }

    [Fact]
    public void Constructor_WhenBaseDirectoryContainsArtifacts_UsesThatDirectory()
    {
        // Arrange
        using var artifactsDir = new TempDirectory("artifacts-test");

        var mockEnv = new MockEnvironment
        {
            CurrentProcessId = 9999,
            BaseDirectory = artifactsDir.Path,
            TempPath = _testDirectory
        };

        // Act
        var service = new PidFileService(mockEnv, _mockProcessProvider, _mockTimeProvider, _logger);

        // Assert
        service.PidFilePath.Should().StartWith(artifactsDir.Path, "should use BaseDirectory when it contains 'artifacts'");
    }

    [Fact]
    public void Register_CreatesFileWithCorrectEntry()
    {
        // Arrange
        var service = new PidFileService(_mockEnvironment, _mockProcessProvider, _mockTimeProvider, _logger);
        var testPort = 4318;
        var testTimestamp = new DateTime(2025, 1, 17, 12, 0, 0, DateTimeKind.Utc);
        _mockTimeProvider.UtcNow = testTimestamp;

        // Act
        service.Register(testPort);

        // Assert
        File.Exists(service.PidFilePath).Should().BeTrue("PID file should be created");

        var entries = service.GetRegisteredEntries();
        entries.Should().HaveCount(1);
        entries[0].Pid.Should().Be(Environment.ProcessId);
        entries[0].Port.Should().Be(testPort);
        entries[0].Timestamp.Should().Be(testTimestamp);
    }

    [Fact]
    public void Register_MultipleProcesses_CreatesMultipleEntries()
    {
        // Arrange
        var service1 = new PidFileService(_mockEnvironment, _mockProcessProvider, _mockTimeProvider, _logger);

        var mockEnv2 = new MockEnvironment
        {
            CurrentProcessId = 99999, // Different PID
            BaseDirectory = _testDirectory,
            TempPath = _testDirectory
        };
        var service2 = new PidFileService(mockEnv2, _mockProcessProvider, _mockTimeProvider, _logger);

        // Act
        service1.Register(4318);
        service2.Register(4319);

        // Assert
        var entries = service1.GetRegisteredEntries();
        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.Pid == Environment.ProcessId && e.Port == 4318);
        entries.Should().Contain(e => e.Pid == 99999 && e.Port == 4319);
    }

    [Fact]
    public void Register_SameProcessTwice_CreatesTwoEntries()
    {
        // Arrange
        var service = new PidFileService(_mockEnvironment, _mockProcessProvider, _mockTimeProvider, _logger);

        // Act
        service.Register(4318);
        service.Register(5000); // Same process, different port

        // Assert
        var entries = service.GetRegisteredEntries();
        entries.Should().HaveCount(2, "both registrations should be recorded");
        entries.Where(e => e.Pid == Environment.ProcessId).Should().HaveCount(2);
    }

    [Fact]
    public void Unregister_RemovesCurrentProcessEntry()
    {
        // Arrange
        var service = new PidFileService(_mockEnvironment, _mockProcessProvider, _mockTimeProvider, _logger);
        service.Register(4318);

        // Act
        service.Unregister();

        // Assert
        File.Exists(service.PidFilePath).Should().BeFalse("PID file should be deleted when last entry removed");
    }

    [Fact]
    public void Unregister_WithMultipleProcesses_RemovesOnlyCurrentProcess()
    {
        // Arrange
        var service1 = new PidFileService(_mockEnvironment, _mockProcessProvider, _mockTimeProvider, _logger);

        var mockEnv2 = new MockEnvironment
        {
            CurrentProcessId = 99999,
            BaseDirectory = _testDirectory,
            TempPath = _testDirectory
        };
        var service2 = new PidFileService(mockEnv2, _mockProcessProvider, _mockTimeProvider, _logger);

        service1.Register(4318);
        service2.Register(4319);

        // Act
        service1.Unregister();

        // Assert
        File.Exists(service1.PidFilePath).Should().BeTrue("PID file should still exist");

        var entries = service1.GetRegisteredEntries();
        entries.Should().HaveCount(1);
        entries[0].Pid.Should().Be(99999, "only process 99999 should remain");
    }

    [Fact]
    public void Unregister_Idempotent_SafeToCallMultipleTimes()
    {
        // Arrange
        var service = new PidFileService(_mockEnvironment, _mockProcessProvider, _mockTimeProvider, _logger);
        service.Register(4318);

        // Act
        service.Unregister();
        service.Unregister(); // Call again
        service.Unregister(); // And again

        // Assert
        File.Exists(service.PidFilePath).Should().BeFalse();
    }

    [Fact]
    public void Unregister_WhenFileDoesNotExist_DoesNotThrow()
    {
        // Arrange
        var service = new PidFileService(_mockEnvironment, _mockProcessProvider, _mockTimeProvider, _logger);

        // Act
        var act = () => service.Unregister();

        // Assert
        act.Should().NotThrow("unregistering when file doesn't exist should be safe");
    }

    [Fact]
    public void GetRegisteredEntries_WhenFileDoesNotExist_ReturnsEmpty()
    {
        // Arrange
        var service = new PidFileService(_mockEnvironment, _mockProcessProvider, _mockTimeProvider, _logger);

        // Act
        var entries = service.GetRegisteredEntries();

        // Assert
        entries.Should().BeEmpty();
    }

    [Fact]
    public void GetRegisteredEntriesForPort_FiltersCorrectly()
    {
        // Arrange
        var service = new PidFileService(_mockEnvironment, _mockProcessProvider, _mockTimeProvider, _logger);
        service.Register(4318);
        service.Register(5000);

        var mockEnv2 = new MockEnvironment
        {
            CurrentProcessId = 99999,
            BaseDirectory = _testDirectory,
            TempPath = _testDirectory
        };
        var service2 = new PidFileService(mockEnv2, _mockProcessProvider, _mockTimeProvider, _logger);
        service2.Register(4318);

        // Act
        var entriesOnPort4318 = service.GetRegisteredEntriesForPort(4318);
        var entriesOnPort5000 = service.GetRegisteredEntriesForPort(5000);

        // Assert
        entriesOnPort4318.Should().HaveCount(2, "two processes on port 4318");
        entriesOnPort5000.Should().HaveCount(1, "one process on port 5000");
    }

    [Fact]
    public void GetEntryByPid_ReturnsCorrectEntry()
    {
        // Arrange
        var service1 = new PidFileService(_mockEnvironment, _mockProcessProvider, _mockTimeProvider, _logger);
        service1.Register(4318);

        var mockEnv2 = new MockEnvironment
        {
            CurrentProcessId = 99999,
            BaseDirectory = _testDirectory,
            TempPath = _testDirectory
        };
        var service2 = new PidFileService(mockEnv2, _mockProcessProvider, _mockTimeProvider, _logger);
        service2.Register(4319);

        // Act
        var entry1 = service1.GetEntryByPid(Environment.ProcessId);
        var entry2 = service1.GetEntryByPid(99999);
        var entryNotFound = service1.GetEntryByPid(88888);

        // Assert
        entry1.Should().NotBeNull();
        entry1!.Port.Should().Be(4318);

        entry2.Should().NotBeNull();
        entry2!.Port.Should().Be(4319);

        entryNotFound.Should().BeNull();
    }

    [Fact]
    public void GetEntryByPort_ReturnsFirstMatch()
    {
        // Arrange
        var service = new PidFileService(_mockEnvironment, _mockProcessProvider, _mockTimeProvider, _logger);
        service.Register(4318);

        var mockEnv2 = new MockEnvironment
        {
            CurrentProcessId = 99999,
            BaseDirectory = _testDirectory,
            TempPath = _testDirectory
        };
        var service2 = new PidFileService(mockEnv2, _mockProcessProvider, _mockTimeProvider, _logger);
        service2.Register(4318); // Same port

        // Act
        var entry = service.GetEntryByPort(4318);

        // Assert
        entry.Should().NotBeNull();
        // Returns first match (implementation detail - order may vary)
    }

    [Fact]
    public void CleanStaleEntries_RemovesDeadProcesses()
    {
        // Arrange
        var service1 = new PidFileService(_mockEnvironment, _mockProcessProvider, _mockTimeProvider, _logger);
        service1.Register(4318);

        var mockEnv2 = new MockEnvironment
        {
            CurrentProcessId = 99999,
            BaseDirectory = _testDirectory,
            TempPath = _testDirectory
        };
        var service2 = new PidFileService(mockEnv2, _mockProcessProvider, _mockTimeProvider, _logger);
        service2.Register(4319);

        // Setup: PID 99999 is dead, current process is alive
        _mockProcessProvider.AddProcess(Environment.ProcessId, "opentelwatcher", hasExited: false);

        // Act
        var removedCount = service1.CleanStaleEntries();

        // Assert
        removedCount.Should().Be(1, "one dead process should be removed");

        var entries = service1.GetRegisteredEntries();
        entries.Should().HaveCount(1);
        entries[0].Pid.Should().Be(Environment.ProcessId, "only alive process should remain");
    }

    [Fact]
    public void CleanStaleEntries_KeepsAliveProcesses()
    {
        // Arrange
        var service = new PidFileService(_mockEnvironment, _mockProcessProvider, _mockTimeProvider, _logger);
        service.Register(4318);

        _mockProcessProvider.AddProcess(Environment.ProcessId, "opentelwatcher", hasExited: false);

        // Act
        var removedCount = service.CleanStaleEntries();

        // Assert
        removedCount.Should().Be(0, "no processes should be removed");

        var entries = service.GetRegisteredEntries();
        entries.Should().HaveCount(1);
    }

    [Fact]
    public void CleanStaleEntries_DeletesFileWhenAllEntriesRemoved()
    {
        // Arrange
        var service = new PidFileService(_mockEnvironment, _mockProcessProvider, _mockTimeProvider, _logger);
        service.Register(4318);

        // No process added to provider, so it's considered dead

        // Act
        var removedCount = service.CleanStaleEntries();

        // Assert
        removedCount.Should().Be(1);
        File.Exists(service.PidFilePath).Should().BeFalse("file should be deleted when all entries removed");
    }

    [Fact]
    public void CleanStaleEntries_WhenFileDoesNotExist_ReturnsZero()
    {
        // Arrange
        var service = new PidFileService(_mockEnvironment, _mockProcessProvider, _mockTimeProvider, _logger);

        // Act
        var removedCount = service.CleanStaleEntries();

        // Assert
        removedCount.Should().Be(0);
    }

    [Fact]
    public async Task FileLocking_ConcurrentRegistrations_MostSucceed()
    {
        // Arrange - simulate concurrent access with retry logic
        var services = Enumerable.Range(0, 5).Select(i =>
        {
            var mockEnv = new MockEnvironment
            {
                CurrentProcessId = 10000 + i,
                BaseDirectory = _testDirectory,
                TempPath = _testDirectory
            };
            return new PidFileService(mockEnv, _mockProcessProvider, _mockTimeProvider, _logger);
        }).ToList();

        // Act - register concurrently
        var tasks = services.Select((service, index) =>
            Task.Run(() => service.Register(4318 + index))
        ).ToArray();

        await Task.WhenAll(tasks);

        // Assert - verify registrations succeeded
        // Due to file locking retry logic (5 attempts, 50ms delay), multiple should succeed
        var entries = services[0].GetRegisteredEntries();

        // With proper file locking and retry logic, we expect 2-5 successful registrations
        // Allow for timing variations by accepting 2+ successes (at least some concurrent access works)
        entries.Count.Should().BeGreaterThanOrEqualTo(2,
            "file locking with retry should allow concurrent registrations to succeed");
        entries.Count.Should().BeLessThanOrEqualTo(5,
            "should not exceed number of registration attempts");

        // Verify PIDs are unique (no duplicate registrations)
        entries.Select(e => e.Pid).Distinct().Count().Should().Be(entries.Count,
            "each registration should have a unique PID");
    }

    [Fact]
    public void Register_WithInvalidTimestamp_StillWorks()
    {
        // Arrange
        var service = new PidFileService(_mockEnvironment, _mockProcessProvider, _mockTimeProvider, _logger);
        _mockTimeProvider.UtcNow = DateTime.MinValue; // Edge case

        // Act
        var act = () => service.Register(4318);

        // Assert
        act.Should().NotThrow();
        service.GetRegisteredEntries().Should().HaveCount(1);
    }

    [Fact]
    public void PidFilePath_UsesXdgRuntimeDir_WhenAvailable()
    {
        // Arrange
        using var xdgDir = new TempDirectory("xdg-test");

        var regularDir = Path.Combine(Path.GetTempPath(), "regular-dir");
        var mockEnv = new MockEnvironment
        {
            CurrentProcessId = 9999,
            BaseDirectory = regularDir, // Not an artifacts directory (doesn't contain "artifacts" substring)
            TempPath = _testDirectory
        };
        mockEnv.SetEnvironmentVariable("XDG_RUNTIME_DIR", xdgDir.Path);

        // Act
        var service = new PidFileService(mockEnv, _mockProcessProvider, _mockTimeProvider, _logger);

        // Assert
        service.PidFilePath.Should().StartWith(xdgDir.Path, "should use XDG_RUNTIME_DIR when available");
    }

    // Helper class for managing temporary directories in tests
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid()}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                try
                {
                    Directory.Delete(Path, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}

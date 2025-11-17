using FluentAssertions;
using OpenTelWatcher.Services.Interfaces;
using UnitTests.Helpers;

namespace UnitTests.Services;

/// <summary>
/// Unit tests for PidEntry record.
/// Tests IsRunning() and GetAge() methods with mocked dependencies.
/// </summary>
public class PidEntryTests
{
    [Fact]
    public void IsRunning_WhenProcessNotFound_ReturnsFalse()
    {
        // Arrange
        var mockProcessProvider = new MockProcessProvider();
        var entry = new PidEntry
        {
            Pid = 12345,
            Port = 4318,
            Timestamp = DateTime.UtcNow
        };

        // Act
        var result = entry.IsRunning(mockProcessProvider);

        // Assert
        result.Should().BeFalse("process does not exist in mock provider");
    }

    [Fact]
    public void IsRunning_WhenProcessExited_ReturnsFalse()
    {
        // Arrange
        var mockProcessProvider = new MockProcessProvider();
        mockProcessProvider.AddProcess(12345, "opentelwatcher", hasExited: true);

        var entry = new PidEntry
        {
            Pid = 12345,
            Port = 4318,
            Timestamp = DateTime.UtcNow
        };

        // Act
        var result = entry.IsRunning(mockProcessProvider);

        // Assert
        result.Should().BeFalse("process has exited");
    }

    [Fact]
    public void IsRunning_WhenProcessNameIsOpenTelWatcher_ReturnsTrue()
    {
        // Arrange
        var mockProcessProvider = new MockProcessProvider();
        mockProcessProvider.AddProcess(12345, "opentelwatcher", hasExited: false);

        var entry = new PidEntry
        {
            Pid = 12345,
            Port = 4318,
            Timestamp = DateTime.UtcNow
        };

        // Act
        var result = entry.IsRunning(mockProcessProvider);

        // Assert
        result.Should().BeTrue("process name contains 'opentelwatcher'");
    }

    [Fact]
    public void IsRunning_WhenProcessNameIsWatcher_ReturnsTrue()
    {
        // Arrange
        var mockProcessProvider = new MockProcessProvider();
        mockProcessProvider.AddProcess(12345, "watcher", hasExited: false);

        var entry = new PidEntry
        {
            Pid = 12345,
            Port = 4318,
            Timestamp = DateTime.UtcNow
        };

        // Act
        var result = entry.IsRunning(mockProcessProvider);

        // Assert
        result.Should().BeTrue("process name contains 'watcher'");
    }

    [Fact]
    public void IsRunning_WhenProcessNameIsDotnet_ReturnsTrue()
    {
        // Arrange
        var mockProcessProvider = new MockProcessProvider();
        mockProcessProvider.AddProcess(12345, "dotnet", hasExited: false);

        var entry = new PidEntry
        {
            Pid = 12345,
            Port = 4318,
            Timestamp = DateTime.UtcNow
        };

        // Act
        var result = entry.IsRunning(mockProcessProvider);

        // Assert
        result.Should().BeTrue("process name contains 'dotnet'");
    }

    [Fact]
    public void IsRunning_WhenProcessNameIsUnrelated_ReturnsFalse()
    {
        // Arrange
        var mockProcessProvider = new MockProcessProvider();
        mockProcessProvider.AddProcess(12345, "notepad", hasExited: false);

        var entry = new PidEntry
        {
            Pid = 12345,
            Port = 4318,
            Timestamp = DateTime.UtcNow
        };

        // Act
        var result = entry.IsRunning(mockProcessProvider);

        // Assert
        result.Should().BeFalse("process name does not match expected patterns");
    }

    [Theory]
    [InlineData("OpenTelWatcher.exe")]
    [InlineData("OPENTELWATCHER")]
    [InlineData("opentelwatcher")]
    [InlineData("watcher.exe")]
    [InlineData("WATCHER")]
    [InlineData("dotnet.exe")]
    [InlineData("DOTNET")]
    public void IsRunning_IsCaseInsensitive(string processName)
    {
        // Arrange
        var mockProcessProvider = new MockProcessProvider();
        mockProcessProvider.AddProcess(12345, processName, hasExited: false);

        var entry = new PidEntry
        {
            Pid = 12345,
            Port = 4318,
            Timestamp = DateTime.UtcNow
        };

        // Act
        var result = entry.IsRunning(mockProcessProvider);

        // Assert
        result.Should().BeTrue($"process name '{processName}' should match (case-insensitive)");
    }

    [Fact]
    public void GetAge_ReturnsTimeSinceTimestamp()
    {
        // Arrange
        var mockTimeProvider = new MockTimeProvider
        {
            UtcNow = new DateTime(2025, 1, 17, 12, 0, 0, DateTimeKind.Utc)
        };

        var entry = new PidEntry
        {
            Pid = 12345,
            Port = 4318,
            Timestamp = new DateTime(2025, 1, 17, 11, 55, 0, DateTimeKind.Utc) // 5 minutes ago
        };

        // Act
        var age = entry.GetAge(mockTimeProvider);

        // Assert
        age.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void GetAge_WhenJustCreated_ReturnsZero()
    {
        // Arrange
        var now = new DateTime(2025, 1, 17, 12, 0, 0, DateTimeKind.Utc);
        var mockTimeProvider = new MockTimeProvider { UtcNow = now };

        var entry = new PidEntry
        {
            Pid = 12345,
            Port = 4318,
            Timestamp = now
        };

        // Act
        var age = entry.GetAge(mockTimeProvider);

        // Assert
        age.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GetAge_WithTimeProgression_ReturnsCorrectAge()
    {
        // Arrange
        var mockTimeProvider = new MockTimeProvider
        {
            UtcNow = new DateTime(2025, 1, 17, 12, 0, 0, DateTimeKind.Utc)
        };

        var entry = new PidEntry
        {
            Pid = 12345,
            Port = 4318,
            Timestamp = new DateTime(2025, 1, 17, 11, 0, 0, DateTimeKind.Utc)
        };

        // Act - initial age
        var age1 = entry.GetAge(mockTimeProvider);

        // Advance time by 30 minutes
        mockTimeProvider.AdvanceTime(TimeSpan.FromMinutes(30));
        var age2 = entry.GetAge(mockTimeProvider);

        // Assert
        age1.Should().Be(TimeSpan.FromHours(1), "initial age should be 1 hour");
        age2.Should().Be(TimeSpan.FromHours(1.5), "age after 30 minutes should be 1.5 hours");
    }

    [Fact]
    public void PidEntry_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var timestamp = DateTime.UtcNow;
        var entry = new PidEntry
        {
            Pid = 12345,
            Port = 4318,
            Timestamp = timestamp
        };

        // Assert
        entry.Pid.Should().Be(12345);
        entry.Port.Should().Be(4318);
        entry.Timestamp.Should().Be(timestamp);
    }
}

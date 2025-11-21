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
    [InlineData("dotnet.exe")]
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

}

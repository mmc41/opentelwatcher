using FluentAssertions;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Services;
using OpenTelWatcher.Services.Interfaces;
using Xunit;

namespace UnitTests.Services;

public class HealthMonitorTests
{
    [Fact]
    public void Status_Initially_IsHealthy()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };
        var monitor = new HealthMonitor(options);

        // Act & Assert
        monitor.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void ConsecutiveErrorCount_Initially_IsZero()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };
        var monitor = new HealthMonitor(options);

        // Act & Assert
        monitor.ConsecutiveErrorCount.Should().Be(0);
    }

    [Fact]
    public void RecordError_SingleError_IncrementsConsecutiveErrorCount()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };
        var monitor = new HealthMonitor(options);

        // Act
        monitor.RecordError("Test error 1");

        // Assert
        monitor.ConsecutiveErrorCount.Should().Be(1);
        monitor.Status.Should().Be(HealthStatus.Healthy); // Not degraded yet
    }

    [Fact]
    public void RecordError_ReachingThreshold_ChangesStatusToDegraded()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            MaxConsecutiveFileErrors = 3,
            MaxErrorHistorySize = 50
        };
        var monitor = new HealthMonitor(options);

        // Act
        monitor.RecordError("Error 1");
        monitor.RecordError("Error 2");
        monitor.RecordError("Error 3");

        // Assert
        monitor.ConsecutiveErrorCount.Should().Be(3);
        monitor.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public void RecordError_ExceedingThreshold_KeepsStatusDegraded()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            MaxConsecutiveFileErrors = 3,
            MaxErrorHistorySize = 50
        };
        var monitor = new HealthMonitor(options);

        // Act
        for (int i = 0; i < 5; i++)
        {
            monitor.RecordError($"Error {i}");
        }

        // Assert
        monitor.ConsecutiveErrorCount.Should().Be(5);
        monitor.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public void RecordSuccess_AfterError_ResetsConsecutiveErrorCount()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };
        var monitor = new HealthMonitor(options);

        monitor.RecordError("Error 1");
        monitor.RecordError("Error 2");

        // Act
        monitor.RecordSuccess();

        // Assert
        monitor.ConsecutiveErrorCount.Should().Be(0);
        monitor.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void RecordSuccess_AfterDegradation_RestoresHealthyStatus()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            MaxConsecutiveFileErrors = 2,
            MaxErrorHistorySize = 50
        };
        var monitor = new HealthMonitor(options);

        monitor.RecordError("Error 1");
        monitor.RecordError("Error 2"); // Should be degraded now

        // Act
        monitor.RecordSuccess();

        // Assert
        monitor.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void GetRecentErrors_WithNoErrors_ReturnsEmptyList()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };
        var monitor = new HealthMonitor(options);

        // Act
        var errors = monitor.GetRecentErrors();

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public void GetRecentErrors_WithMultipleErrors_ReturnsAllErrors()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };
        var monitor = new HealthMonitor(options);

        // Act
        monitor.RecordError("Error 1");
        monitor.RecordError("Error 2");
        monitor.RecordError("Error 3");

        var errors = monitor.GetRecentErrors();

        // Assert
        errors.Should().HaveCount(3);
        errors.Should().Contain("Error 1");
        errors.Should().Contain("Error 2");
        errors.Should().Contain("Error 3");
    }

    [Fact]
    public void GetRecentErrors_ExceedingMaxHistorySize_LimitsToMaxSize()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            MaxConsecutiveFileErrors = 100,
            MaxErrorHistorySize = 5
        };
        var monitor = new HealthMonitor(options);

        // Act - Record more errors than the max history size
        for (int i = 0; i < 10; i++)
        {
            monitor.RecordError($"Error {i}");
        }

        var errors = monitor.GetRecentErrors();

        // Assert
        errors.Should().HaveCount(5); // Limited to MaxErrorHistorySize
    }

    [Fact]
    public void GetRecentErrors_ExceedingMaxHistorySize_KeepsMostRecent()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            MaxConsecutiveFileErrors = 100,
            MaxErrorHistorySize = 3
        };
        var monitor = new HealthMonitor(options);

        // Act
        monitor.RecordError("Error 1");
        monitor.RecordError("Error 2");
        monitor.RecordError("Error 3");
        monitor.RecordError("Error 4");
        monitor.RecordError("Error 5");

        var errors = monitor.GetRecentErrors();

        // Assert
        errors.Should().HaveCount(3);
        errors.Should().Contain("Error 3");
        errors.Should().Contain("Error 4");
        errors.Should().Contain("Error 5");
    }

    [Fact]
    public void Reset_AfterErrors_ClearsAllState()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            MaxConsecutiveFileErrors = 2,
            MaxErrorHistorySize = 50
        };
        var monitor = new HealthMonitor(options);

        monitor.RecordError("Error 1");
        monitor.RecordError("Error 2");
        monitor.RecordError("Error 3");

        // Act
        monitor.Reset();

        // Assert
        monitor.Status.Should().Be(HealthStatus.Healthy);
        monitor.ConsecutiveErrorCount.Should().Be(0);
        monitor.GetRecentErrors().Should().BeEmpty();
    }
}

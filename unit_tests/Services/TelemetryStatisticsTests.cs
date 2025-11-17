using OpenTelWatcher.Services;
using FluentAssertions;
using Xunit;

namespace UnitTests.Services;

public class TelemetryStatisticsTests
{
    [Fact]
    public void IncrementTraces_IncreasesTracesReceivedCount()
    {
        // Arrange
        var stats = new TelemetryStatisticsService();

        // Act
        stats.IncrementTraces();
        stats.IncrementTraces();

        // Assert
        stats.TracesReceived.Should().Be(2);
    }

    [Fact]
    public void IncrementLogs_IncreasesLogsReceivedCount()
    {
        // Arrange
        var stats = new TelemetryStatisticsService();

        // Act
        stats.IncrementLogs();
        stats.IncrementLogs();
        stats.IncrementLogs();

        // Assert
        stats.LogsReceived.Should().Be(3);
    }

    [Fact]
    public void IncrementMetrics_IncreasesMetricsReceivedCount()
    {
        // Arrange
        var stats = new TelemetryStatisticsService();

        // Act
        stats.IncrementMetrics();

        // Assert
        stats.MetricsReceived.Should().Be(1);
    }

    [Fact]
    public async Task Statistics_AreThreadSafe()
    {
        // Arrange
        var stats = new TelemetryStatisticsService();

        // Act
        var tasks = Enumerable.Range(0, 1000)
            .Select(_ => Task.Run(() => stats.IncrementTraces()))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        stats.TracesReceived.Should().Be(1000);
    }
}

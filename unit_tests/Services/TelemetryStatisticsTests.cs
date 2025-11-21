using OpenTelWatcher.Services;
using FluentAssertions;
using Xunit;

namespace UnitTests.Services;

public class TelemetryStatisticsTests
{
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

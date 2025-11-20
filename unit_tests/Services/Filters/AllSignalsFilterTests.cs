using FluentAssertions;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Filters;
using Xunit;

namespace OpenTelWatcher.Tests.Services.Filters;

public class AllSignalsFilterTests
{
    private readonly AllSignalsFilter _filter = new();

    [Theory]
    [InlineData(SignalType.Traces)]
    [InlineData(SignalType.Logs)]
    [InlineData(SignalType.Metrics)]
    public void ShouldWrite_ReturnsTrue_ForAllSignalTypes(SignalType signal)
    {
        // Arrange
        var item = new TelemetryItem(signal, "{}\n", false, DateTimeOffset.UtcNow);

        // Act
        var result = _filter.ShouldWrite(item);

        // Assert
        result.Should().BeTrue();
    }
}

using FluentAssertions;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Filters;
using Xunit;

namespace OpenTelWatcher.Tests.Services.Filters;

public class ErrorsOnlyFilterTests
{
    private readonly ErrorsOnlyFilter _filter = new();

    [Theory]
    [InlineData(SignalType.Traces)]
    [InlineData(SignalType.Logs)]
    [InlineData(SignalType.Metrics)]
    public void ShouldWrite_WorksForAllSignalTypes_WhenError(SignalType signal)
    {
        // Arrange
        var item = new TelemetryItem(signal, "{}\n", IsError: true, DateTimeOffset.UtcNow);

        // Act
        var result = _filter.ShouldWrite(item);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(SignalType.Traces)]
    [InlineData(SignalType.Logs)]
    [InlineData(SignalType.Metrics)]
    public void ShouldWrite_WorksForAllSignalTypes_WhenNotError(SignalType signal)
    {
        // Arrange
        var item = new TelemetryItem(signal, "{}\n", IsError: false, DateTimeOffset.UtcNow);

        // Act
        var result = _filter.ShouldWrite(item);

        // Assert
        result.Should().BeFalse();
    }
}

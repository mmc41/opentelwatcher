using FluentAssertions;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Filters;
using Xunit;

namespace OpenTelWatcher.Tests.Services.Filters;

public class ErrorsOnlyFilterTests
{
    private readonly ErrorsOnlyFilter _filter = new();

    [Fact]
    public void ShouldWrite_ReturnsTrue_WhenItemIsError()
    {
        // Arrange
        var item = new TelemetryItem(
            Signal: SignalType.Traces,
            NdjsonLine: "{\"status\":{\"code\":2}}\n",
            IsError: true,
            Timestamp: DateTimeOffset.UtcNow);

        // Act
        var result = _filter.ShouldWrite(item);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldWrite_ReturnsFalse_WhenItemIsNotError()
    {
        // Arrange
        var item = new TelemetryItem(
            Signal: SignalType.Traces,
            NdjsonLine: "{\"status\":{\"code\":1}}\n",
            IsError: false,
            Timestamp: DateTimeOffset.UtcNow);

        // Act
        var result = _filter.ShouldWrite(item);

        // Assert
        result.Should().BeFalse();
    }

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

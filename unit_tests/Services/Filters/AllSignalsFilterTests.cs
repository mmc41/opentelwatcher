using FluentAssertions;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Filters;
using Xunit;

namespace OpenTelWatcher.Tests.Services.Filters;

public class AllSignalsFilterTests
{
    private readonly AllSignalsFilter _filter = new();

    [Fact]
    public void ShouldWrite_ReturnsTrue_ForNonErrorTraces()
    {
        // Arrange
        var item = new TelemetryItem(
            Signal: SignalType.Traces,
            NdjsonLine: "{\"traceId\":\"123\"}\n",
            IsError: false,
            Timestamp: DateTimeOffset.UtcNow);

        // Act
        var result = _filter.ShouldWrite(item);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldWrite_ReturnsTrue_ForErrorLogs()
    {
        // Arrange
        var item = new TelemetryItem(
            Signal: SignalType.Logs,
            NdjsonLine: "{\"severityNumber\":17}\n",
            IsError: true,
            Timestamp: DateTimeOffset.UtcNow);

        // Act
        var result = _filter.ShouldWrite(item);

        // Assert
        result.Should().BeTrue();
    }

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

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
    public void ShouldWrite_ReturnsTrue_ForAnySignal()
    {
        // Arrange - test with one representative signal type
        var item = new TelemetryItem(SignalType.Traces, "{}\n", false, DateTimeOffset.UtcNow);

        // Act
        var result = _filter.ShouldWrite(item);

        // Assert
        result.Should().BeTrue("AllSignalsFilter returns true for all signals by definition");
    }
}

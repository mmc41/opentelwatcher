using OpenTelWatcher.Utilities;
using FluentAssertions;
using Xunit;

namespace UnitTests.Utilities;

public class UptimeFormatterTests
{
    [Theory]
    [InlineData(5, "5s")]
    [InlineData(90, "1m 30s")]
    [InlineData(3665, "1h 1m 5s")]
    [InlineData(90061, "1d 1h 1m")]
    public void FormatUptime_ReturnsExpectedFormat(int totalSeconds, string expected)
    {
        // Arrange
        var uptime = TimeSpan.FromSeconds(totalSeconds);

        // Act
        var result = UptimeFormatter.FormatUptime(uptime);

        // Assert
        result.Should().Be(expected);
    }
}

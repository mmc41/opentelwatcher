using OpenTelWatcher.Utilities;
using FluentAssertions;
using Xunit;

namespace OpenTelWatcher.Tests.Utilities;

public class NumberFormatterTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1000, "1.0K")]
    [InlineData(1234, "1.2K")]
    [InlineData(999999, "1000.0K")]
    [InlineData(1000000, "1.0M")]
    [InlineData(1234567, "1.2M")]
    public void FormatCount_ReturnsExpectedFormat(long input, string expected)
    {
        // Act
        var result = NumberFormatter.FormatCount(input);

        // Assert
        result.Should().Be(expected);
    }
}

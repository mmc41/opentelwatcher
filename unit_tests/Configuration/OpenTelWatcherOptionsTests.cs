using FluentAssertions;
using OpenTelWatcher.Configuration;
using Xunit;

namespace UnitTests.Configuration;

public class OpenTelWatcherOptionsTests
{
    [Fact]
    public void DefaultValues_ShouldBeSetCorrectly()
    {
        // Arrange & Act
        var options = new OpenTelWatcherOptions();

        // Assert
        options.OutputDirectory.Should().Be("./telemetry-data");
        options.MaxFileSizeMB.Should().Be(100);
        options.PrettyPrint.Should().BeFalse();
        options.MaxErrorHistorySize.Should().Be(50);
        options.MaxConsecutiveFileErrors.Should().Be(10);
        options.RequestTimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void Properties_ShouldBeSettable()
    {
        // Arrange
        var options = new OpenTelWatcherOptions();

        // Act
        options.OutputDirectory = "./custom-output";
        options.MaxFileSizeMB = 200;
        options.PrettyPrint = true;
        options.MaxErrorHistorySize = 100;
        options.MaxConsecutiveFileErrors = 20;
        options.RequestTimeoutSeconds = 60;

        // Assert
        options.OutputDirectory.Should().Be("./custom-output");
        options.MaxFileSizeMB.Should().Be(200);
        options.PrettyPrint.Should().BeTrue();
        options.MaxErrorHistorySize.Should().Be(100);
        options.MaxConsecutiveFileErrors.Should().Be(20);
        options.RequestTimeoutSeconds.Should().Be(60);
    }
}

using FluentAssertions;
using OpenTelWatcher.Serialization;
using System.Text.Json;
using Xunit;

namespace OpenTelWatcher.Tests.Serialization;

public class NdjsonWriterTests
{
    [Fact]
    public void FormatAsNdjsonLine_WithValidJson_AppendsNewline()
    {
        // Arrange
        var json = """{"test": "value"}""";

        // Act
        var ndjsonLine = NdjsonWriter.FormatAsNdjsonLine(json);

        // Assert
        ndjsonLine.Should().EndWith("\n");
        ndjsonLine.Should().StartWith("{");
    }

    [Fact]
    public void FormatAsNdjsonLine_WithValidJson_DoesNotAddExtraNewlines()
    {
        // Arrange
        var json = """{"test": "value"}""";

        // Act
        var ndjsonLine = NdjsonWriter.FormatAsNdjsonLine(json);

        // Assert
        // Should have exactly one newline at the end
        ndjsonLine.Should().Be(json + "\n");
        ndjsonLine.Count(c => c == '\n').Should().Be(1);
    }

    [Fact]
    public void FormatAsNdjsonLine_WithJsonContainingEmbeddedNewlines_PreservesStructure()
    {
        // Arrange - pretty-printed JSON with newlines
        var json = """
{
  "test": "value"
}
""".Trim();

        // Act
        var ndjsonLine = NdjsonWriter.FormatAsNdjsonLine(json);

        // Assert
        ndjsonLine.Should().EndWith("\n");
        // Should preserve internal structure but still be valid
        var parsed = JsonDocument.Parse(ndjsonLine.TrimEnd('\n'));
        parsed.Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FormatAsNdjsonLine_WithInvalidInput_ThrowsArgumentException(string? invalidJson)
    {
        // Act & Assert
        var act = () => NdjsonWriter.FormatAsNdjsonLine(invalidJson!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FormatAsNdjsonLine_PreservesUtf8Characters()
    {
        // Arrange
        var json = """{"name": "Test™", "emoji": "🎯"}""";

        // Act
        var ndjsonLine = NdjsonWriter.FormatAsNdjsonLine(json);

        // Assert
        ndjsonLine.Should().Contain("™");
        ndjsonLine.Should().Contain("🎯");
        ndjsonLine.Should().EndWith("\n");
    }
}

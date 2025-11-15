using FluentAssertions;
using OpenTelWatcher.Services;
using Xunit;

namespace OpenTelWatcher.Tests.Services;

public class FileRotationServiceTests
{
    [Fact]
    public void GenerateNewFilePath_WithValidInputs_ReturnsCorrectFormat()
    {
        // Arrange
        var service = new FileRotationService();
        var outputDir = Path.Combine(Path.GetTempPath(), "telemetry-test");
        var signal = "traces";

        // Act
        var filePath = service.GenerateNewFilePath(outputDir, signal);

        // Assert
        filePath.Should().StartWith(outputDir);
        filePath.Should().EndWith(".ndjson");
        filePath.Should().Contain("traces.");

        // Verify timestamp format: traces.yyyyMMdd_HHmmss_fff.ndjson
        var fileName = Path.GetFileName(filePath);
        fileName.Should().MatchRegex(@"traces\.\d{8}_\d{6}_\d{3}\.ndjson");
    }

    [Theory]
    [InlineData("traces")]
    [InlineData("logs")]
    [InlineData("metrics")]
    public void GenerateNewFilePath_WithDifferentSignals_IncludesSignalInFileName(string signal)
    {
        // Arrange
        var service = new FileRotationService();
        var outputDir = Path.GetTempPath();

        // Act
        var filePath = service.GenerateNewFilePath(outputDir, signal);

        // Assert
        var fileName = Path.GetFileName(filePath);
        fileName.Should().StartWith(signal + ".");
    }

    [Fact]
    public void ShouldRotate_WithNonExistentFile_ReturnsFalse()
    {
        // Arrange
        var service = new FileRotationService();
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "non-existent-file.ndjson");

        // Act
        var shouldRotate = service.ShouldRotate(nonExistentPath, maxFileSizeMB: 100);

        // Assert
        shouldRotate.Should().BeFalse();
    }

    [Fact]
    public void ShouldRotate_WithSmallFile_ReturnsFalse()
    {
        // Arrange
        var service = new FileRotationService();
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "small content");

            // Act
            var shouldRotate = service.ShouldRotate(tempFile, maxFileSizeMB: 100);

            // Assert
            shouldRotate.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ShouldRotate_WithLargeFile_ReturnsTrue()
    {
        // Arrange
        var service = new FileRotationService();
        var tempFile = Path.GetTempFileName();
        try
        {
            // Create a file larger than 1MB (1MB = 1048576 bytes)
            var largeContent = new string('x', 1048576 + 100);
            File.WriteAllText(tempFile, largeContent);

            // Act
            var shouldRotate = service.ShouldRotate(tempFile, maxFileSizeMB: 1);

            // Assert
            shouldRotate.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetOrCreateFilePath_FirstCall_CreatesNewFile()
    {
        // Arrange
        var service = new FileRotationService();
        var outputDir = Path.Combine(Path.GetTempPath(), "telemetry-test-" + Guid.NewGuid());
        var signal = "traces";

        try
        {
            // Act
            var filePath = service.GetOrCreateFilePath(outputDir, signal);

            // Assert
            filePath.Should().StartWith(outputDir);
            filePath.Should().EndWith(".ndjson");
            Directory.Exists(outputDir).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public void GetOrCreateFilePath_MultipleCallsSameSignal_ReturnsSamePath()
    {
        // Arrange
        var service = new FileRotationService();
        var outputDir = Path.Combine(Path.GetTempPath(), "telemetry-test-" + Guid.NewGuid());
        var signal = "traces";

        try
        {
            // Act
            var filePath1 = service.GetOrCreateFilePath(outputDir, signal);
            var filePath2 = service.GetOrCreateFilePath(outputDir, signal);

            // Assert
            filePath1.Should().Be(filePath2);
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public void GetOrCreateFilePath_DifferentSignals_ReturnsDifferentPaths()
    {
        // Arrange
        var service = new FileRotationService();
        var outputDir = Path.Combine(Path.GetTempPath(), "telemetry-test-" + Guid.NewGuid());

        try
        {
            // Act
            var tracesPath = service.GetOrCreateFilePath(outputDir, "traces");
            var logsPath = service.GetOrCreateFilePath(outputDir, "logs");
            var metricsPath = service.GetOrCreateFilePath(outputDir, "metrics");

            // Assert
            tracesPath.Should().NotBe(logsPath);
            logsPath.Should().NotBe(metricsPath);
            tracesPath.Should().NotBe(metricsPath);

            tracesPath.Should().Contain("traces.");
            logsPath.Should().Contain("logs.");
            metricsPath.Should().Contain("metrics.");
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }
}

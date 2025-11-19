using FluentAssertions;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Services;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Services;

public class FileRotationServiceTests
{
    [Fact]
    public void GenerateNewFilePath_WithValidInputs_ReturnsCorrectFormat()
    {
        // Arrange
        var service = new FileRotationService(new MockTimeProvider());
        var outputDir = Path.Combine(Path.GetTempPath(), "telemetry-test");
        var signal = SignalType.Traces;

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
    [InlineData(SignalType.Traces)]
    [InlineData(SignalType.Logs)]
    [InlineData(SignalType.Metrics)]
    public void GenerateNewFilePath_WithDifferentSignals_IncludesSignalInFileName(SignalType signal)
    {
        // Arrange
        var service = new FileRotationService(new MockTimeProvider());
        var outputDir = Path.GetTempPath();

        // Act
        var filePath = service.GenerateNewFilePath(outputDir, signal);

        // Assert
        var fileName = Path.GetFileName(filePath);
        fileName.Should().StartWith(signal.ToLowerString() + ".");
    }

    [Fact]
    public void ShouldRotate_WithNonExistentFile_ReturnsFalse()
    {
        // Arrange
        var service = new FileRotationService(new MockTimeProvider());
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
        var service = new FileRotationService(new MockTimeProvider());
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
        var service = new FileRotationService(new MockTimeProvider());
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
        var service = new FileRotationService(new MockTimeProvider());
        var outputDir = Path.Combine(Path.GetTempPath(), "telemetry-test-" + Guid.NewGuid());
        var signal = SignalType.Traces;

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
        var service = new FileRotationService(new MockTimeProvider());
        var outputDir = Path.Combine(Path.GetTempPath(), "telemetry-test-" + Guid.NewGuid());
        var signal = SignalType.Traces;

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
        var service = new FileRotationService(new MockTimeProvider());
        var outputDir = Path.Combine(Path.GetTempPath(), "telemetry-test-" + Guid.NewGuid());

        try
        {
            // Act
            var tracesPath = service.GetOrCreateFilePath(outputDir, SignalType.Traces);
            var logsPath = service.GetOrCreateFilePath(outputDir, SignalType.Logs);
            var metricsPath = service.GetOrCreateFilePath(outputDir, SignalType.Metrics);

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

    #region RotateFile Tests

    [Fact]
    public void RotateFile_FirstCall_ReturnsNewFilePath()
    {
        // Arrange
        var service = new FileRotationService(new MockTimeProvider());
        var outputDir = Path.Combine(Path.GetTempPath(), "telemetry-test-" + Guid.NewGuid());
        var signal = SignalType.Traces;

        try
        {
            // Act
            var filePath = service.RotateFile(outputDir, signal);

            // Assert
            filePath.Should().StartWith(outputDir);
            filePath.Should().EndWith(".ndjson");
            filePath.Should().Contain("traces.");
            Directory.Exists(outputDir).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public void RotateFile_MultipleCallsSameSignal_ReturnsDifferentPaths()
    {
        // Arrange
        var timeProvider = new MockTimeProvider();
        var service = new FileRotationService(timeProvider);
        var outputDir = Path.Combine(Path.GetTempPath(), "telemetry-test-" + Guid.NewGuid());
        var signal = SignalType.Traces;

        try
        {
            // Act
            var filePath1 = service.RotateFile(outputDir, signal);

            // Advance time to ensure different timestamp
            timeProvider.AdvanceTime(TimeSpan.FromMilliseconds(1));

            var filePath2 = service.RotateFile(outputDir, signal);

            // Assert
            filePath1.Should().NotBe(filePath2);
            filePath1.Should().Contain("traces.");
            filePath2.Should().Contain("traces.");
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public void RotateFile_CreatesOutputDirectory()
    {
        // Arrange
        var service = new FileRotationService(new MockTimeProvider());
        var outputDir = Path.Combine(Path.GetTempPath(), "telemetry-test-" + Guid.NewGuid());
        var signal = SignalType.Logs;

        try
        {
            // Verify directory doesn't exist before rotation
            Directory.Exists(outputDir).Should().BeFalse();

            // Act
            var filePath = service.RotateFile(outputDir, signal);

            // Assert
            Directory.Exists(outputDir).Should().BeTrue();
            filePath.Should().StartWith(outputDir);
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public void RotateFile_UpdatesCachedFilePath()
    {
        // Arrange
        var timeProvider = new MockTimeProvider();
        var service = new FileRotationService(timeProvider);
        var outputDir = Path.Combine(Path.GetTempPath(), "telemetry-test-" + Guid.NewGuid());
        var signal = SignalType.Metrics;

        try
        {
            // Act
            var initialPath = service.GetOrCreateFilePath(outputDir, signal);

            // Advance time and rotate
            timeProvider.AdvanceTime(TimeSpan.FromSeconds(1));
            var rotatedPath = service.RotateFile(outputDir, signal);

            // Get the cached path again
            var cachedPath = service.GetOrCreateFilePath(outputDir, signal);

            // Assert
            cachedPath.Should().Be(rotatedPath);
            cachedPath.Should().NotBe(initialPath);
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task RotateFile_ConcurrentCalls_ThreadSafe()
    {
        // Arrange
        var service = new FileRotationService(new MockTimeProvider());
        var outputDir = Path.Combine(Path.GetTempPath(), "telemetry-test-" + Guid.NewGuid());
        var signal = SignalType.Traces;

        try
        {
            // Act - Call RotateFile concurrently from multiple threads
            var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
            {
                return service.RotateFile(outputDir, signal);
            })).ToArray();

            var results = await Task.WhenAll(tasks);

            // Assert - All calls should complete without exception
            results.Should().HaveCount(10);
            results.Should().OnlyContain(path => path.Contains(signal.ToLowerString()));

            // All threads should have gotten valid paths (semaphore protected the operation)
            results.Should().OnlyContain(path => !string.IsNullOrEmpty(path));
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var service = new FileRotationService(new MockTimeProvider());
        var outputDir = Path.Combine(Path.GetTempPath(), "telemetry-test-" + Guid.NewGuid());

        try
        {
            // Create some semaphores by rotating files
            service.RotateFile(outputDir, SignalType.Traces);
            service.RotateFile(outputDir, SignalType.Logs);

            // Act - Dispose multiple times
            service.Dispose();
            service.Dispose(); // Should not throw

            // Assert - No exception thrown
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    #endregion
}

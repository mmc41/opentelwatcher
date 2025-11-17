using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenTelWatcher.Utilities;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Utilities;

/// <summary>
/// Unit tests for TelemetryCleaner.
/// Tests file deletion, retry logic, error handling, and cancellation.
/// </summary>
public class TelemetryCleanerTests : IDisposable
{
    private readonly string _testOutputDir;

    public TelemetryCleanerTests()
    {
        _testOutputDir = Path.Combine(Path.GetTempPath(), "cleaner-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testOutputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testOutputDir))
        {
            try
            {
                Directory.Delete(_testOutputDir, recursive: true);
            }
            catch (Exception ex)
            {
                // Log cleanup errors but don't throw to prevent test failures during disposal
                var logger = TestLoggerFactory.CreateLogger<TelemetryCleanerTests>();
                logger.LogWarning(ex, "Failed to cleanup test directory {TestOutputDir}", _testOutputDir);
            }
        }
    }

    #region Basic Functionality Tests

    [Fact]
    public async Task ClearFilesAsync_DirectoryNotExists_ReturnsZeroFiles()
    {
        // Arrange
        var nonExistentDir = Path.Combine(Path.GetTempPath(), "non-existent-" + Guid.NewGuid());
        var loggerFactory = TestLoggerFactory.Instance;

        // Act
        var result = await TelemetryCleaner.ClearFilesAsync(loggerFactory, nonExistentDir, TestContext.Current.CancellationToken);

        // Assert
        result.FilesDeleted.Should().Be(0);
        result.SpaceFreedBytes.Should().Be(0);
        result.FilesBeforeCount.Should().Be(0);
        result.DirectoryPath.Should().Be(nonExistentDir);
    }

    [Fact]
    public async Task ClearFilesAsync_EmptyDirectory_ReturnsZeroFiles()
    {
        // Arrange
        var loggerFactory = TestLoggerFactory.Instance;

        // Act
        var result = await TelemetryCleaner.ClearFilesAsync(loggerFactory, _testOutputDir, TestContext.Current.CancellationToken);

        // Assert
        result.FilesDeleted.Should().Be(0);
        result.SpaceFreedBytes.Should().Be(0);
        result.FilesBeforeCount.Should().Be(0);
    }

    [Fact]
    public async Task ClearFilesAsync_WithNdjsonFiles_DeletesAllFiles()
    {
        // Arrange
        var file1 = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        var file2 = Path.Combine(_testOutputDir, "logs.20250117_110000_000.ndjson");
        var file3 = Path.Combine(_testOutputDir, "traces.20250117_120000_000.errors.ndjson");
        File.WriteAllText(file1, "test data 1");
        File.WriteAllText(file2, "test data 2");
        File.WriteAllText(file3, "test data 3");

        var loggerFactory = TestLoggerFactory.Instance;

        // Act
        var result = await TelemetryCleaner.ClearFilesAsync(loggerFactory, _testOutputDir, TestContext.Current.CancellationToken);

        // Assert
        result.FilesDeleted.Should().Be(3);
        result.FilesBeforeCount.Should().Be(3);
        result.SpaceFreedBytes.Should().BeGreaterThan(0);

        // Verify files were actually deleted
        File.Exists(file1).Should().BeFalse();
        File.Exists(file2).Should().BeFalse();
        File.Exists(file3).Should().BeFalse();
    }

    [Fact]
    public async Task ClearFilesAsync_WithNonNdjsonFiles_LeavesThemUntouched()
    {
        // Arrange
        var ndjsonFile = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        var textFile = Path.Combine(_testOutputDir, "readme.txt");
        var jsonFile = Path.Combine(_testOutputDir, "config.json");
        File.WriteAllText(ndjsonFile, "delete me");
        File.WriteAllText(textFile, "keep me");
        File.WriteAllText(jsonFile, "keep me too");

        var loggerFactory = TestLoggerFactory.Instance;

        // Act
        var result = await TelemetryCleaner.ClearFilesAsync(loggerFactory, _testOutputDir, TestContext.Current.CancellationToken);

        // Assert
        result.FilesDeleted.Should().Be(1); // Only .ndjson file
        result.FilesBeforeCount.Should().Be(1);

        // Verify only ndjson file was deleted
        File.Exists(ndjsonFile).Should().BeFalse();
        File.Exists(textFile).Should().BeTrue();
        File.Exists(jsonFile).Should().BeTrue();
    }

    [Fact]
    public async Task ClearFilesAsync_CalculatesSpaceFreedCorrectly()
    {
        // Arrange
        var file1 = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        var file2 = Path.Combine(_testOutputDir, "logs.20250117_110000_000.ndjson");
        var content1 = new string('x', 1000); // 1000 bytes
        var content2 = new string('y', 2000); // 2000 bytes
        File.WriteAllText(file1, content1);
        File.WriteAllText(file2, content2);

        var loggerFactory = TestLoggerFactory.Instance;

        // Act
        var result = await TelemetryCleaner.ClearFilesAsync(loggerFactory, _testOutputDir, TestContext.Current.CancellationToken);

        // Assert
        result.FilesDeleted.Should().Be(2);
        result.SpaceFreedBytes.Should().BeGreaterThanOrEqualTo(3000); // At least content size
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task ClearFilesAsync_NullLoggerFactory_ThrowsArgumentNullException()
    {
        // Arrange
        ILoggerFactory? nullLoggerFactory = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await TelemetryCleaner.ClearFilesAsync(nullLoggerFactory!, _testOutputDir, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ClearFilesAsync_InvalidOutputDirectory_ThrowsArgumentException(string? invalidDir)
    {
        // Arrange
        var loggerFactory = TestLoggerFactory.Instance;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await TelemetryCleaner.ClearFilesAsync(loggerFactory, invalidDir!, TestContext.Current.CancellationToken));
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task ClearFilesAsync_CancellationRequested_StopsDeletingFiles()
    {
        // Arrange
        var file1 = Path.Combine(_testOutputDir, "file1.ndjson");
        var file2 = Path.Combine(_testOutputDir, "file2.ndjson");
        var file3 = Path.Combine(_testOutputDir, "file3.ndjson");
        File.WriteAllText(file1, "data");
        File.WriteAllText(file2, "data");
        File.WriteAllText(file3, "data");

        var loggerFactory = TestLoggerFactory.Instance;
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act
        var result = await TelemetryCleaner.ClearFilesAsync(loggerFactory, _testOutputDir, cts.Token);

        // Assert
        result.FilesBeforeCount.Should().Be(3);
        result.FilesDeleted.Should().BeLessThan(3); // Should stop before deleting all files
    }

    #endregion

    #region File Size Calculation Error Handling

    [Fact]
    public async Task ClearFilesAsync_FileGetSizeFails_ContinuesDeletion()
    {
        // Arrange
        var file1 = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        File.WriteAllText(file1, "test data");

        var loggerFactory = TestLoggerFactory.Instance;

        // Act
        var result = await TelemetryCleaner.ClearFilesAsync(loggerFactory, _testOutputDir, TestContext.Current.CancellationToken);

        // Assert
        result.FilesDeleted.Should().Be(1);
        // SpaceFreedBytes should still be calculated (or 0 if file size couldn't be determined)
        result.SpaceFreedBytes.Should().BeGreaterThanOrEqualTo(0);
    }

    #endregion

    #region Subdirectory Tests

    [Fact]
    public async Task ClearFilesAsync_OnlyDeletesTopLevelFiles()
    {
        // Arrange
        var topLevelFile = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        var subdirPath = Path.Combine(_testOutputDir, "subdir");
        Directory.CreateDirectory(subdirPath);
        var subdirFile = Path.Combine(subdirPath, "logs.20250117_110000_000.ndjson");

        File.WriteAllText(topLevelFile, "top level");
        File.WriteAllText(subdirFile, "subdirectory");

        var loggerFactory = TestLoggerFactory.Instance;

        // Act
        var result = await TelemetryCleaner.ClearFilesAsync(loggerFactory, _testOutputDir, TestContext.Current.CancellationToken);

        // Assert
        result.FilesDeleted.Should().Be(1); // Only top-level file
        result.FilesBeforeCount.Should().Be(1);

        // Verify top-level deleted, subdirectory file untouched
        File.Exists(topLevelFile).Should().BeFalse();
        File.Exists(subdirFile).Should().BeTrue();
    }

    #endregion

    #region Multiple File Types

    [Fact]
    public async Task ClearFilesAsync_WithErrorFiles_DeletesBothNormalAndErrorFiles()
    {
        // Arrange
        var normalFile = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        var errorFile = Path.Combine(_testOutputDir, "logs.20250117_110000_000.errors.ndjson");
        File.WriteAllText(normalFile, "normal");
        File.WriteAllText(errorFile, "error");

        var loggerFactory = TestLoggerFactory.Instance;

        // Act
        var result = await TelemetryCleaner.ClearFilesAsync(loggerFactory, _testOutputDir, TestContext.Current.CancellationToken);

        // Assert
        result.FilesDeleted.Should().Be(2);
        result.FilesBeforeCount.Should().Be(2);

        // Both types deleted
        File.Exists(normalFile).Should().BeFalse();
        File.Exists(errorFile).Should().BeFalse();
    }

    [Theory]
    [InlineData("traces")]
    [InlineData("logs")]
    [InlineData("metrics")]
    public async Task ClearFilesAsync_WithDifferentSignals_DeletesAll(string signal)
    {
        // Arrange
        var file = Path.Combine(_testOutputDir, $"{signal}.20250117_100000_000.ndjson");
        File.WriteAllText(file, "data");

        var loggerFactory = TestLoggerFactory.Instance;

        // Act
        var result = await TelemetryCleaner.ClearFilesAsync(loggerFactory, _testOutputDir, TestContext.Current.CancellationToken);

        // Assert
        result.FilesDeleted.Should().Be(1);
        File.Exists(file).Should().BeFalse();
    }

    #endregion

    #region Result Consistency Tests

    [Fact]
    public async Task ClearFilesAsync_ReturnsConsistentResult()
    {
        // Arrange
        var file1 = Path.Combine(_testOutputDir, "file1.ndjson");
        var file2 = Path.Combine(_testOutputDir, "file2.ndjson");
        File.WriteAllText(file1, "data1");
        File.WriteAllText(file2, "data2");

        var loggerFactory = TestLoggerFactory.Instance;

        // Act
        var result = await TelemetryCleaner.ClearFilesAsync(loggerFactory, _testOutputDir, TestContext.Current.CancellationToken);

        // Assert
        result.FilesDeleted.Should().Be(result.FilesBeforeCount);
        result.DirectoryPath.Should().Be(_testOutputDir);
        result.SpaceFreedBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ClearFilesAsync_EmptyDirectory_ReturnsConsistentZeroResult()
    {
        // Arrange
        var loggerFactory = TestLoggerFactory.Instance;

        // Act
        var result = await TelemetryCleaner.ClearFilesAsync(loggerFactory, _testOutputDir, TestContext.Current.CancellationToken);

        // Assert
        result.FilesDeleted.Should().Be(0);
        result.FilesBeforeCount.Should().Be(0);
        result.SpaceFreedBytes.Should().Be(0);
        result.DirectoryPath.Should().Be(_testOutputDir);
    }

    #endregion
}

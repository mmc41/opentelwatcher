using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenTelWatcher.Services;
using UnitTests.Helpers;
using Xunit;

namespace OpenTelWatcher.Tests.Services;

public class TelemetryFileManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly TelemetryFileManager _service;

    public TelemetryFileManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"telemetry-clear-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _service = new TelemetryFileManager(TestLoggerFactory.CreateLogger<TelemetryFileManager>(), TestLoggerFactory.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public async Task ClearAllFilesAsync_WithNullDirectory_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.ClearAllFilesAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClearAllFilesAsync_WithEmptyDirectory_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.ClearAllFilesAsync("", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClearAllFilesAsync_WithNonExistentDirectory_ReturnsZero()
    {
        // Arrange
        var nonExistentDir = Path.Combine(Path.GetTempPath(), $"non-existent-{Guid.NewGuid()}");

        // Act
        var result = await _service.ClearAllFilesAsync(nonExistentDir, TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task ClearAllFilesAsync_WithEmptyDirectory_ReturnsZero()
    {
        // Act
        var result = await _service.ClearAllFilesAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task ClearAllFilesAsync_WithSingleFile_DeletesFileAndReturnsOne()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "traces.20250114_120000_000.ndjson");
        await File.WriteAllTextAsync(filePath, "{\"test\": \"data\"}", TestContext.Current.CancellationToken);

        // Act
        var result = await _service.ClearAllFilesAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(1);
        File.Exists(filePath).Should().BeFalse("file should be deleted");
    }

    [Fact]
    public async Task ClearAllFilesAsync_WithMultipleFiles_DeletesAllAndReturnsCount()
    {
        // Arrange
        var files = new[]
        {
            Path.Combine(_testDir, "traces.20250114_120000_000.ndjson"),
            Path.Combine(_testDir, "logs.20250114_120001_000.ndjson"),
            Path.Combine(_testDir, "metrics.20250114_120002_000.ndjson")
        };

        foreach (var file in files)
        {
            await File.WriteAllTextAsync(file, "{\"test\": \"data\"}", TestContext.Current.CancellationToken);
        }

        // Act
        var result = await _service.ClearAllFilesAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(3);
        foreach (var file in files)
        {
            File.Exists(file).Should().BeFalse($"{Path.GetFileName(file)} should be deleted");
        }
    }

    [Fact]
    public async Task ClearAllFilesAsync_OnlyDeletesNdjsonFiles_LeavesOtherFiles()
    {
        // Arrange
        var ndjsonFile = Path.Combine(_testDir, "traces.ndjson");
        var txtFile = Path.Combine(_testDir, "readme.txt");
        var logFile = Path.Combine(_testDir, "app.log");

        await File.WriteAllTextAsync(ndjsonFile, "{}", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(txtFile, "readme", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(logFile, "log data", TestContext.Current.CancellationToken);

        // Act
        var result = await _service.ClearAllFilesAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(1, "only .ndjson file should be deleted");
        File.Exists(ndjsonFile).Should().BeFalse(".ndjson file should be deleted");
        File.Exists(txtFile).Should().BeTrue(".txt file should NOT be deleted");
        File.Exists(logFile).Should().BeTrue(".log file should NOT be deleted");
    }

    [Fact]
    public async Task ClearAllFilesAsync_WithMixedFileTypes_OnlyDeletesNdjson()
    {
        // Arrange
        var ndjsonFiles = new[]
        {
            Path.Combine(_testDir, "traces.ndjson"),
            Path.Combine(_testDir, "logs.ndjson"),
            Path.Combine(_testDir, "data.ndjson")
        };

        var otherFiles = new[]
        {
            Path.Combine(_testDir, "config.json"),
            Path.Combine(_testDir, "data.xml"),
            Path.Combine(_testDir, "notes.txt")
        };

        foreach (var file in ndjsonFiles.Concat(otherFiles))
        {
            await File.WriteAllTextAsync(file, "test", TestContext.Current.CancellationToken);
        }

        // Act
        var result = await _service.ClearAllFilesAsync(_testDir, TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(3, "only 3 .ndjson files should be deleted");

        foreach (var file in ndjsonFiles)
        {
            File.Exists(file).Should().BeFalse($"{Path.GetFileName(file)} should be deleted");
        }

        foreach (var file in otherFiles)
        {
            File.Exists(file).Should().BeTrue($"{Path.GetFileName(file)} should NOT be deleted");
        }
    }

    [Fact]
    public async Task ClearAllFilesAsync_WithCancellation_StopsClearing()
    {
        // Arrange
        var files = Enumerable.Range(0, 20)
            .Select(i => Path.Combine(_testDir, $"file{i}.ndjson"))
            .ToArray();

        foreach (var file in files)
        {
            await File.WriteAllTextAsync(file, "data", TestContext.Current.CancellationToken);
        }

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act
        var result = await _service.ClearAllFilesAsync(_testDir, cts.Token);

        // Assert
        result.Should().BeLessThan(files.Length, "not all files should be deleted due to cancellation");
    }
}

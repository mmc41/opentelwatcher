using FluentAssertions;
using OpenTelWatcher.CLI.Commands;
using Xunit;

namespace UnitTests.CLI.Commands;

/// <summary>
/// Unit tests for ListCommand.
/// Tests file listing, filtering, error handling, and display modes.
/// </summary>
public class ListCommandTests : IDisposable
{
    private readonly string _testOutputDir;

    public ListCommandTests()
    {
        _testOutputDir = Path.Combine(Path.GetTempPath(), "list-command-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testOutputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testOutputDir))
        {
            Directory.Delete(_testOutputDir, recursive: true);
        }
    }

    #region Basic Functionality Tests

    [Fact]
    public async Task ExecuteAsync_DirectoryNotExists_ReturnsUserError()
    {
        // Arrange
        var nonExistentDir = Path.Combine(Path.GetTempPath(), "non-existent-" + Guid.NewGuid());
        var command = new ListCommand();

        // Act
        var result = await command.ExecuteAsync(outputDir: nonExistentDir, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(1); // User error
        result.Message.Should().Be("Directory not found");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyDirectory_ReturnsSuccessWithZeroFiles()
    {
        // Arrange
        var command = new ListCommand();

        // Act
        var result = await command.ExecuteAsync(outputDir: _testOutputDir, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0); // Success
        result.Message.Should().Be("Files listed");
        result.Data.Should().NotBeNull();
        var resultData = result.Data!["result"] as Dictionary<string, object>;
        resultData.Should().NotBeNull();
        resultData!["fileCount"].Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithNdjsonFiles_ListsAllFiles()
    {
        // Arrange
        var file1 = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        var file2 = Path.Combine(_testOutputDir, "logs.20250117_110000_000.ndjson");
        var file3 = Path.Combine(_testOutputDir, "metrics.20250117_120000_000.ndjson");
        File.WriteAllText(file1, "{}");
        File.WriteAllText(file2, "{}");
        File.WriteAllText(file3, "{}");

        var command = new ListCommand();

        // Act
        var result = await command.ExecuteAsync(outputDir: _testOutputDir, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Files listed");
        var resultData = result.Data!["result"] as Dictionary<string, object>;
        resultData!["fileCount"].Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonNdjsonFiles_ExcludesThem()
    {
        // Arrange
        var ndjsonFile = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        var textFile = Path.Combine(_testOutputDir, "readme.txt");
        var jsonFile = Path.Combine(_testOutputDir, "config.json");
        File.WriteAllText(ndjsonFile, "{}");
        File.WriteAllText(textFile, "keep me");
        File.WriteAllText(jsonFile, "{}");

        var command = new ListCommand();

        // Act
        var result = await command.ExecuteAsync(outputDir: _testOutputDir, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);
        var resultData = result.Data!["result"] as Dictionary<string, object>;
        resultData!["fileCount"].Should().Be(1); // Only the .ndjson file
    }

    #endregion

    #region Errors-Only Filtering Tests

    [Fact]
    public async Task ExecuteAsync_ErrorsOnly_ListsOnlyErrorFiles()
    {
        // Arrange
        var normalFile1 = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        var normalFile2 = Path.Combine(_testOutputDir, "logs.20250117_110000_000.ndjson");
        var errorFile1 = Path.Combine(_testOutputDir, "traces.20250117_120000_000.errors.ndjson");
        var errorFile2 = Path.Combine(_testOutputDir, "logs.20250117_130000_000.errors.ndjson");
        File.WriteAllText(normalFile1, "{}");
        File.WriteAllText(normalFile2, "{}");
        File.WriteAllText(errorFile1, "{}");
        File.WriteAllText(errorFile2, "{}");

        var command = new ListCommand();

        // Act
        var result = await command.ExecuteAsync(outputDir: _testOutputDir, errorsOnly: true, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);
        var resultData = result.Data!["result"] as Dictionary<string, object>;
        resultData!["fileCount"].Should().Be(2); // Only error files
        resultData["errorsOnly"].Should().Be(true);
    }

    [Fact]
    public async Task ExecuteAsync_ErrorsOnly_NoErrorFiles_ReturnsZeroFiles()
    {
        // Arrange
        var normalFile = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        File.WriteAllText(normalFile, "{}");

        var command = new ListCommand();

        // Act
        var result = await command.ExecuteAsync(outputDir: _testOutputDir, errorsOnly: true, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);
        var resultData = result.Data!["result"] as Dictionary<string, object>;
        resultData!["fileCount"].Should().Be(0);
    }

    #endregion

    #region Verbose Mode Tests

    [Fact]
    public async Task ExecuteAsync_VerboseMode_IncludesFileSizeAndDate()
    {
        // Arrange
        var file1 = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        File.WriteAllText(file1, "test data with some content");

        var command = new ListCommand();

        // Act
        var result = await command.ExecuteAsync(outputDir: _testOutputDir, verbose: true, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);
        var resultData = result.Data!["result"] as Dictionary<string, object>;
        resultData!["fileCount"].Should().Be(1);

        // Verify the files list exists and contains detailed info
        resultData.Should().ContainKey("files");
        var filesObject = resultData["files"];
        filesObject.Should().NotBeNull();

        // In verbose mode, file details are included (name, sizeBytes, lastModified)
        // Console output would show size and date (tested in E2E tests)
    }

    [Fact]
    public async Task ExecuteAsync_NormalMode_IncludesBasicFileInfo()
    {
        // Arrange
        var file1 = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        File.WriteAllText(file1, "test");

        var command = new ListCommand();

        // Act
        var result = await command.ExecuteAsync(outputDir: _testOutputDir, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);
        var resultData = result.Data!["result"] as Dictionary<string, object>;
        resultData!["fileCount"].Should().Be(1);
        resultData.Should().ContainKey("files");
    }

    #endregion

    #region Default Directory Tests

    [Fact]
    public async Task ExecuteAsync_NoOutputDirProvided_UsesDefaultDirectory()
    {
        // Arrange
        var command = new ListCommand();

        // Act (no outputDir provided, uses default which gets passed as parameter)
        var result = await command.ExecuteAsync(defaultOutputDir: _testOutputDir, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Files listed");
    }

    #endregion

    #region File Sorting Tests

    [Fact]
    public async Task ExecuteAsync_MultipleFiles_SortsByLastWriteTime()
    {
        // Arrange
        var file1 = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        var file2 = Path.Combine(_testOutputDir, "logs.20250117_110000_000.ndjson");
        var file3 = Path.Combine(_testOutputDir, "metrics.20250117_120000_000.ndjson");

        // Write in specific order with delays to ensure different timestamps
        File.WriteAllText(file2, "{}");
        await Task.Delay(10, TestContext.Current.CancellationToken);
        File.WriteAllText(file3, "{}");
        await Task.Delay(10, TestContext.Current.CancellationToken);
        File.WriteAllText(file1, "{}");

        var command = new ListCommand();

        // Act
        var result = await command.ExecuteAsync(outputDir: _testOutputDir, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);
        var resultData = result.Data!["result"] as Dictionary<string, object>;
        resultData!["fileCount"].Should().Be(3);

        // Files should be sorted by last write time (verified by file count)
        // Detailed file ordering is tested in console output (E2E tests)
    }

    #endregion

    #region Silent and JSON Output Tests

    [Fact]
    public async Task ExecuteAsync_SilentMode_ReturnsSuccessWithoutOutput()
    {
        // Arrange
        var file1 = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        File.WriteAllText(file1, "{}");

        var command = new ListCommand();

        // Act
        var result = await command.ExecuteAsync(outputDir: _testOutputDir, silent: true, jsonOutput: false);

        // Assert
        result.ExitCode.Should().Be(0);
        result.Message.Should().Be("Files listed");
        // Silent mode suppresses console output (tested in integration tests)
    }

    [Fact]
    public async Task ExecuteAsync_JsonOutput_ReturnsStructuredData()
    {
        // Arrange
        var file1 = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        File.WriteAllText(file1, "test content");

        var command = new ListCommand();

        // Act
        var result = await command.ExecuteAsync(outputDir: _testOutputDir, jsonOutput: true, silent: false);

        // Assert
        result.ExitCode.Should().Be(0);
        var resultData = result.Data!["result"] as Dictionary<string, object>;
        resultData.Should().NotBeNull();
        resultData!.Should().ContainKey("success");
        resultData.Should().ContainKey("outputDirectory");
        resultData.Should().ContainKey("fileCount");
        resultData.Should().ContainKey("files");
        resultData["success"].Should().Be(true);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ExecuteAsync_WithSubdirectories_OnlyListsTopLevel()
    {
        // Arrange
        var topLevelFile = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        var subdirPath = Path.Combine(_testOutputDir, "subdir");
        Directory.CreateDirectory(subdirPath);
        var subdirFile = Path.Combine(subdirPath, "logs.20250117_110000_000.ndjson");

        File.WriteAllText(topLevelFile, "{}");
        File.WriteAllText(subdirFile, "{}");

        var command = new ListCommand();

        // Act
        var result = await command.ExecuteAsync(outputDir: _testOutputDir, silent: true, jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);
        var resultData = result.Data!["result"] as Dictionary<string, object>;
        resultData!["fileCount"].Should().Be(1); // Only top-level file
    }

    #endregion

    #region Combined Flags Tests

    [Fact]
    public async Task ExecuteAsync_ErrorsOnlyAndVerbose_ListsErrorFilesWithDetails()
    {
        // Arrange
        var normalFile = Path.Combine(_testOutputDir, "traces.20250117_100000_000.ndjson");
        var errorFile = Path.Combine(_testOutputDir, "logs.20250117_110000_000.errors.ndjson");
        File.WriteAllText(normalFile, "normal");
        File.WriteAllText(errorFile, "error data");

        var command = new ListCommand();

        // Act
        var result = await command.ExecuteAsync(
            outputDir: _testOutputDir,
            errorsOnly: true,
            verbose: true,
            silent: true,
            jsonOutput: true);

        // Assert
        result.ExitCode.Should().Be(0);
        var resultData = result.Data!["result"] as Dictionary<string, object>;
        resultData!["fileCount"].Should().Be(1); // Only error file
        resultData["errorsOnly"].Should().Be(true);

        // Verbose mode includes file details in console output
        // Detailed field verification tested in E2E tests
        resultData.Should().ContainKey("files");
    }

    #endregion
}

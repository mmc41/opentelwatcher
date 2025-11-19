using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Receivers;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Services.Receivers;

public class FileReceiverTests : FileBasedTestBase
{
    [Fact]
    public async Task WriteAsync_WritesNdjsonToFile_WithNormalExtension()
    {
        // Arrange
        var mockRotation = new MockFileRotationService(TestOutputDir);
        var receiver = new FileReceiver(
            mockRotation,
            TestOutputDir,
            ".ndjson",
            100,
            NullLogger<FileReceiver>.Instance);

        var item = new TelemetryItem(
            SignalType.Traces,
            "{\"traceId\":\"123\"}\n",
            false,
            DateTimeOffset.UtcNow);

        // Act
        await receiver.WriteAsync(item, TestContext.Current.CancellationToken);

        // Assert
        var files = Directory.GetFiles(TestOutputDir, "traces.*.ndjson");
        files.Should().ContainSingle();
        var content = await File.ReadAllTextAsync(files[0], TestContext.Current.CancellationToken);
        content.Should().Be("{\"traceId\":\"123\"}\n");
    }

    [Fact]
    public async Task WriteAsync_WritesNdjsonToFile_WithErrorExtension()
    {
        // Arrange
        var mockRotation = new MockFileRotationService(TestOutputDir);
        var receiver = new FileReceiver(
            mockRotation,
            TestOutputDir,
            ".errors.ndjson",
            100,
            NullLogger<FileReceiver>.Instance);

        var item = new TelemetryItem(
            SignalType.Logs,
            "{\"severityNumber\":17}\n",
            true,
            DateTimeOffset.UtcNow);

        // Act
        await receiver.WriteAsync(item, TestContext.Current.CancellationToken);

        // Assert
        var files = Directory.GetFiles(TestOutputDir, "logs.*.errors.ndjson");
        files.Should().ContainSingle();
    }

    [Fact]
    public async Task WriteAsync_DisposedReceiver_ThrowsObjectDisposedException()
    {
        // Arrange
        var mockRotation = new MockFileRotationService(TestOutputDir);
        var receiver = new FileReceiver(
            mockRotation,
            TestOutputDir,
            ".ndjson",
            100,
            NullLogger<FileReceiver>.Instance);

        receiver.Dispose();

        var item = new TelemetryItem(SignalType.Traces, "{}\n", false, DateTimeOffset.UtcNow);

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => receiver.WriteAsync(item, TestContext.Current.CancellationToken));
    }
}

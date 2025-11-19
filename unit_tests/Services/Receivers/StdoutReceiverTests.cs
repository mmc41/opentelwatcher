using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Receivers;
using Xunit;

namespace OpenTelWatcher.Tests.Services.Receivers;

public class StdoutReceiverTests
{
    [Fact]
    public async Task WriteAsync_FormatsOutput_WithTimestampAndSignal()
    {
        // Arrange
        var receiver = new StdoutReceiver(NullLogger<StdoutReceiver>.Instance);
        var timestamp = new DateTimeOffset(2025, 1, 19, 12, 30, 45, 123, TimeSpan.Zero);
        var item = new TelemetryItem(
            SignalType.Traces,
            "{\"traceId\":\"abc123\"}\n",
            false,
            timestamp);

        var output = CaptureConsoleOutput(() =>
        {
            receiver.WriteAsync(item, CancellationToken.None).Wait();
        });

        // Assert
        output.Should().Contain("[2025-01-19T12:30:45.123]");
        output.Should().Contain("[traces]");
        output.Should().Contain("{\"traceId\":\"abc123\"}");
    }

    [Theory]
    [InlineData(SignalType.Traces, false, "\x1b[36m")] // Cyan
    [InlineData(SignalType.Logs, false, "\x1b[37m")]   // White
    [InlineData(SignalType.Metrics, false, "\x1b[32m")] // Green
    public async Task WriteAsync_ColorizesOutput_BySignalType(
        SignalType signal,
        bool isError,
        string expectedColor)
    {
        // Arrange
        var receiver = new StdoutReceiver(NullLogger<StdoutReceiver>.Instance);
        var item = new TelemetryItem(signal, "{}\n", isError, DateTimeOffset.UtcNow);

        var output = CaptureConsoleOutput(() =>
        {
            receiver.WriteAsync(item, CancellationToken.None).Wait();
        });

        // Assert
        output.Should().StartWith(expectedColor);
        output.Should().Contain("\x1b[0m"); // Reset color (platform-independent)
    }

    [Theory]
    [InlineData(SignalType.Traces)]
    [InlineData(SignalType.Logs)]
    [InlineData(SignalType.Metrics)]
    public async Task WriteAsync_ColorizesErrors_InRed(SignalType signal)
    {
        // Arrange
        var receiver = new StdoutReceiver(NullLogger<StdoutReceiver>.Instance);
        var item = new TelemetryItem(signal, "{}\n", IsError: true, DateTimeOffset.UtcNow);

        var output = CaptureConsoleOutput(() =>
        {
            receiver.WriteAsync(item, CancellationToken.None).Wait();
        });

        // Assert
        output.Should().StartWith("\x1b[31m"); // Red
    }

    [Fact]
    public async Task WriteAsync_PreservesRawNdjson_NoFormatting()
    {
        // Arrange
        var receiver = new StdoutReceiver(NullLogger<StdoutReceiver>.Instance);
        var complexJson = "{\"nested\":{\"array\":[1,2,3]},\"value\":\"test\"}\n";
        var item = new TelemetryItem(SignalType.Traces, complexJson, false, DateTimeOffset.UtcNow);

        var output = CaptureConsoleOutput(() =>
        {
            receiver.WriteAsync(item, CancellationToken.None).Wait();
        });

        // Assert - JSON should be preserved exactly (not prettified)
        output.Should().Contain("{\"nested\":{\"array\":[1,2,3]},\"value\":\"test\"}");
    }

    [Fact]
    public async Task WriteAsync_ThreadSafe_ConcurrentWrites()
    {
        // Arrange
        var receiver = new StdoutReceiver(NullLogger<StdoutReceiver>.Instance);
        var items = Enumerable.Range(0, 100).Select(i => new TelemetryItem(
            SignalType.Traces,
            $"{{\"id\":{i}}}\n",
            false,
            DateTimeOffset.UtcNow)).ToList();

        // Act - concurrent writes should not throw exceptions
        var exception = await Record.ExceptionAsync(async () =>
        {
            await Parallel.ForEachAsync(items, async (item, ct) =>
            {
                await receiver.WriteAsync(item, ct);
            });
        });

        // Assert - no exceptions thrown
        exception.Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_TrimsTrailingNewline_FromNdjson()
    {
        // Arrange
        var receiver = new StdoutReceiver(NullLogger<StdoutReceiver>.Instance);
        var item = new TelemetryItem(SignalType.Traces, "{\"id\":1}\n", false, DateTimeOffset.UtcNow);

        var output = CaptureConsoleOutput(() =>
        {
            receiver.WriteAsync(item, CancellationToken.None).Wait();
        });

        // Assert - should not have double newline
        output.Should().NotContain("}\n\n");
    }

    // Helper method
    private string CaptureConsoleOutput(Action action)
    {
        var originalOut = Console.Out;
        try
        {
            using var writer = new StringWriter();
            Console.SetOut(writer);
            action();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}

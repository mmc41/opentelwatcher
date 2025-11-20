using System.Collections.Concurrent;
using FluentAssertions;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Receivers;
using UnitTests.Helpers;
using Xunit;

namespace OpenTelWatcher.Tests.Services.Receivers;

public class StdoutReceiverTests
{
    [Fact]
    public async Task WriteAsync_FormatsOutput_WithTimestampAndSignal()
    {
        // Arrange
        var receiver = new StdoutReceiver(TestLoggerFactory.CreateLogger<StdoutReceiver>());
        var timestamp = new DateTimeOffset(2025, 1, 19, 12, 30, 45, 123, TimeSpan.Zero);
        var item = new TelemetryItem(
            SignalType.Traces,
            "{\"traceId\":\"abc123\"}\n",
            false,
            timestamp);

        var output = await CaptureConsoleOutputAsync(async () =>
        {
            await receiver.WriteAsync(item, TestContext.Current.CancellationToken);
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
        var receiver = new StdoutReceiver(TestLoggerFactory.CreateLogger<StdoutReceiver>());
        var item = new TelemetryItem(signal, "{}\n", isError, DateTimeOffset.UtcNow);

        var output = await CaptureConsoleOutputAsync(async () =>
        {
            await receiver.WriteAsync(item, TestContext.Current.CancellationToken);
        });

        // Assert - Check that output contains the expected color code (may have other output from parallel tests)
        output.Should().Contain(expectedColor, "output should contain the color code for {0}", signal);
        output.Should().Contain("\x1b[0m"); // Reset color
        output.Should().Contain($"[{signal.ToLowerString()}]", "output should contain signal type label");
    }

    [Theory]
    [InlineData(SignalType.Traces)]
    [InlineData(SignalType.Logs)]
    [InlineData(SignalType.Metrics)]
    public async Task WriteAsync_ColorizesErrors_InRed(SignalType signal)
    {
        // Arrange
        var receiver = new StdoutReceiver(TestLoggerFactory.CreateLogger<StdoutReceiver>());
        var item = new TelemetryItem(signal, "{}\n", IsError: true, DateTimeOffset.UtcNow);

        var output = await CaptureConsoleOutputAsync(async () =>
        {
            await receiver.WriteAsync(item, TestContext.Current.CancellationToken);
        });

        // Assert - Check that output contains red color code and signal label
        output.Should().Contain("\x1b[31m", "errors should be colored red");
        output.Should().Contain($"[{signal.ToLowerString()}]", "output should contain signal type label");
    }

    [Fact]
    public async Task WriteAsync_PreservesRawNdjson_NoFormatting()
    {
        // Arrange
        var receiver = new StdoutReceiver(TestLoggerFactory.CreateLogger<StdoutReceiver>());
        var complexJson = "{\"nested\":{\"array\":[1,2,3]},\"value\":\"test\"}\n";
        var item = new TelemetryItem(SignalType.Traces, complexJson, false, DateTimeOffset.UtcNow);

        var output = await CaptureConsoleOutputAsync(async () =>
        {
            await receiver.WriteAsync(item, TestContext.Current.CancellationToken);
        });

        // Assert - JSON should be preserved exactly (not prettified)
        output.Should().Contain("{\"nested\":{\"array\":[1,2,3]},\"value\":\"test\"}");
    }

    [Fact]
    public async Task WriteAsync_ThreadSafe_ConcurrentWrites()
    {
        // Arrange
        var receiver = new StdoutReceiver(TestLoggerFactory.CreateLogger<StdoutReceiver>());
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
        var receiver = new StdoutReceiver(TestLoggerFactory.CreateLogger<StdoutReceiver>());
        var item = new TelemetryItem(SignalType.Traces, "{\"id\":1}\n", false, DateTimeOffset.UtcNow);

        var output = await CaptureConsoleOutputAsync(async () =>
        {
            await receiver.WriteAsync(item, TestContext.Current.CancellationToken);
        });

        // Assert - should not have double newline
        output.Should().NotContain("}\n\n");
    }

    // Helper method
    private async Task<string> CaptureConsoleOutputAsync(Func<Task> action)
    {
        var originalOut = Console.Out;
        try
        {
            using var writer = new StringWriter();
            Console.SetOut(writer);
            await action();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}

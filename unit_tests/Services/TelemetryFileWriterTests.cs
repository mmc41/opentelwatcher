using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using OpenTelWatcher.Configuration;
using UnitTests.Helpers;
using OpenTelWatcher.Services;
using OpenTelWatcher.Services.Interfaces;
using System.Text.Json;
using Xunit;

namespace UnitTests.Services;

public class TelemetryFileWriterTests : FileBasedTestBase
{

    [Fact]
    public async Task WriteAsync_WithValidMessage_CreatesFileAndWritesJson()
    {
        // Arrange
        using var writer = TestBuilders.CreateTelemetryFileWriter(TestOutputDir);

        var timestamp = Timestamp.FromDateTime(DateTime.UtcNow);

        // Act
        await writer.WriteAsync(timestamp, TestConstants.Signals.Traces, TestContext.Current.CancellationToken);

        // Assert
        var filePath = writer.GetCurrentFilePath(TestConstants.Signals.Traces);
        File.Exists(filePath).Should().BeTrue();

        var content = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken);
        content.Should().NotBeNullOrEmpty();
        content.Should().EndWith("\n"); // NDJSON format

        // Verify it's valid JSON
        var lines = content.Trim().Split('\n');
        lines.Should().HaveCountGreaterThan(0);

        var json = JsonDocument.Parse(lines[0]);
        json.Should().NotBeNull();
    }

    [Fact]
    public async Task WriteAsync_MultipleTimes_AppendsToSameFile()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = TestOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService(new MockTimeProvider());
        var healthMonitor = new HealthMonitor(options);
        var errorDetectionService = new ErrorDetectionService();
        var logger = TestLoggerFactory.CreateLogger<TelemetryFileWriter>();
        using var writer = new TelemetryFileWriter(options, fileRotationService, healthMonitor, errorDetectionService, logger);

        var timestamp1 = Timestamp.FromDateTime(DateTime.UtcNow);
        var timestamp2 = Timestamp.FromDateTime(DateTime.UtcNow.AddSeconds(1));

        // Act
        await writer.WriteAsync(timestamp1, TestConstants.Signals.Traces, TestContext.Current.CancellationToken);
        await writer.WriteAsync(timestamp2, TestConstants.Signals.Traces, TestContext.Current.CancellationToken);

        // Assert
        var filePath = writer.GetCurrentFilePath(TestConstants.Signals.Traces);
        var content = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken);
        var lines = content.Trim().Split('\n');

        lines.Should().HaveCount(2); // Two NDJSON lines
    }

    [Theory]
    [InlineData(TestConstants.Signals.Traces)]
    [InlineData(TestConstants.Signals.Logs)]
    [InlineData(TestConstants.Signals.Metrics)]
    public async Task WriteAsync_DifferentSignals_CreatesSeparateFiles(string signal)
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = TestOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService(new MockTimeProvider());
        var healthMonitor = new HealthMonitor(options);
        var errorDetectionService = new ErrorDetectionService();
        var logger = TestLoggerFactory.CreateLogger<TelemetryFileWriter>();
        using var writer = new TelemetryFileWriter(options, fileRotationService, healthMonitor, errorDetectionService, logger);

        var timestamp = Timestamp.FromDateTime(DateTime.UtcNow);

        // Act
        await writer.WriteAsync(timestamp, signal, TestContext.Current.CancellationToken);

        // Assert
        var filePath = writer.GetCurrentFilePath(signal);
        filePath.Should().Contain(signal);
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_WithPrettyPrint_WritesIndentedJson()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = TestOutputDir,
            PrettyPrint = true,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService(new MockTimeProvider());
        var healthMonitor = new HealthMonitor(options);
        var errorDetectionService = new ErrorDetectionService();
        var logger = TestLoggerFactory.CreateLogger<TelemetryFileWriter>();
        using var writer = new TelemetryFileWriter(options, fileRotationService, healthMonitor, errorDetectionService, logger);

        var structMsg = new Struct();
        structMsg.Fields.Add("test", Google.Protobuf.WellKnownTypes.Value.ForString("value"));

        // Act
        await writer.WriteAsync(structMsg, TestConstants.Signals.Traces, TestContext.Current.CancellationToken);

        // Assert
        var filePath = writer.GetCurrentFilePath(TestConstants.Signals.Traces);
        var content = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken);

        // Pretty printed JSON should contain multiple lines before the final NDJSON newline
        content.Should().Contain("  "); // Indentation
    }

    [Fact]
    public async Task WriteAsync_Successful_RecordsSuccessInHealthMonitor()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = TestOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService(new MockTimeProvider());
        var healthMonitor = new HealthMonitor(options);
        var errorDetectionService = new ErrorDetectionService();
        var logger = TestLoggerFactory.CreateLogger<TelemetryFileWriter>();
        using var writer = new TelemetryFileWriter(options, fileRotationService, healthMonitor, errorDetectionService, logger);

        var timestamp = Timestamp.FromDateTime(DateTime.UtcNow);

        // Act
        await writer.WriteAsync(timestamp, TestConstants.Signals.Traces, TestContext.Current.CancellationToken);

        // Assert
        healthMonitor.Status.Should().Be(HealthStatus.Healthy);
        healthMonitor.ConsecutiveErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentFilePath_ReturnsActiveFilePath()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = TestOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService(new MockTimeProvider());
        var healthMonitor = new HealthMonitor(options);
        var errorDetectionService = new ErrorDetectionService();
        var logger = TestLoggerFactory.CreateLogger<TelemetryFileWriter>();
        using var writer = new TelemetryFileWriter(options, fileRotationService, healthMonitor, errorDetectionService, logger);

        var timestamp = Timestamp.FromDateTime(DateTime.UtcNow);
        await writer.WriteAsync(timestamp, TestConstants.Signals.Traces, TestContext.Current.CancellationToken);

        // Act
        var filePath = writer.GetCurrentFilePath(TestConstants.Signals.Traces);

        // Assert
        filePath.Should().StartWith(TestOutputDir);
        filePath.Should().Contain("traces.");
        filePath.Should().EndWith(".ndjson");
    }

    [Fact]
    public async Task WriteAsync_TraceWithError_CreatesErrorFile()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = TestOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService(new MockTimeProvider());
        var healthMonitor = new HealthMonitor(options);
        var errorDetectionService = new ErrorDetectionService();
        var logger = TestLoggerFactory.CreateLogger<TelemetryFileWriter>();
        using var writer = new TelemetryFileWriter(options, fileRotationService, healthMonitor, errorDetectionService, logger);

        // Create trace with error status
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "error-span",
            Status = new Status { Code = Status.Types.StatusCode.Error }
        };

        var scopeSpan = new ScopeSpans();
        scopeSpan.Spans.Add(span);

        var resourceSpan = new ResourceSpans();
        resourceSpan.Resource = new Resource();
        resourceSpan.ScopeSpans.Add(scopeSpan);

        var traceRequest = new ExportTraceServiceRequest();
        traceRequest.ResourceSpans.Add(resourceSpan);

        // Act
        await writer.WriteAsync(traceRequest, TestConstants.Signals.Traces, TestContext.Current.CancellationToken);

        // Assert - Normal file should exist
        var normalFilePath = writer.GetCurrentFilePath(TestConstants.Signals.Traces);
        File.Exists(normalFilePath).Should().BeTrue();

        // Error file should also exist
        var errorFiles = Directory.GetFiles(TestOutputDir, "traces.*.errors.ndjson");
        errorFiles.Should().HaveCount(1);

        // Both files should have the same content
        var normalContent = await File.ReadAllTextAsync(normalFilePath, TestContext.Current.CancellationToken);
        var errorContent = await File.ReadAllTextAsync(errorFiles[0], TestContext.Current.CancellationToken);
        errorContent.Should().Be(normalContent);
    }

    [Fact]
    public async Task WriteAsync_LogWithError_CreatesErrorFile()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = TestOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService(new MockTimeProvider());
        var healthMonitor = new HealthMonitor(options);
        var errorDetectionService = new ErrorDetectionService();
        var logger = TestLoggerFactory.CreateLogger<TelemetryFileWriter>();
        using var writer = new TelemetryFileWriter(options, fileRotationService, healthMonitor, errorDetectionService, logger);

        // Create log with error severity
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Error,
            SeverityText = "Error"
        };
        logRecord.Body = new AnyValue { StringValue = "An error occurred" };

        var scopeLog = new ScopeLogs();
        scopeLog.LogRecords.Add(logRecord);

        var resourceLog = new ResourceLogs();
        resourceLog.Resource = new Resource();
        resourceLog.ScopeLogs.Add(scopeLog);

        var logsRequest = new ExportLogsServiceRequest();
        logsRequest.ResourceLogs.Add(resourceLog);

        // Act
        await writer.WriteAsync(logsRequest, TestConstants.Signals.Logs, TestContext.Current.CancellationToken);

        // Assert - Normal file should exist
        var normalFilePath = writer.GetCurrentFilePath(TestConstants.Signals.Logs);
        File.Exists(normalFilePath).Should().BeTrue();

        // Error file should also exist
        var errorFiles = Directory.GetFiles(TestOutputDir, "logs.*.errors.ndjson");
        errorFiles.Should().HaveCount(1);

        // Both files should have the same content
        var normalContent = await File.ReadAllTextAsync(normalFilePath, TestContext.Current.CancellationToken);
        var errorContent = await File.ReadAllTextAsync(errorFiles[0], TestContext.Current.CancellationToken);
        errorContent.Should().Be(normalContent);
    }

    [Fact]
    public async Task WriteAsync_TraceWithoutError_DoesNotCreateErrorFile()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = TestOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService(new MockTimeProvider());
        var healthMonitor = new HealthMonitor(options);
        var errorDetectionService = new ErrorDetectionService();
        var logger = TestLoggerFactory.CreateLogger<TelemetryFileWriter>();
        using var writer = new TelemetryFileWriter(options, fileRotationService, healthMonitor, errorDetectionService, logger);

        // Create trace with OK status
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "ok-span",
            Status = new Status { Code = Status.Types.StatusCode.Ok }
        };

        var scopeSpan = new ScopeSpans();
        scopeSpan.Spans.Add(span);

        var resourceSpan = new ResourceSpans();
        resourceSpan.Resource = new Resource();
        resourceSpan.ScopeSpans.Add(scopeSpan);

        var traceRequest = new ExportTraceServiceRequest();
        traceRequest.ResourceSpans.Add(resourceSpan);

        // Act
        await writer.WriteAsync(traceRequest, TestConstants.Signals.Traces, TestContext.Current.CancellationToken);

        // Assert - Normal file should exist
        var normalFilePath = writer.GetCurrentFilePath(TestConstants.Signals.Traces);
        File.Exists(normalFilePath).Should().BeTrue();

        // Error file should NOT exist
        var errorFiles = Directory.GetFiles(TestOutputDir, "traces.*.errors.ndjson");
        errorFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_MixedTraces_OnlyErrorsInErrorFile()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = TestOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService(new MockTimeProvider());
        var healthMonitor = new HealthMonitor(options);
        var errorDetectionService = new ErrorDetectionService();
        var logger = TestLoggerFactory.CreateLogger<TelemetryFileWriter>();
        using var writer = new TelemetryFileWriter(options, fileRotationService, healthMonitor, errorDetectionService, logger);

        // Create OK trace
        var okSpan = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "ok-span",
            Status = new Status { Code = Status.Types.StatusCode.Ok }
        };
        var okRequest = TestBuilders.CreateTraceRequest(okSpan);

        // Create error trace
        var errorSpan = TestBuilders.CreateErrorSpan();
        var errorRequest = TestBuilders.CreateTraceRequest(errorSpan);

        // Act
        await writer.WriteAsync(okRequest, TestConstants.Signals.Traces, TestContext.Current.CancellationToken);
        await writer.WriteAsync(errorRequest, TestConstants.Signals.Traces, TestContext.Current.CancellationToken);

        // Assert
        var normalFilePath = writer.GetCurrentFilePath(TestConstants.Signals.Traces);
        var normalContent = await File.ReadAllTextAsync(normalFilePath, TestContext.Current.CancellationToken);
        var normalLines = normalContent.Trim().Split('\n');
        normalLines.Should().HaveCount(2); // Both traces in normal file

        var errorFiles = Directory.GetFiles(TestOutputDir, "traces.*.errors.ndjson");
        errorFiles.Should().HaveCount(1);
        var errorContent = await File.ReadAllTextAsync(errorFiles[0], TestContext.Current.CancellationToken);
        var errorLines = errorContent.Trim().Split('\n');
        errorLines.Should().HaveCount(1); // Only error trace in error file
    }

    #region Dispose Tests

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        using var writer = TestBuilders.CreateTelemetryFileWriter(TestOutputDir);

        // Act - Dispose multiple times
        writer.Dispose();
        writer.Dispose(); // Should not throw

        // Assert - No exception thrown
    }

    #endregion

    #region File Rotation Tests

    [Fact]
    public async Task WriteAsync_FileExceedsMaxSize_RotatesFile()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = TestOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 1, // 1 MB limit (very small for testing)
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var timeProvider = new MockTimeProvider();
        var fileRotationService = new FileRotationService(timeProvider);
        var healthMonitor = new HealthMonitor(options);
        var errorDetectionService = new ErrorDetectionService();
        var logger = TestLoggerFactory.CreateLogger<TelemetryFileWriter>();
        using var writer = new TelemetryFileWriter(options, fileRotationService, healthMonitor, errorDetectionService, logger);

        // Create large message (over 1 MB to ensure rotation)
        var largeStruct = new Struct();
        var largeValue = new string('x', 1_100_000); // 1.1 MB string
        largeStruct.Fields.Add("data", Google.Protobuf.WellKnownTypes.Value.ForString(largeValue));

        var firstFilePath = string.Empty;

        // Act - Write first large message
        await writer.WriteAsync(largeStruct, TestConstants.Signals.Traces, TestContext.Current.CancellationToken);
        firstFilePath = writer.GetCurrentFilePath(TestConstants.Signals.Traces);

        // Advance time to ensure different timestamp for rotated file
        timeProvider.AdvanceTime(TimeSpan.FromMilliseconds(1));

        // Write second large message (should trigger rotation because file > 1 MB)
        await writer.WriteAsync(largeStruct, TestConstants.Signals.Traces, TestContext.Current.CancellationToken);
        var secondFilePath = writer.GetCurrentFilePath(TestConstants.Signals.Traces);

        // Assert - File path should have changed due to rotation
        secondFilePath.Should().NotBe(firstFilePath);

        // Both files should exist
        File.Exists(firstFilePath).Should().BeTrue();
        File.Exists(secondFilePath).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_MultipleSignals_RotatesIndependently()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = TestOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 1,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var timeProvider = new MockTimeProvider();
        var fileRotationService = new FileRotationService(timeProvider);
        var healthMonitor = new HealthMonitor(options);
        var errorDetectionService = new ErrorDetectionService();
        var logger = TestLoggerFactory.CreateLogger<TelemetryFileWriter>();
        using var writer = new TelemetryFileWriter(options, fileRotationService, healthMonitor, errorDetectionService, logger);

        var largeStruct = new Struct();
        var largeValue = new string('x', 1_100_000);
        largeStruct.Fields.Add("data", Google.Protobuf.WellKnownTypes.Value.ForString(largeValue));

        // Act - Write to traces (causes rotation)
        await writer.WriteAsync(largeStruct, TestConstants.Signals.Traces, TestContext.Current.CancellationToken);
        var tracesFile1 = writer.GetCurrentFilePath(TestConstants.Signals.Traces);

        // Advance time for rotation
        timeProvider.AdvanceTime(TimeSpan.FromMilliseconds(1));

        await writer.WriteAsync(largeStruct, TestConstants.Signals.Traces, TestContext.Current.CancellationToken);
        var tracesFile2 = writer.GetCurrentFilePath(TestConstants.Signals.Traces);

        // Write to logs (should not affect traces rotation)
        await writer.WriteAsync(largeStruct, TestConstants.Signals.Logs, TestContext.Current.CancellationToken);
        var logsFile1 = writer.GetCurrentFilePath(TestConstants.Signals.Logs);

        // Assert
        tracesFile2.Should().NotBe(tracesFile1); // Traces rotated
        logsFile1.Should().NotBe(tracesFile1); // Logs has separate file
        logsFile1.Should().Contain("logs."); // Logs file contains signal name
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task WriteAsync_DirectoryCreationFailure_HandlesGracefully()
    {
        // Arrange - Create directory path that will fail (invalid path characters on Windows)
        var invalidDir = Path.Combine(Path.GetTempPath(), "invalid<>path" + Guid.NewGuid());
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = invalidDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 3,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService(new MockTimeProvider());
        var healthMonitor = new HealthMonitor(options);
        var errorDetectionService = new ErrorDetectionService();
        var logger = TestLoggerFactory.CreateLogger<TelemetryFileWriter>();
        using var writer = new TelemetryFileWriter(options, fileRotationService, healthMonitor, errorDetectionService, logger);

        var timestamp = Timestamp.FromDateTime(DateTime.UtcNow);

        // Act - Multiple write attempts should increment consecutive errors
        for (int i = 0; i < 5; i++)
        {
            try
            {
                await writer.WriteAsync(timestamp, TestConstants.Signals.Traces, TestContext.Current.CancellationToken);
            }
            catch
            {
                // Expected failures
            }
        }

        // Assert - Health monitor should be degraded after consecutive errors exceed threshold
        healthMonitor.Status.Should().Be(HealthStatus.Degraded);
        healthMonitor.ConsecutiveErrorCount.Should().BeGreaterThanOrEqualTo(3);
    }

    #endregion
}

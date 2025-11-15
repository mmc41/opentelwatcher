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
using OpenTelWatcher.Services;
using OpenTelWatcher.Services.Interfaces;
using System.Text.Json;
using UnitTests.Helpers;
using Xunit;

namespace OpenTelWatcher.Tests.Services;

public class TelemetryFileWriterTests : IDisposable
{
    private readonly string _testOutputDir;

    public TelemetryFileWriterTests()
    {
        _testOutputDir = Path.Combine(Path.GetTempPath(), "telemetry-writer-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testOutputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testOutputDir))
        {
            Directory.Delete(_testOutputDir, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_WithValidMessage_CreatesFileAndWritesJson()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = _testOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService();
        var healthMonitor = new HealthMonitor(options);
        var errorDetectionService = new ErrorDetectionService();
        var logger = TestLoggerFactory.CreateLogger<TelemetryFileWriter>();
        using var writer = new TelemetryFileWriter(options, fileRotationService, healthMonitor, errorDetectionService, logger);

        var timestamp = Timestamp.FromDateTime(DateTime.UtcNow);

        // Act
        await writer.WriteAsync(timestamp, "traces", TestContext.Current.CancellationToken);

        // Assert
        var filePath = writer.GetCurrentFilePath("traces");
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
            OutputDirectory = _testOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService();
        var healthMonitor = new HealthMonitor(options);
        var errorDetectionService = new ErrorDetectionService();
        var logger = TestLoggerFactory.CreateLogger<TelemetryFileWriter>();
        using var writer = new TelemetryFileWriter(options, fileRotationService, healthMonitor, errorDetectionService, logger);

        var timestamp1 = Timestamp.FromDateTime(DateTime.UtcNow);
        var timestamp2 = Timestamp.FromDateTime(DateTime.UtcNow.AddSeconds(1));

        // Act
        await writer.WriteAsync(timestamp1, "traces", TestContext.Current.CancellationToken);
        await writer.WriteAsync(timestamp2, "traces", TestContext.Current.CancellationToken);

        // Assert
        var filePath = writer.GetCurrentFilePath("traces");
        var content = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken);
        var lines = content.Trim().Split('\n');

        lines.Should().HaveCount(2); // Two NDJSON lines
    }

    [Theory]
    [InlineData("traces")]
    [InlineData("logs")]
    [InlineData("metrics")]
    public async Task WriteAsync_DifferentSignals_CreatesSeparateFiles(string signal)
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = _testOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService();
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
            OutputDirectory = _testOutputDir,
            PrettyPrint = true,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService();
        var healthMonitor = new HealthMonitor(options);
        var errorDetectionService = new ErrorDetectionService();
        var logger = TestLoggerFactory.CreateLogger<TelemetryFileWriter>();
        using var writer = new TelemetryFileWriter(options, fileRotationService, healthMonitor, errorDetectionService, logger);

        var structMsg = new Struct();
        structMsg.Fields.Add("test", Google.Protobuf.WellKnownTypes.Value.ForString("value"));

        // Act
        await writer.WriteAsync(structMsg, "traces", TestContext.Current.CancellationToken);

        // Assert
        var filePath = writer.GetCurrentFilePath("traces");
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
            OutputDirectory = _testOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService();
        var healthMonitor = new HealthMonitor(options);
        var errorDetectionService = new ErrorDetectionService();
        var logger = TestLoggerFactory.CreateLogger<TelemetryFileWriter>();
        using var writer = new TelemetryFileWriter(options, fileRotationService, healthMonitor, errorDetectionService, logger);

        var timestamp = Timestamp.FromDateTime(DateTime.UtcNow);

        // Act
        await writer.WriteAsync(timestamp, "traces", TestContext.Current.CancellationToken);

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
            OutputDirectory = _testOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService();
        var healthMonitor = new HealthMonitor(options);
        var errorDetectionService = new ErrorDetectionService();
        var logger = TestLoggerFactory.CreateLogger<TelemetryFileWriter>();
        using var writer = new TelemetryFileWriter(options, fileRotationService, healthMonitor, errorDetectionService, logger);

        var timestamp = Timestamp.FromDateTime(DateTime.UtcNow);
        await writer.WriteAsync(timestamp, "traces", TestContext.Current.CancellationToken);

        // Act
        var filePath = writer.GetCurrentFilePath("traces");

        // Assert
        filePath.Should().StartWith(_testOutputDir);
        filePath.Should().Contain("traces.");
        filePath.Should().EndWith(".ndjson");
    }

    [Fact]
    public async Task WriteAsync_TraceWithError_CreatesErrorFile()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = _testOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService();
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
        await writer.WriteAsync(traceRequest, "traces", TestContext.Current.CancellationToken);

        // Assert - Normal file should exist
        var normalFilePath = writer.GetCurrentFilePath("traces");
        File.Exists(normalFilePath).Should().BeTrue();

        // Error file should also exist
        var errorFiles = Directory.GetFiles(_testOutputDir, "traces.*.errors.ndjson");
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
            OutputDirectory = _testOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService();
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
        await writer.WriteAsync(logsRequest, "logs", TestContext.Current.CancellationToken);

        // Assert - Normal file should exist
        var normalFilePath = writer.GetCurrentFilePath("logs");
        File.Exists(normalFilePath).Should().BeTrue();

        // Error file should also exist
        var errorFiles = Directory.GetFiles(_testOutputDir, "logs.*.errors.ndjson");
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
            OutputDirectory = _testOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService();
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
        await writer.WriteAsync(traceRequest, "traces", TestContext.Current.CancellationToken);

        // Assert - Normal file should exist
        var normalFilePath = writer.GetCurrentFilePath("traces");
        File.Exists(normalFilePath).Should().BeTrue();

        // Error file should NOT exist
        var errorFiles = Directory.GetFiles(_testOutputDir, "traces.*.errors.ndjson");
        errorFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_MixedTraces_OnlyErrorsInErrorFile()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = _testOutputDir,
            PrettyPrint = false,
            MaxFileSizeMB = 100,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };

        var fileRotationService = new FileRotationService();
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
        var okRequest = CreateTraceRequest(okSpan);

        // Create error trace
        var errorSpan = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "error-span",
            Status = new Status { Code = Status.Types.StatusCode.Error }
        };
        var errorRequest = CreateTraceRequest(errorSpan);

        // Act
        await writer.WriteAsync(okRequest, "traces", TestContext.Current.CancellationToken);
        await writer.WriteAsync(errorRequest, "traces", TestContext.Current.CancellationToken);

        // Assert
        var normalFilePath = writer.GetCurrentFilePath("traces");
        var normalContent = await File.ReadAllTextAsync(normalFilePath, TestContext.Current.CancellationToken);
        var normalLines = normalContent.Trim().Split('\n');
        normalLines.Should().HaveCount(2); // Both traces in normal file

        var errorFiles = Directory.GetFiles(_testOutputDir, "traces.*.errors.ndjson");
        errorFiles.Should().HaveCount(1);
        var errorContent = await File.ReadAllTextAsync(errorFiles[0], TestContext.Current.CancellationToken);
        var errorLines = errorContent.Trim().Split('\n');
        errorLines.Should().HaveCount(1); // Only error trace in error file
    }

    private static ExportTraceServiceRequest CreateTraceRequest(Span span)
    {
        var scopeSpan = new ScopeSpans();
        scopeSpan.Spans.Add(span);

        var resourceSpan = new ResourceSpans();
        resourceSpan.Resource = new Resource();
        resourceSpan.ScopeSpans.Add(scopeSpan);

        var request = new ExportTraceServiceRequest();
        request.ResourceSpans.Add(resourceSpan);

        return request;
    }
}

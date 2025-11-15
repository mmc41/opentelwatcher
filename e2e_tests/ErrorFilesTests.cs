using FluentAssertions;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using OpenTelWatcher.Tests.E2E;
using System.Net;
using Xunit;

namespace OpenTelWatcher.E2ETests;

[Collection("Watcher Server")]
public class ErrorFilesTests
{
    private readonly OpenTelWatcherServerFixture _fixture;
    private readonly HttpClient _client;
    private readonly string _outputDirectory;

    public ErrorFilesTests(OpenTelWatcherServerFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;

        // Find solution root by looking for project.root marker file
        var currentDir = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(currentDir, "project.root")))
        {
            currentDir = Directory.GetParent(currentDir)?.FullName
                ?? throw new InvalidOperationException("Could not find solution root (project.root marker file)");
        }

        _outputDirectory = Path.Combine(currentDir, "telemetry-data");
    }

    [Fact]
    public async Task TraceWithErrorStatus_CreatesErrorFile()
    {
        // Arrange - Create trace with error status
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "error-span",
            Status = new Status { Code = Status.Types.StatusCode.Error, Message = "Test error" }
        };

        var traceRequest = CreateTraceRequest(span);

        // Act - Send trace request
        var response = await _client.PostAsync("/v1/traces",
            new ByteArrayContent(traceRequest.ToByteArray()),
            TestContext.Current.CancellationToken);

        // Assert - Request should succeed
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Wait briefly for file write
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Verify error file was created
        var errorFiles = Directory.GetFiles(_outputDirectory, "traces.*.errors.ndjson");
        errorFiles.Should().NotBeEmpty("error file should be created for trace with error status");
    }

    [Fact]
    public async Task TraceWithExceptionEvent_CreatesErrorFile()
    {
        // Arrange - Create trace with exception event
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "span-with-exception"
        };

        span.Events.Add(new Span.Types.Event
        {
            Name = "exception",
            TimeUnixNano = 1700000000000000000
        });

        var traceRequest = CreateTraceRequest(span);

        // Act - Send trace request
        var response = await _client.PostAsync("/v1/traces",
            new ByteArrayContent(traceRequest.ToByteArray()),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var errorFiles = Directory.GetFiles(_outputDirectory, "traces.*.errors.ndjson");
        errorFiles.Should().NotBeEmpty("error file should be created for trace with exception event");
    }

    [Fact]
    public async Task LogWithErrorSeverity_CreatesErrorFile()
    {
        // Arrange - Create log with error severity
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Error,
            SeverityText = "Error"
        };
        logRecord.Body = new AnyValue { StringValue = "Test error message" };

        var logsRequest = CreateLogsRequest(logRecord);

        // Act - Send logs request
        var response = await _client.PostAsync("/v1/logs",
            new ByteArrayContent(logsRequest.ToByteArray()),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var errorFiles = Directory.GetFiles(_outputDirectory, "logs.*.errors.ndjson");
        errorFiles.Should().NotBeEmpty("error file should be created for log with error severity");
    }

    [Fact]
    public async Task LogWithExceptionAttributes_CreatesErrorFile()
    {
        // Arrange - Create log with exception attributes
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Info // Not error severity, but has exception attributes
        };
        logRecord.Attributes.Add(new KeyValue
        {
            Key = "exception.type",
            Value = new AnyValue { StringValue = "System.InvalidOperationException" }
        });
        logRecord.Attributes.Add(new KeyValue
        {
            Key = "exception.message",
            Value = new AnyValue { StringValue = "Test exception" }
        });

        var logsRequest = CreateLogsRequest(logRecord);

        // Act - Send logs request
        var response = await _client.PostAsync("/v1/logs",
            new ByteArrayContent(logsRequest.ToByteArray()),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var errorFiles = Directory.GetFiles(_outputDirectory, "logs.*.errors.ndjson");
        errorFiles.Should().NotBeEmpty("error file should be created for log with exception attributes");
    }

    [Fact]
    public async Task TraceWithoutError_DoesNotCreateErrorFile()
    {
        // Arrange - Count existing error files
        var errorFilesBefore = Directory.GetFiles(_outputDirectory, "traces.*.errors.ndjson").Length;

        // Create trace with OK status
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "ok-span",
            Status = new Status { Code = Status.Types.StatusCode.Ok }
        };

        var traceRequest = CreateTraceRequest(span);

        // Act - Send trace request
        var response = await _client.PostAsync("/v1/traces",
            new ByteArrayContent(traceRequest.ToByteArray()),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // No new error files should be created
        var errorFilesAfter = Directory.GetFiles(_outputDirectory, "traces.*.errors.ndjson").Length;
        errorFilesAfter.Should().Be(errorFilesBefore, "no error file should be created for trace without errors");
    }

    [Fact]
    public async Task LogWithInfoSeverity_DoesNotCreateErrorFile()
    {
        // Arrange - Count existing error files
        var errorFilesBefore = Directory.GetFiles(_outputDirectory, "logs.*.errors.ndjson").Length;

        // Create log with info severity
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Info,
            SeverityText = "Info"
        };
        logRecord.Body = new AnyValue { StringValue = "Test info message" };

        var logsRequest = CreateLogsRequest(logRecord);

        // Act - Send logs request
        var response = await _client.PostAsync("/v1/logs",
            new ByteArrayContent(logsRequest.ToByteArray()),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // No new error files should be created
        var errorFilesAfter = Directory.GetFiles(_outputDirectory, "logs.*.errors.ndjson").Length;
        errorFilesAfter.Should().Be(errorFilesBefore, "no error file should be created for log without errors");
    }

    [Fact]
    public async Task ClearCommand_DeletesErrorFiles()
    {
        // Arrange - Create a trace with error to ensure error file exists
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "error-span",
            Status = new Status { Code = Status.Types.StatusCode.Error }
        };

        var traceRequest = CreateTraceRequest(span);
        await _client.PostAsync("/v1/traces",
            new ByteArrayContent(traceRequest.ToByteArray()),
            TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Verify error file was created
        var errorFilesBefore = Directory.GetFiles(_outputDirectory, "*.errors.ndjson");
        errorFilesBefore.Should().NotBeEmpty("error file should exist before clearing");

        // Act - Clear all files via API
        var response = await _client.PostAsync("/api/clear", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Wait for clear operation to complete
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // All error files should be deleted
        var errorFilesAfter = Directory.GetFiles(_outputDirectory, "*.errors.ndjson");
        errorFilesAfter.Should().BeEmpty("all error files should be deleted after clear");
    }

    #region Helper Methods

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

    private static ExportLogsServiceRequest CreateLogsRequest(LogRecord logRecord)
    {
        var scopeLog = new ScopeLogs();
        scopeLog.LogRecords.Add(logRecord);

        var resourceLog = new ResourceLogs();
        resourceLog.Resource = new Resource();
        resourceLog.ScopeLogs.Add(scopeLog);

        var request = new ExportLogsServiceRequest();
        request.ResourceLogs.Add(resourceLog);

        return request;
    }

    #endregion
}

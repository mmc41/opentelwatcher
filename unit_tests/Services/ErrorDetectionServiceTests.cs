using FluentAssertions;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using OpenTelWatcher.Services;
using Xunit;

namespace OpenTelWatcher.Tests.Unit.Services;

public class ErrorDetectionServiceTests
{
    private readonly ErrorDetectionService _service;

    public ErrorDetectionServiceTests()
    {
        _service = new ErrorDetectionService();
    }

    #region Trace Error Detection Tests

    [Fact]
    public void ContainsErrors_TraceWithErrorStatus_ReturnsTrue()
    {
        // Arrange
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "error-span",
            Status = new Status { Code = Status.Types.StatusCode.Error }
        };

        var request = CreateTraceRequest(span);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsErrors_TraceWithOkStatus_ReturnsFalse()
    {
        // Arrange
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "ok-span",
            Status = new Status { Code = Status.Types.StatusCode.Ok }
        };

        var request = CreateTraceRequest(span);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsErrors_TraceWithUnsetStatus_ReturnsFalse()
    {
        // Arrange
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "unset-span",
            Status = new Status { Code = Status.Types.StatusCode.Unset }
        };

        var request = CreateTraceRequest(span);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsErrors_TraceWithExceptionEvent_ReturnsTrue()
    {
        // Arrange
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "span-with-exception-event"
        };
        span.Events.Add(new Span.Types.Event
        {
            Name = "exception",
            TimeUnixNano = 1700000000000000000
        });

        var request = CreateTraceRequest(span);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsErrors_TraceWithNonExceptionEvent_ReturnsFalse()
    {
        // Arrange
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "span-with-other-event"
        };
        span.Events.Add(new Span.Types.Event
        {
            Name = "log",
            TimeUnixNano = 1700000000000000000
        });

        var request = CreateTraceRequest(span);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsErrors_TraceWithMultipleSpans_OneWithError_ReturnsTrue()
    {
        // Arrange
        var okSpan = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "ok-span",
            Status = new Status { Code = Status.Types.StatusCode.Ok }
        };

        var errorSpan = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "error-span",
            Status = new Status { Code = Status.Types.StatusCode.Error }
        };

        var request = CreateTraceRequest(okSpan, errorSpan);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsErrors_EmptyTraceRequest_ReturnsFalse()
    {
        // Arrange
        var request = new ExportTraceServiceRequest();

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsErrors_NullTraceRequest_ReturnsFalse()
    {
        // Arrange
        ExportTraceServiceRequest? request = null;

        // Act
        var result = _service.ContainsErrors(request!);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Log Error Detection Tests

    [Fact]
    public void ContainsErrors_LogWithErrorSeverity_ReturnsTrue()
    {
        // Arrange - SeverityNumber 17 = ERROR
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Error,
            SeverityText = "Error"
        };
        logRecord.Body = new AnyValue { StringValue = "An error occurred" };

        var request = CreateLogRequest(logRecord);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsErrors_LogWithFatalSeverity_ReturnsTrue()
    {
        // Arrange - SeverityNumber 21 = FATAL
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Fatal,
            SeverityText = "Fatal"
        };
        logRecord.Body = new AnyValue { StringValue = "A fatal error occurred" };

        var request = CreateLogRequest(logRecord);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsErrors_LogWithInfoSeverity_ReturnsFalse()
    {
        // Arrange - SeverityNumber 9 = INFO
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Info,
            SeverityText = "Info"
        };
        logRecord.Body = new AnyValue { StringValue = "Information message" };

        var request = CreateLogRequest(logRecord);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsErrors_LogWithWarnSeverity_ReturnsFalse()
    {
        // Arrange - SeverityNumber 13 = WARN
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Warn,
            SeverityText = "Warn"
        };
        logRecord.Body = new AnyValue { StringValue = "Warning message" };

        var request = CreateLogRequest(logRecord);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsErrors_LogWithExceptionTypeAttribute_ReturnsTrue()
    {
        // Arrange
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Info
        };
        logRecord.Attributes.Add(new KeyValue
        {
            Key = "exception.type",
            Value = new AnyValue { StringValue = "System.InvalidOperationException" }
        });

        var request = CreateLogRequest(logRecord);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsErrors_LogWithExceptionMessageAttribute_ReturnsTrue()
    {
        // Arrange
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Info
        };
        logRecord.Attributes.Add(new KeyValue
        {
            Key = "exception.message",
            Value = new AnyValue { StringValue = "Operation failed" }
        });

        var request = CreateLogRequest(logRecord);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsErrors_LogWithExceptionStacktraceAttribute_ReturnsTrue()
    {
        // Arrange
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Info
        };
        logRecord.Attributes.Add(new KeyValue
        {
            Key = "exception.stacktrace",
            Value = new AnyValue { StringValue = "at System.Foo.Bar()" }
        });

        var request = CreateLogRequest(logRecord);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsErrors_LogWithNonExceptionAttributes_ReturnsFalse()
    {
        // Arrange
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Info
        };
        logRecord.Attributes.Add(new KeyValue
        {
            Key = "user.id",
            Value = new AnyValue { StringValue = "123" }
        });
        logRecord.Attributes.Add(new KeyValue
        {
            Key = "http.method",
            Value = new AnyValue { StringValue = "GET" }
        });

        var request = CreateLogRequest(logRecord);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsErrors_LogWithMultipleRecords_OneWithError_ReturnsTrue()
    {
        // Arrange
        var infoLog = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Info
        };

        var errorLog = new LogRecord
        {
            TimeUnixNano = 1700000000000000001,
            SeverityNumber = SeverityNumber.Error
        };

        var request = CreateLogRequest(infoLog, errorLog);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsErrors_EmptyLogRequest_ReturnsFalse()
    {
        // Arrange
        var request = new ExportLogsServiceRequest();

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsErrors_NullLogRequest_ReturnsFalse()
    {
        // Arrange
        ExportLogsServiceRequest? request = null;

        // Act
        var result = _service.ContainsErrors(request!);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private static ExportTraceServiceRequest CreateTraceRequest(params Span[] spans)
    {
        var scopeSpan = new ScopeSpans();
        foreach (var span in spans)
        {
            scopeSpan.Spans.Add(span);
        }

        var resourceSpan = new ResourceSpans();
        resourceSpan.Resource = new Resource();
        resourceSpan.ScopeSpans.Add(scopeSpan);

        var request = new ExportTraceServiceRequest();
        request.ResourceSpans.Add(resourceSpan);

        return request;
    }

    private static ExportLogsServiceRequest CreateLogRequest(params LogRecord[] logRecords)
    {
        var scopeLog = new ScopeLogs();
        foreach (var logRecord in logRecords)
        {
            scopeLog.LogRecords.Add(logRecord);
        }

        var resourceLog = new ResourceLogs();
        resourceLog.Resource = new Resource();
        resourceLog.ScopeLogs.Add(scopeLog);

        var request = new ExportLogsServiceRequest();
        request.ResourceLogs.Add(resourceLog);

        return request;
    }

    #endregion
}

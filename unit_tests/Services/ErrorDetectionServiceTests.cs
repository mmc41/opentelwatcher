using FluentAssertions;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using OpenTelWatcher.Services;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Services;

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

        var request = TestBuilders.CreateTraceRequest(span);

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

        var request = TestBuilders.CreateTraceRequest(span);

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

        var request = TestBuilders.CreateTraceRequest(span);

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

        var request = TestBuilders.CreateTraceRequest(span);

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

        var request = TestBuilders.CreateTraceRequest(span);

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

        var request = TestBuilders.CreateTraceRequest(okSpan, errorSpan);

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

    [Fact]
    public void ContainsErrors_TraceWithMultipleSpansAndEvents_IdentifiesError()
    {
        // Arrange - Span with both OK status and non-exception event (shouldn't trigger)
        var span1 = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "span1",
            Status = new Status { Code = Status.Types.StatusCode.Ok }
        };
        span1.Events.Add(new Span.Types.Event { Name = "log", TimeUnixNano = 1700000000000000000 });

        // Span with exception event (should trigger)
        var span2 = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "span2",
            Status = new Status { Code = Status.Types.StatusCode.Unset }
        };
        span2.Events.Add(new Span.Types.Event { Name = "log", TimeUnixNano = 1700000000000000001 });
        span2.Events.Add(new Span.Types.Event { Name = "exception", TimeUnixNano = 1700000000000000002 });

        var request = TestBuilders.CreateTraceRequest(span1, span2);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsErrors_TraceWithMultipleResourceSpans_IdentifiesError()
    {
        // Arrange - Create two separate resource spans
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

        var scopeSpan1 = new ScopeSpans();
        scopeSpan1.Spans.Add(okSpan);

        var scopeSpan2 = new ScopeSpans();
        scopeSpan2.Spans.Add(errorSpan);

        var resourceSpan1 = new ResourceSpans();
        resourceSpan1.Resource = new Resource();
        resourceSpan1.ScopeSpans.Add(scopeSpan1);

        var resourceSpan2 = new ResourceSpans();
        resourceSpan2.Resource = new Resource();
        resourceSpan2.ScopeSpans.Add(scopeSpan2);

        var request = new ExportTraceServiceRequest();
        request.ResourceSpans.Add(resourceSpan1);
        request.ResourceSpans.Add(resourceSpan2);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsErrors_TraceWithNoStatus_ReturnsFalse()
    {
        // Arrange
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "span-without-status"
            // Status is null
        };

        var request = TestBuilders.CreateTraceRequest(span);

        // Act
        var result = _service.ContainsErrors(request);

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

        var request = TestBuilders.CreateLogRequest(logRecord);

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

        var request = TestBuilders.CreateLogRequest(logRecord);

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

        var request = TestBuilders.CreateLogRequest(logRecord);

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

        var request = TestBuilders.CreateLogRequest(logRecord);

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

        var request = TestBuilders.CreateLogRequest(logRecord);

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

        var request = TestBuilders.CreateLogRequest(logRecord);

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

        var request = TestBuilders.CreateLogRequest(logRecord);

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

        var request = TestBuilders.CreateLogRequest(logRecord);

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

        var request = TestBuilders.CreateLogRequest(infoLog, errorLog);

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

    [Fact]
    public void ContainsErrors_LogWithMultipleRecordsAndAttributes_IdentifiesError()
    {
        // Arrange - Log with multiple non-exception attributes (shouldn't trigger)
        var log1 = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Info
        };
        log1.Attributes.Add(new KeyValue { Key = "user.id", Value = new AnyValue { StringValue = "123" } });
        log1.Attributes.Add(new KeyValue { Key = "http.method", Value = new AnyValue { StringValue = "GET" } });

        // Log with exception attribute (should trigger)
        var log2 = new LogRecord
        {
            TimeUnixNano = 1700000000000000001,
            SeverityNumber = SeverityNumber.Info
        };
        log2.Attributes.Add(new KeyValue { Key = "user.id", Value = new AnyValue { StringValue = "456" } });
        log2.Attributes.Add(new KeyValue { Key = "exception.type", Value = new AnyValue { StringValue = "System.Exception" } });

        var request = TestBuilders.CreateLogRequest(log1, log2);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsErrors_LogWithMultipleResourceLogs_IdentifiesError()
    {
        // Arrange - Create two separate resource logs
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

        var scopeLog1 = new ScopeLogs();
        scopeLog1.LogRecords.Add(infoLog);

        var scopeLog2 = new ScopeLogs();
        scopeLog2.LogRecords.Add(errorLog);

        var resourceLog1 = new ResourceLogs();
        resourceLog1.Resource = new Resource();
        resourceLog1.ScopeLogs.Add(scopeLog1);

        var resourceLog2 = new ResourceLogs();
        resourceLog2.Resource = new Resource();
        resourceLog2.ScopeLogs.Add(scopeLog2);

        var request = new ExportLogsServiceRequest();
        request.ResourceLogs.Add(resourceLog1);
        request.ResourceLogs.Add(resourceLog2);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Malformed Data Handling Tests

    [Fact]
    public void ContainsErrors_TraceWithNullResourceSpans_ReturnsFalse()
    {
        // Arrange - Request with null ResourceSpans (defensive check)
        var request = new ExportTraceServiceRequest();
        // ResourceSpans collection is empty (not null, but no entries)

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsErrors_TraceWithNullScopeSpans_ReturnsFalse()
    {
        // Arrange - ResourceSpan with empty ScopeSpans
        var resourceSpan = new ResourceSpans
        {
            Resource = new Resource()
        };
        // ScopeSpans collection is empty

        var request = new ExportTraceServiceRequest();
        request.ResourceSpans.Add(resourceSpan);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsErrors_TraceWithNullSpanStatus_ReturnsFalse()
    {
        // Arrange - Span with null Status property
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "span-without-status"
            // Status is null
        };

        var request = TestBuilders.CreateTraceRequest(span);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeFalse("null status should be treated as non-error");
    }

    [Fact]
    public void ContainsErrors_LogWithNullResourceLogs_ReturnsFalse()
    {
        // Arrange - Request with empty ResourceLogs collection
        var request = new ExportLogsServiceRequest();
        // ResourceLogs collection is empty

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsErrors_LogWithNullScopeLogs_ReturnsFalse()
    {
        // Arrange - ResourceLog with empty ScopeLogs
        var resourceLog = new ResourceLogs
        {
            Resource = new Resource()
        };
        // ScopeLogs collection is empty

        var request = new ExportLogsServiceRequest();
        request.ResourceLogs.Add(resourceLog);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsErrors_LogWithNullAttributes_DoesNotThrow()
    {
        // Arrange - Log record with empty attributes
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Info
        };
        // Attributes collection is empty (not null)

        var request = TestBuilders.CreateLogRequest(logRecord);

        // Act
        var act = () => _service.ContainsErrors(request);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ContainsErrors_TraceWithEmptyEvents_ReturnsFalse()
    {
        // Arrange - Span with empty events collection
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = "span-without-events",
            Status = new Status { Code = Status.Types.StatusCode.Ok }
        };
        // Events collection is empty

        var request = TestBuilders.CreateTraceRequest(span);

        // Act
        var result = _service.ContainsErrors(request);

        // Assert
        result.Should().BeFalse();
    }

    #endregion
}

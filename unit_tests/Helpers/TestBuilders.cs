using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Services.Interfaces;
using Google.Protobuf;

namespace UnitTests.Helpers;

/// <summary>
/// Fluent builders for common test objects to reduce duplication in unit tests.
///
/// Design Note: This is a simpler version compared to e2e_tests/Helpers/ProtobufBuilders.cs.
/// The duplication is intentional to keep test projects independent:
/// - TestBuilders: Simpler, focused on unit test scenarios with minimal setup
/// - ProtobufBuilders: More comprehensive, with extension methods and flexible parameters for E2E tests
///
/// If adding new builders, consider whether they belong in unit tests (simple mocks)
/// or E2E tests (realistic data with full protobuf structure).
/// </summary>
public static class TestBuilders
{
    /// <summary>
    /// Creates OpenTelWatcherOptions with default test values.
    /// </summary>
    public static OpenTelWatcherOptions CreateDefaultOptions(string outputDirectory)
    {
        return new OpenTelWatcherOptions
        {
            OutputDirectory = outputDirectory,
            PrettyPrint = TestConstants.DefaultConfig.PrettyPrint,
            MaxFileSizeMB = TestConstants.DefaultConfig.MaxFileSizeMB,
            MaxConsecutiveFileErrors = TestConstants.DefaultConfig.MaxConsecutiveFileErrors,
            MaxErrorHistorySize = TestConstants.DefaultConfig.MaxErrorHistorySize
        };
    }

    /// <summary>
    /// Creates a trace request with the specified spans.
    /// </summary>
    public static ExportTraceServiceRequest CreateTraceRequest(params Span[] spans)
    {
        var scopeSpan = new ScopeSpans();
        foreach (var span in spans)
        {
            scopeSpan.Spans.Add(span);
        }

        var resourceSpan = new ResourceSpans
        {
            Resource = new Resource()
        };
        resourceSpan.ScopeSpans.Add(scopeSpan);

        var request = new ExportTraceServiceRequest();
        request.ResourceSpans.Add(resourceSpan);

        return request;
    }

    /// <summary>
    /// Creates a log request with the specified log records.
    /// </summary>
    public static ExportLogsServiceRequest CreateLogRequest(params LogRecord[] logRecords)
    {
        var scopeLog = new ScopeLogs();
        foreach (var logRecord in logRecords)
        {
            scopeLog.LogRecords.Add(logRecord);
        }

        var resourceLog = new ResourceLogs
        {
            Resource = new Resource()
        };
        resourceLog.ScopeLogs.Add(scopeLog);

        var request = new ExportLogsServiceRequest();
        request.ResourceLogs.Add(resourceLog);

        return request;
    }

    /// <summary>
    /// Creates a span with error status.
    /// </summary>
    public static Span CreateErrorSpan(string name = "error-span")
    {
        return new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = name,
            Status = new Status { Code = Status.Types.StatusCode.Error }
        };
    }

    /// <summary>
    /// Creates a span with OK status.
    /// </summary>
    public static Span CreateOkSpan(string name = "ok-span")
    {
        return new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = name,
            Status = new Status { Code = Status.Types.StatusCode.Ok }
        };
    }

    /// <summary>
    /// Creates a span with exception event.
    /// </summary>
    public static Span CreateSpanWithException(string name = "span-with-exception")
    {
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(new byte[16]),
            SpanId = ByteString.CopyFrom(new byte[8]),
            Name = name
        };
        span.Events.Add(new Span.Types.Event
        {
            Name = "exception",
            TimeUnixNano = 1700000000000000000
        });
        return span;
    }

    /// <summary>
    /// Creates a log record with error severity.
    /// </summary>
    public static LogRecord CreateErrorLogRecord(string message = "An error occurred")
    {
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Error,
            SeverityText = "Error",
            Body = new AnyValue { StringValue = message }
        };
        return logRecord;
    }

    /// <summary>
    /// Creates a log record with info severity.
    /// </summary>
    public static LogRecord CreateInfoLogRecord(string message = "Information message")
    {
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Info,
            SeverityText = "Info",
            Body = new AnyValue { StringValue = message }
        };
        return logRecord;
    }

    /// <summary>
    /// Creates a StatusResponse for API tests.
    /// </summary>
    public static StatusResponse CreateStatusResponse(
        string outputDir = "./telemetry-data",
        int fileCount = 0,
        long totalSize = 0,
        int processId = TestConstants.ProcessIds.DefaultTestPid,
        int port = TestConstants.Network.DefaultPort)
    {
        return new StatusResponse
        {
            Application = "OpenTelWatcher",
            Version = TestConstants.Versions.VersionString,
            VersionComponents = new VersionComponents
            {
                Major = TestConstants.Versions.MajorVersion,
                Minor = TestConstants.Versions.MinorVersion,
                Patch = TestConstants.Versions.PatchVersion
            },
            ProcessId = processId,
            Port = port,
            UptimeSeconds = TestConstants.Timing.DefaultUptimeSeconds,
            Health = new DiagnoseHealth
            {
                Status = "healthy",
                ConsecutiveErrors = 0,
                RecentErrors = new List<string>()
            },
            Telemetry = new TelemetryStatistics
            {
                Traces = new TelemetryTypeStats { Requests = 0 },
                Logs = new TelemetryTypeStats { Requests = 0 },
                Metrics = new TelemetryTypeStats { Requests = 0 }
            },
            Files = new StatusFileStatistics
            {
                Count = fileCount,
                TotalSizeBytes = totalSize,
                Breakdown = new FileBreakdown
                {
                    Traces = new FileTypeStats { Count = 0, SizeBytes = 0 },
                    Logs = new FileTypeStats { Count = 0, SizeBytes = 0 },
                    Metrics = new FileTypeStats { Count = 0, SizeBytes = 0 }
                }
            },
            Configuration = new DiagnoseConfiguration
            {
                OutputDirectory = outputDir
            }
        };
    }
}

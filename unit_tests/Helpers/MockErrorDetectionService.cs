using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelWatcher.Services.Interfaces;

namespace UnitTests.Helpers;

/// <summary>
/// Mock error detection service for testing.
/// </summary>
public class MockErrorDetectionService : IErrorDetectionService
{
    public bool ContainsErrorResult { get; set; } = false;

    public bool ContainsErrors(ExportTraceServiceRequest request)
    {
        return ContainsErrorResult;
    }

    public bool ContainsErrors(ExportLogsServiceRequest request)
    {
        return ContainsErrorResult;
    }
}

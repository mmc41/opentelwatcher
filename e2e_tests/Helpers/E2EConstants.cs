namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// Constants used across E2E tests to avoid magic strings.
/// Centralizes endpoint URLs, file patterns, timeouts, and other commonly used values.
/// </summary>
public static class E2EConstants
{
    /// <summary>
    /// OTLP endpoint URLs for receiving telemetry data.
    /// </summary>
    public static class OtlpEndpoints
    {
        public const string Traces = "/v1/traces";
        public const string Logs = "/v1/logs";
        public const string Metrics = "/v1/metrics";
    }

    /// <summary>
    /// Management API endpoint URLs.
    /// </summary>
    public static class ApiEndpoints
    {
        public const string Status = "/api/status";
        public const string Stop = "/api/stop";
        public const string Clear = "/api/clear";
        public const string List = "/api/list";
    }

    /// <summary>
    /// Web UI endpoint URLs.
    /// </summary>
    public static class WebEndpoints
    {
        public const string Root = "/";
        public const string Health = "/healthz";
        public const string Swagger = "/swagger/index.html";
        public const string OpenApiSpec = "/openapi/v1.json";
    }

    /// <summary>
    /// File patterns for telemetry data files.
    /// </summary>
    public static class FilePatterns
    {
        public const string TracesNdjson = "traces.*.ndjson";
        public const string LogsNdjson = "logs.*.ndjson";
        public const string MetricsNdjson = "metrics.*.ndjson";
        public const string TracesErrors = "traces.*.errors.ndjson";
        public const string LogsErrors = "logs.*.errors.ndjson";
        public const string MetricsErrors = "metrics.*.errors.ndjson";
        public const string AllErrors = "*.errors.ndjson";
        public const string AllNdjson = "*.ndjson";
    }

    /// <summary>
    /// Default timeout values in milliseconds.
    /// </summary>
    public static class Timeouts
    {
        public const int FileWriteMs = 2000;
        public const int ProcessExitMs = 5000;
        public const int ServerStartupMs = 10000;
        public const int HealthCheckMs = 5000;
        public const int ApiRequestMs = 5000;
    }

    /// <summary>
    /// Delay values in milliseconds for test timing coordination.
    /// Used to ensure proper sequencing of operations (e.g., file timestamps, async processing).
    /// </summary>
    public static class Delays
    {
        /// <summary>Minimum delay to ensure different file timestamps (10ms)</summary>
        public const int TimestampDifferentiationMs = 10;

        /// <summary>Short delay for concurrent operation coordination (50ms)</summary>
        public const int ShortCoordinationMs = 50;

        /// <summary>Standard polling/processing interval (100ms)</summary>
        public const int StandardPollingMs = 100;

        /// <summary>File write settling time before verification (200ms)</summary>
        public const int FileWriteSettlingMs = 200;

        /// <summary>Processing completion wait time (500ms)</summary>
        public const int ProcessingCompletionMs = 500;

        /// <summary>Server health check polling interval (1000ms)</summary>
        public const int HealthCheckPollingMs = 1000;
    }

    /// <summary>
    /// Query string parameters for API endpoints.
    /// </summary>
    public static class QueryParams
    {
        public const string Signal = "signal";
    }

    /// <summary>
    /// Common JSON property names in API responses.
    /// </summary>
    public static class JsonProperties
    {
        public const string Status = "status";
        public const string Application = "application";
        public const string Version = "version";
        public const string Health = "health";
        public const string Files = "files";
        public const string Configuration = "configuration";
        public const string OutputDirectory = "outputDirectory";
        public const string Success = "success";
        public const string Message = "message";
    }

    /// <summary>
    /// Expected values in responses.
    /// </summary>
    public static class ExpectedValues
    {
        public const string ApplicationName = "OpenTelWatcher";
        public const string HealthyStatus = "healthy";
        public const string StopInitiatedMessage = "Stop initiated";
    }

    /// <summary>
    /// Content type headers.
    /// </summary>
    public static class ContentTypes
    {
        public const string Protobuf = "application/x-protobuf";
        public const string Json = "application/json";
    }

    /// <summary>
    /// Test service names for protobuf data.
    /// </summary>
    public static class TestServiceNames
    {
        public const string Default = "test-service";
        public const string Logs = "log-test-service";
        public const string Metrics = "metrics-test-service";
    }

    /// <summary>
    /// Test instrumentation scope names.
    /// </summary>
    public static class TestScopeNames
    {
        public const string Default = "test-instrumentation";
        public const string Logs = "test-log-scope";
        public const string Metrics = "test-metrics-scope";
    }
}

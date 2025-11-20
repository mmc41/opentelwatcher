using FluentAssertions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using System.Net;
using System.Text.Json;
using Xunit;
using OpenTelWatcher.Configuration;

namespace OpenTelWatcher.Tests.E2E;

[Collection("Watcher Server")]
public class OpenTelWatcherE2ETests
{
    private readonly OpenTelWatcherServerFixture _fixture;
    private readonly HttpClient _client;
    private readonly ILogger<OpenTelWatcherE2ETests> _logger;

    public OpenTelWatcherE2ETests(OpenTelWatcherServerFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
        _logger = TestLoggerFactory.CreateLogger<OpenTelWatcherE2ETests>();
    }

    [Fact]
    public async Task HealthzEndpoint_ReturnsHealthyStatus()
    {
        // Act
        _logger.LogInformation("Testing {Endpoint} endpoint returns healthy status", E2EConstants.WebEndpoints.Health);
        var response = await _client.GetAsync(E2EConstants.WebEndpoints.Health, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = JsonDocument.Parse(content);

        var status = json.RootElement.GetProperty(E2EConstants.JsonProperties.Status).GetString();
        _logger.LogInformation("Health status: {Status}", status);

        status.Should().Be(E2EConstants.ExpectedValues.HealthyStatus);
    }

    [Fact]
    public async Task EndToEnd_MultipleRequests_MaintainsHealthyStatus()
    {
        // Arrange - Create protobuf requests
        var traceRequest = new ExportTraceServiceRequest();
        var logsRequest = new ExportLogsServiceRequest();
        var metricsRequest = new ExportMetricsServiceRequest();

        // Act - Send multiple requests
        await _client.PostAsync(E2EConstants.OtlpEndpoints.Traces, new ByteArrayContent(traceRequest.ToByteArray()), TestContext.Current.CancellationToken);
        await _client.PostAsync(E2EConstants.OtlpEndpoints.Logs, new ByteArrayContent(logsRequest.ToByteArray()), TestContext.Current.CancellationToken);
        await _client.PostAsync(E2EConstants.OtlpEndpoints.Metrics, new ByteArrayContent(metricsRequest.ToByteArray()), TestContext.Current.CancellationToken);

        // Check health after multiple successful requests
        var healthResponse = await _client.GetAsync(E2EConstants.WebEndpoints.Health, TestContext.Current.CancellationToken);

        // Assert
        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var healthContent = await healthResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = JsonDocument.Parse(healthContent);

        json.RootElement.GetProperty(E2EConstants.JsonProperties.Status).GetString().Should().Be(E2EConstants.ExpectedValues.HealthyStatus);
    }

    [Fact]
    public async Task TracesEndpoint_RoundTripValidation()
    {
        // Arrange - Create a complete trace with span data
        _logger.LogInformation("Testing trace round-trip validation");
        var traceId = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var spanId = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var span = new Span
        {
            TraceId = ByteString.CopyFrom(traceId),
            SpanId = ByteString.CopyFrom(spanId),
            Name = "test-span",
            StartTimeUnixNano = 1700000000000000000,
            EndTimeUnixNano = 1700000001000000000
        };
        span.Attributes.Add(new KeyValue { Key = "http.method", Value = new AnyValue { StringValue = "GET" } });
        span.Attributes.Add(new KeyValue { Key = "http.status_code", Value = new AnyValue { IntValue = 200 } });

        var scopeSpan = new ScopeSpans();
        scopeSpan.Spans.Add(span);
        scopeSpan.Scope = new InstrumentationScope { Name = "test-instrumentation", Version = "1.0.0" };

        var resourceSpan = new ResourceSpans();
        resourceSpan.Resource = new Resource();
        resourceSpan.Resource.Attributes.Add(new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "test-service" } });
        resourceSpan.Resource.Attributes.Add(new KeyValue { Key = "service.version", Value = new AnyValue { StringValue = "1.2.3" } });
        resourceSpan.ScopeSpans.Add(scopeSpan);

        var traceRequest = new ExportTraceServiceRequest();
        traceRequest.ResourceSpans.Add(resourceSpan);

        var protobufData = traceRequest.ToByteArray();
        var content = new ByteArrayContent(protobufData);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(E2EConstants.ContentTypes.Protobuf);

        // Act - Send the trace
        _logger.LogInformation("Sending trace request to {Endpoint}", E2EConstants.OtlpEndpoints.Traces);
        var response = await _client.PostAsync(E2EConstants.OtlpEndpoints.Traces, content, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Get output directory from diagnostics
        var diagResponse = await _client.GetAsync($"{E2EConstants.ApiEndpoints.Status}?{E2EConstants.QueryParams.Signal}={SignalType.Traces.ToLowerString()}", TestContext.Current.CancellationToken);
        var diagContent = await diagResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var diagJson = JsonDocument.Parse(diagContent);
        var outputDir = diagJson.RootElement.GetProperty(E2EConstants.JsonProperties.Configuration).GetProperty(E2EConstants.JsonProperties.OutputDirectory).GetString();

        _logger.LogInformation("Output directory: {OutputDir}", outputDir);

        // Wait for trace file to be written
        var fileCreated = await PollingHelpers.WaitForFileAsync(
            outputDir!,
            E2EConstants.FilePatterns.TracesNdjson,
            timeoutMs: E2EConstants.Timeouts.FileWriteMs,
            cancellationToken: TestContext.Current.CancellationToken,
            logger: _logger);

        fileCreated.Should().BeTrue("trace file should be created within timeout");

        // Find and read the trace file
        var traceFiles = Directory.GetFiles(outputDir!, "traces.*.ndjson");
        _logger.LogInformation("Found {Count} trace file(s)", traceFiles.Length);
        traceFiles.Should().NotBeEmpty();

        var latestFile = traceFiles.OrderByDescending(File.GetLastWriteTimeUtc).First();
        _logger.LogDebug("Reading trace file: {FilePath}", latestFile);
        var fileContent = await File.ReadAllTextAsync(latestFile, TestContext.Current.CancellationToken);

        // Parse NDJSON and deserialize back to protobuf
        // File may contain multiple lines, get the last non-empty one
        var lines = fileContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        _logger.LogDebug("File contains {LineCount} line(s) of JSON", lines.Length);
        var jsonLine = lines.Last();
        var parser = new Google.Protobuf.JsonParser(Google.Protobuf.JsonParser.Settings.Default);
        var deserializedRequest = parser.Parse<ExportTraceServiceRequest>(jsonLine);

        _logger.LogInformation("Successfully deserialized trace request from file");

        // Assert - Verify round-trip equality
        deserializedRequest.Should().BeEquivalentTo(traceRequest, options => options
            .ComparingByMembers<ExportTraceServiceRequest>()
            .ComparingByMembers<ResourceSpans>()
            .ComparingByMembers<ScopeSpans>()
            .ComparingByMembers<Span>()
            .ComparingByMembers<Resource>()
            .ComparingByMembers<InstrumentationScope>()
            .ComparingByMembers<KeyValue>()
            .ComparingByMembers<AnyValue>());
    }

    [Fact]
    public async Task LogsEndpoint_RoundTripValidation()
    {
        // Arrange - Create a complete log record
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1700000000000000000,
            SeverityNumber = SeverityNumber.Info,
            SeverityText = "INFO",
            Body = new AnyValue { StringValue = "This is a test log message" }
        };
        logRecord.Attributes.Add(new KeyValue { Key = "log.source", Value = new AnyValue { StringValue = "test-logger" } });
        logRecord.Attributes.Add(new KeyValue { Key = "thread.id", Value = new AnyValue { IntValue = 12345 } });

        var scopeLog = new ScopeLogs();
        scopeLog.LogRecords.Add(logRecord);
        scopeLog.Scope = new InstrumentationScope { Name = "test-log-scope", Version = "2.0.0" };

        var resourceLog = new ResourceLogs();
        resourceLog.Resource = new Resource();
        resourceLog.Resource.Attributes.Add(new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "log-test-service" } });
        resourceLog.ScopeLogs.Add(scopeLog);

        var logsRequest = new ExportLogsServiceRequest();
        logsRequest.ResourceLogs.Add(resourceLog);

        var protobufData = logsRequest.ToByteArray();
        var content = new ByteArrayContent(protobufData);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");

        // Act - Send the logs
        var response = await _client.PostAsync("/v1/logs", content, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Get output directory from diagnostics
        var diagResponse = await _client.GetAsync("/api/status?signal=logs", TestContext.Current.CancellationToken);
        var diagContent = await diagResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var diagJson = JsonDocument.Parse(diagContent);
        var outputDir = diagJson.RootElement.GetProperty("configuration").GetProperty("outputDirectory").GetString();

        // Wait for log file to be written
        var fileCreated = await PollingHelpers.WaitForFileAsync(
            outputDir!,
            "logs.*.ndjson",
            timeoutMs: 2000,
            cancellationToken: TestContext.Current.CancellationToken,
            logger: _logger);

        fileCreated.Should().BeTrue("log file should be created within timeout");

        // Find and read the logs file
        var logFiles = Directory.GetFiles(outputDir!, "logs.*.ndjson");
        logFiles.Should().NotBeEmpty();

        var latestFile = logFiles.OrderByDescending(File.GetLastWriteTimeUtc).First();
        var fileContent = await File.ReadAllTextAsync(latestFile, TestContext.Current.CancellationToken);

        // Parse NDJSON and deserialize back to protobuf
        // File may contain multiple lines, get the last non-empty one
        var lines = fileContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var jsonLine = lines.Last();
        var parser = new Google.Protobuf.JsonParser(Google.Protobuf.JsonParser.Settings.Default);
        var deserializedRequest = parser.Parse<ExportLogsServiceRequest>(jsonLine);

        // Assert - Verify round-trip equality
        deserializedRequest.Should().BeEquivalentTo(logsRequest, options => options
            .ComparingByMembers<ExportLogsServiceRequest>()
            .ComparingByMembers<ResourceLogs>()
            .ComparingByMembers<ScopeLogs>()
            .ComparingByMembers<LogRecord>()
            .ComparingByMembers<Resource>()
            .ComparingByMembers<InstrumentationScope>()
            .ComparingByMembers<KeyValue>()
            .ComparingByMembers<AnyValue>());
    }

    [Fact]
    public async Task MetricsEndpoint_RoundTripValidation()
    {
        // Arrange - Create a complete metric with data points
        var metric = new Metric
        {
            Name = "http.request.duration",
            Unit = "ms",
            Description = "HTTP request duration in milliseconds"
        };

        var histogram = new Histogram();
        var dataPoint = new HistogramDataPoint
        {
            StartTimeUnixNano = 1700000000000000000,
            TimeUnixNano = 1700000001000000000,
            Count = 100,
            Sum = 5000.0
        };
        dataPoint.ExplicitBounds.Add(100);
        dataPoint.ExplicitBounds.Add(500);
        dataPoint.ExplicitBounds.Add(1000);
        dataPoint.BucketCounts.Add(30);
        dataPoint.BucketCounts.Add(50);
        dataPoint.BucketCounts.Add(15);
        dataPoint.BucketCounts.Add(5);
        dataPoint.Attributes.Add(new KeyValue { Key = "http.method", Value = new AnyValue { StringValue = "POST" } });
        dataPoint.Attributes.Add(new KeyValue { Key = "http.route", Value = new AnyValue { StringValue = "/api/users" } });

        histogram.DataPoints.Add(dataPoint);
        metric.Histogram = histogram;

        var scopeMetric = new ScopeMetrics();
        scopeMetric.Metrics.Add(metric);
        scopeMetric.Scope = new InstrumentationScope { Name = "test-metrics-scope", Version = "3.0.0" };

        var resourceMetric = new ResourceMetrics();
        resourceMetric.Resource = new Resource();
        resourceMetric.Resource.Attributes.Add(new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "metrics-test-service" } });
        resourceMetric.Resource.Attributes.Add(new KeyValue { Key = "host.name", Value = new AnyValue { StringValue = "test-host-001" } });
        resourceMetric.ScopeMetrics.Add(scopeMetric);

        var metricsRequest = new ExportMetricsServiceRequest();
        metricsRequest.ResourceMetrics.Add(resourceMetric);

        var protobufData = metricsRequest.ToByteArray();
        var content = new ByteArrayContent(protobufData);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");

        // Act - Send the metrics
        var response = await _client.PostAsync("/v1/metrics", content, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Get output directory from diagnostics
        var diagResponse = await _client.GetAsync("/api/status?signal=metrics", TestContext.Current.CancellationToken);
        var diagContent = await diagResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var diagJson = JsonDocument.Parse(diagContent);
        var outputDir = diagJson.RootElement.GetProperty("configuration").GetProperty("outputDirectory").GetString();

        // Wait for metrics file to be written
        var fileCreated = await PollingHelpers.WaitForFileAsync(
            outputDir!,
            "metrics.*.ndjson",
            timeoutMs: 2000,
            cancellationToken: TestContext.Current.CancellationToken,
            logger: _logger);

        fileCreated.Should().BeTrue("metrics file should be created within timeout");

        // Find and read the metrics file
        var metricFiles = Directory.GetFiles(outputDir!, "metrics.*.ndjson");
        metricFiles.Should().NotBeEmpty();

        var latestFile = metricFiles.OrderByDescending(File.GetLastWriteTimeUtc).First();
        var fileContent = await File.ReadAllTextAsync(latestFile, TestContext.Current.CancellationToken);

        // Parse NDJSON and deserialize back to protobuf
        // File may contain multiple lines, get the last non-empty one
        var lines = fileContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var jsonLine = lines.Last();
        var parser = new Google.Protobuf.JsonParser(Google.Protobuf.JsonParser.Settings.Default);
        var deserializedRequest = parser.Parse<ExportMetricsServiceRequest>(jsonLine);

        // Assert - Verify round-trip equality
        deserializedRequest.Should().BeEquivalentTo(metricsRequest, options => options
            .ComparingByMembers<ExportMetricsServiceRequest>()
            .ComparingByMembers<ResourceMetrics>()
            .ComparingByMembers<ScopeMetrics>()
            .ComparingByMembers<Metric>()
            .ComparingByMembers<Histogram>()
            .ComparingByMembers<HistogramDataPoint>()
            .ComparingByMembers<Resource>()
            .ComparingByMembers<InstrumentationScope>()
            .ComparingByMembers<KeyValue>()
            .ComparingByMembers<AnyValue>());
    }
}

using FluentAssertions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using OpenTelWatcher.Tests.E2E;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace OpenTelWatcher.Tests.E2E;

[Collection("Watcher Server")]
public class ErrorFilesTests
{
    private readonly OpenTelWatcherServerFixture _fixture;
    private readonly HttpClient _client;
    private readonly ILogger<ErrorFilesTests> _logger;

    public ErrorFilesTests(OpenTelWatcherServerFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
        _logger = TestLoggerFactory.CreateLogger<ErrorFilesTests>();
    }

    /// <summary>
    /// Gets the output directory from the running fixture instance.
    /// This ensures we're checking the correct directory for each test.
    /// </summary>
    private async Task<string> GetOutputDirectoryAsync()
    {
        var response = await _client.GetAsync(E2EConstants.ApiEndpoints.Status, TestContext.Current.CancellationToken);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var statusDoc = JsonDocument.Parse(json);
        return statusDoc.RootElement.GetProperty(E2EConstants.JsonProperties.Configuration).GetProperty(E2EConstants.JsonProperties.OutputDirectory).GetString()
            ?? throw new InvalidOperationException("Output directory not found in status response");
    }

    [Fact]
    public async Task TraceWithErrorStatus_CreatesErrorFile()
    {
        // Arrange - Create trace with error status
        _logger.LogInformation("Creating trace with error status");
        var span = ProtobufBuilders.CreateErrorSpan();
        var traceRequest = ProtobufBuilders.CreateTraceRequest(span);

        // Act - Send trace request
        _logger.LogDebug("Sending trace request with error status");
        var response = await _client.PostAsync(E2EConstants.OtlpEndpoints.Traces,
            new ByteArrayContent(traceRequest.ToByteArray()),
            TestContext.Current.CancellationToken);

        // Assert - Request should succeed
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Wait for error file to be written
        var outputDirectory = await GetOutputDirectoryAsync();
        var fileCreated = await PollingHelpers.WaitForFileAsync(
            outputDirectory,
            E2EConstants.FilePatterns.TracesErrors,
            timeoutMs: E2EConstants.Timeouts.FileWriteMs,
            cancellationToken: TestContext.Current.CancellationToken,
            logger: _logger);

        fileCreated.Should().BeTrue("error file should be created within timeout");

        // Verify error file was created
        _logger.LogInformation("Checking for error files in {OutputDirectory}", outputDirectory);
        var errorFiles = Directory.GetFiles(outputDirectory, E2EConstants.FilePatterns.TracesErrors);
        _logger.LogInformation("Found {ErrorFileCount} error file(s)", errorFiles.Length);
        errorFiles.Should().NotBeEmpty("error file should be created for trace with error status");
    }

    [Fact]
    public async Task TraceWithExceptionEvent_CreatesErrorFile()
    {
        // Arrange - Create trace with exception event
        var span = ProtobufBuilders.CreateSpanWithException();
        var traceRequest = ProtobufBuilders.CreateTraceRequest(span);

        // Act - Send trace request
        var response = await _client.PostAsync(E2EConstants.OtlpEndpoints.Traces,
            new ByteArrayContent(traceRequest.ToByteArray()),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var outputDirectory = await GetOutputDirectoryAsync();
        var fileCreated = await PollingHelpers.WaitForFileAsync(
            outputDirectory,
            E2EConstants.FilePatterns.TracesErrors,
            timeoutMs: E2EConstants.Timeouts.FileWriteMs,
            cancellationToken: TestContext.Current.CancellationToken,
            logger: _logger);

        fileCreated.Should().BeTrue("error file should be created within timeout");
        var errorFiles = Directory.GetFiles(outputDirectory, E2EConstants.FilePatterns.TracesErrors);
        errorFiles.Should().NotBeEmpty("error file should be created for trace with exception event");
    }

    [Fact]
    public async Task LogWithErrorSeverity_CreatesErrorFile()
    {
        // Arrange - Create log with error severity
        var logRecord = ProtobufBuilders.CreateErrorLogRecord();
        var logsRequest = ProtobufBuilders.CreateLogsRequest(logRecord);

        // Act - Send logs request
        var response = await _client.PostAsync("/v1/logs",
            new ByteArrayContent(logsRequest.ToByteArray()),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var outputDirectory = await GetOutputDirectoryAsync();
        var fileCreated = await PollingHelpers.WaitForFileAsync(
            outputDirectory,
            "logs.*.errors.ndjson",
            timeoutMs: 2000,
            cancellationToken: TestContext.Current.CancellationToken,
            logger: _logger);

        fileCreated.Should().BeTrue("error file should be created within timeout");
        var errorFiles = Directory.GetFiles(outputDirectory, "logs.*.errors.ndjson");
        errorFiles.Should().NotBeEmpty("error file should be created for log with error severity");
    }

    [Fact]
    public async Task LogWithExceptionAttributes_CreatesErrorFile()
    {
        // Arrange - Create log with exception attributes (not error severity, but has exception)
        var logRecord = ProtobufBuilders.CreateLogRecordWithException();
        var logsRequest = ProtobufBuilders.CreateLogsRequest(logRecord);

        // Act - Send logs request
        var response = await _client.PostAsync("/v1/logs",
            new ByteArrayContent(logsRequest.ToByteArray()),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var outputDirectory = await GetOutputDirectoryAsync();
        var fileCreated = await PollingHelpers.WaitForFileAsync(
            outputDirectory,
            "logs.*.errors.ndjson",
            timeoutMs: 2000,
            cancellationToken: TestContext.Current.CancellationToken,
            logger: _logger);

        fileCreated.Should().BeTrue("error file should be created within timeout");
        var errorFiles = Directory.GetFiles(outputDirectory, "logs.*.errors.ndjson");
        errorFiles.Should().NotBeEmpty("error file should be created for log with exception attributes");
    }

    [Fact]
    public async Task TraceWithoutError_DoesNotCreateErrorFile()
    {
        // Arrange - Get output directory and count existing error files
        var outputDirectory = await GetOutputDirectoryAsync();
        var errorFilesBefore = Directory.GetFiles(outputDirectory, "traces.*.errors.ndjson").Length;

        // Create trace with OK status
        var span = ProtobufBuilders.CreateSpan(name: "ok-span", statusCode: Status.Types.StatusCode.Ok);
        var traceRequest = ProtobufBuilders.CreateTraceRequest(span);

        // Act - Send trace request
        var response = await _client.PostAsync("/v1/traces",
            new ByteArrayContent(traceRequest.ToByteArray()),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Wait a moment to ensure any file write would have occurred
        await Task.Delay(E2EConstants.Delays.FileWriteSettlingMs, TestContext.Current.CancellationToken);

        // No new error files should be created
        var errorFilesAfter = Directory.GetFiles(outputDirectory, "traces.*.errors.ndjson").Length;
        errorFilesAfter.Should().Be(errorFilesBefore, "no error file should be created for trace without errors");
    }

    [Fact]
    public async Task LogWithInfoSeverity_DoesNotCreateErrorFile()
    {
        // Arrange - Get output directory and count existing error files
        var outputDirectory = await GetOutputDirectoryAsync();
        var errorFilesBefore = Directory.GetFiles(outputDirectory, "logs.*.errors.ndjson").Length;

        // Create log with info severity
        var logRecord = ProtobufBuilders.CreateLogRecord(body: "Test info message");
        var logsRequest = ProtobufBuilders.CreateLogsRequest(logRecord);

        // Act - Send logs request
        var response = await _client.PostAsync("/v1/logs",
            new ByteArrayContent(logsRequest.ToByteArray()),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Wait a moment to ensure any file write would have occurred
        await Task.Delay(E2EConstants.Delays.FileWriteSettlingMs, TestContext.Current.CancellationToken);

        // No new error files should be created
        var errorFilesAfter = Directory.GetFiles(outputDirectory, "logs.*.errors.ndjson").Length;
        errorFilesAfter.Should().Be(errorFilesBefore, "no error file should be created for log without errors");
    }

    [Fact]
    public async Task ClearCommand_DeletesErrorFiles()
    {
        // Arrange - Create a trace with error to ensure error file exists
        var span = ProtobufBuilders.CreateErrorSpan();
        var traceRequest = ProtobufBuilders.CreateTraceRequest(span);
        await _client.PostAsync("/v1/traces",
            new ByteArrayContent(traceRequest.ToByteArray()),
            TestContext.Current.CancellationToken);

        // Wait for error file to be created
        var outputDirectory = await GetOutputDirectoryAsync();
        var fileCreated = await PollingHelpers.WaitForFileAsync(
            outputDirectory,
            "*.errors.ndjson",
            timeoutMs: 2000,
            cancellationToken: TestContext.Current.CancellationToken,
            logger: _logger);

        fileCreated.Should().BeTrue("error file should be created within timeout");

        // Verify error file was created
        var errorFilesBefore = Directory.GetFiles(outputDirectory, "*.errors.ndjson");
        errorFilesBefore.Should().NotBeEmpty("error file should exist before clearing");

        // Act - Clear all files via API
        var response = await _client.PostAsync("/api/clear", null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Wait for clear operation to complete
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // All error files should be deleted
        var errorFilesAfter = Directory.GetFiles(outputDirectory, "*.errors.ndjson");
        errorFilesAfter.Should().BeEmpty("all error files should be deleted after clear");
    }

}

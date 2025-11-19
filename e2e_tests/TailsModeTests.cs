using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using OpenTelWatcher.Configuration;
using Xunit;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// E2E test for tails mode with error filtering.
/// Verifies that --tails --tails-filter-errors-only options are accepted and server functions correctly.
/// </summary>
public class TailsModeTests : IAsyncLifetime, IDisposable
{
    private readonly ILogger<TailsModeTests> _logger;
    private readonly string _tempDir;
    private readonly int _port;
    private Process? _watcherProcess;
    private HttpClient? _client;

    public TailsModeTests()
    {
        _logger = TestLoggerFactory.CreateLogger<TailsModeTests>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"opentelwatcher-tails-test-{Guid.NewGuid():N}");
        _port = PortAllocator.Allocate();
        Directory.CreateDirectory(_tempDir);
    }

    public async ValueTask InitializeAsync()
    {
        // Start watcher with tails mode and error filter
        var (fileName, solutionRoot) = GetDotnetExecutableInfo();
        var arguments = $"run --project opentelwatcher -- start --port {_port} --output-dir \"{_tempDir}\" --tails --tails-filter-errors-only";

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = solutionRoot
        };

        _watcherProcess = Process.Start(startInfo);
        if (_watcherProcess == null)
        {
            throw new InvalidOperationException("Failed to start watcher process");
        }

        // Capture stdout (for tails output)
        _watcherProcess.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                _logger.LogInformation("STDOUT: {Data}", e.Data);
            }
        };
        _watcherProcess.BeginOutputReadLine();

        // Capture stderr
        _watcherProcess.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                _logger.LogError("STDERR: {Data}", e.Data);
            }
        };
        _watcherProcess.BeginErrorReadLine();

        _logger.LogInformation("Started watcher process with tails mode (PID: {Pid})", _watcherProcess.Id);

        // Wait for server to be ready
        _client = new HttpClient { BaseAddress = new Uri($"http://{ApiConstants.Network.LocalhostIp}:{_port}") };
        await WaitForServerReadyAsync();
    }

    [Fact]
    public async Task TailsMode_ServerAcceptsTelemetry()
    {
        // Arrange - Create normal and error telemetry
        var normalTrace = CreateTraceRequest("normal-trace", isError: false);
        var errorTrace = CreateTraceRequest("error-trace", isError: true);
        var normalLog = CreateLogRequest("normal-log", isError: false);
        var errorLog = CreateLogRequest("error-log", isError: true);

        // Act - Send telemetry
        var normalTraceResponse = await _client!.PostAsync("/v1/traces",
            new ByteArrayContent(normalTrace.ToByteArray()),
            TestContext.Current.CancellationToken);

        var errorTraceResponse = await _client.PostAsync("/v1/traces",
            new ByteArrayContent(errorTrace.ToByteArray()),
            TestContext.Current.CancellationToken);

        var normalLogResponse = await _client.PostAsync("/v1/logs",
            new ByteArrayContent(normalLog.ToByteArray()),
            TestContext.Current.CancellationToken);

        var errorLogResponse = await _client.PostAsync("/v1/logs",
            new ByteArrayContent(errorLog.ToByteArray()),
            TestContext.Current.CancellationToken);

        // Assert - All requests accepted
        normalTraceResponse.StatusCode.Should().Be(HttpStatusCode.OK, "normal traces should be accepted");
        errorTraceResponse.StatusCode.Should().Be(HttpStatusCode.OK, "error traces should be accepted");
        normalLogResponse.StatusCode.Should().Be(HttpStatusCode.OK, "normal logs should be accepted");
        errorLogResponse.StatusCode.Should().Be(HttpStatusCode.OK, "error logs should be accepted");

        // Wait for files to be written by polling
        var filesWritten = await PollingHelpers.WaitForConditionAsync(
            condition: () => Directory.GetFiles(_tempDir, "*.ndjson", SearchOption.AllDirectories).Length > 0,
            timeoutMs: 5000,
            pollingIntervalMs: 100,
            cancellationToken: TestContext.Current.CancellationToken,
            logger: _logger,
            conditionDescription: "telemetry files to be written");

        filesWritten.Should().BeTrue("files should be written within timeout");

        // Verify files are still written to disk (tails is additional output, not replacement)
        var files = Directory.GetFiles(_tempDir, "*.ndjson", SearchOption.AllDirectories);
        files.Should().NotBeEmpty("files should still be written to disk in tails mode");

        _logger.LogInformation("Tails mode test completed - server accepted {FileCount} files", files.Length);
    }

    private ExportTraceServiceRequest CreateTraceRequest(string traceId, bool isError)
    {
        var span = new Span
        {
            TraceId = ByteString.CopyFromUtf8(traceId.PadRight(32, '0')),
            SpanId = ByteString.CopyFromUtf8("span123".PadRight(16, '0')),
            Name = isError ? "error-operation" : "normal-operation",
            Kind = Span.Types.SpanKind.Internal,
            StartTimeUnixNano = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000,
            EndTimeUnixNano = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000
        };

        if (isError)
        {
            span.Attributes.Add(new KeyValue
            {
                Key = "error",
                Value = new AnyValue { BoolValue = true }
            });
        }

        return new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = new Resource
                    {
                        Attributes = { new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "test-service" } } }
                    },
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Spans = { span }
                        }
                    }
                }
            }
        };
    }

    private ExportLogsServiceRequest CreateLogRequest(string body, bool isError)
    {
        var logRecord = new LogRecord
        {
            TimeUnixNano = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000,
            SeverityNumber = isError ? SeverityNumber.Error : SeverityNumber.Info,
            SeverityText = isError ? "ERROR" : "INFO",
            Body = new AnyValue { StringValue = body }
        };

        return new ExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ResourceLogs
                {
                    Resource = new Resource
                    {
                        Attributes = { new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "test-service" } } }
                    },
                    ScopeLogs =
                    {
                        new ScopeLogs
                        {
                            LogRecords = { logRecord }
                        }
                    }
                }
            }
        };
    }

    private async Task WaitForServerReadyAsync()
    {
        var maxAttempts = 30;
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                var response = await _client!.GetAsync("/api/status", TestContext.Current.CancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Server is ready (attempt {Attempt})", i + 1);
                    return;
                }
            }
            catch
            {
                // Ignore and retry
            }

            await Task.Delay(E2EConstants.Delays.ProcessingCompletionMs);
        }

        throw new InvalidOperationException($"Server did not become ready within {maxAttempts * E2EConstants.Delays.ProcessingCompletionMs}ms");
    }

    private (string fileName, string solutionRoot) GetDotnetExecutableInfo()
    {
        var dotnetPath = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        var fileName = string.IsNullOrEmpty(dotnetPath)
            ? "dotnet"
            : Path.Combine(dotnetPath, "dotnet");

        // Find solution root by looking for project.root marker
        var currentDir = Directory.GetCurrentDirectory();
        while (currentDir != null && !File.Exists(Path.Combine(currentDir, "project.root")))
        {
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }

        if (currentDir == null)
        {
            throw new InvalidOperationException("Could not find solution root (project.root marker)");
        }

        return (fileName, currentDir);
    }

    public async ValueTask DisposeAsync()
    {
        if (_watcherProcess != null && !_watcherProcess.HasExited)
        {
            try
            {
                // Send shutdown request
                if (_client != null)
                {
                    await _client.PostAsync("/api/stop", null, TestContext.Current.CancellationToken);

                    // Wait for process to exit by polling
                    await PollingHelpers.WaitForProcessExitAsync(
                        process: _watcherProcess,
                        timeoutMs: 5000,
                        pollingIntervalMs: 100,
                        cancellationToken: TestContext.Current.CancellationToken,
                        logger: _logger);
                }
            }
            catch
            {
                // If graceful shutdown fails, kill the process
            }

            if (!_watcherProcess.HasExited)
            {
                _watcherProcess.Kill();

                // Wait for process to exit after kill by polling
                await PollingHelpers.WaitForProcessExitAsync(
                    process: _watcherProcess,
                    timeoutMs: 2000,
                    pollingIntervalMs: 100,
                    cancellationToken: TestContext.Current.CancellationToken,
                    logger: _logger);
            }
        }

        _client?.Dispose();
    }

    public void Dispose()
    {
        _watcherProcess?.Dispose();

        // Clean up temp directory
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }

        PortAllocator.Release(_port);
    }
}

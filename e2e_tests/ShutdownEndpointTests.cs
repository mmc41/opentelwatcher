using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using Xunit;
using FluentAssertions;
using OpenTelWatcher.Configuration;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// Tests for the /api/stop endpoint.
/// Each test starts its own server instance to avoid interference with other tests.
/// </summary>
public class ShutdownEndpointTests : IDisposable
{
    private readonly List<Process> _processesToCleanup = new();
    private readonly List<int> _portsToRelease = new();
    private readonly string _solutionRoot;
    private readonly string _executablePath;
    private readonly ILogger<ShutdownEndpointTests> _logger;

    public ShutdownEndpointTests()
    {
        _solutionRoot = TestHelpers.SolutionRoot;
        _executablePath = TestHelpers.GetWatcherExecutablePath(_solutionRoot);
        _logger = TestLoggerFactory.CreateLogger<ShutdownEndpointTests>();
    }

    [Fact]
    public async Task Shutdown_ShouldReturn200WithSuccessMessage()
    {
        // Arrange
        var port = TestHelpers.GetRandomPort();
        _portsToRelease.Add(port);
        _logger.LogInformation("Test starting with port {Port}", port);

        await TestHelpers.EnsureNoInstanceRunningAsync(port);

        var process = await TestHelpers.StartServerAsync(_executablePath, port, _solutionRoot);
        _processesToCleanup.Add(process);
        _logger.LogInformation("Server started (PID: {ProcessId})", process.Id);

        await TestHelpers.WaitForServerHealthyAsync(port);
        _logger.LogInformation("Server healthy, sending shutdown request");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var shutdownUrl = $"http://{ApiConstants.Network.LocalhostIp}:{port}{E2EConstants.ApiEndpoints.Stop}";

        // Act
        var response = await client.PostAsync(shutdownUrl, null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        _logger.LogInformation("Shutdown response: {Content}", content);
        content.Should().Contain(E2EConstants.ExpectedValues.StopInitiatedMessage);

        // Verify server actually stopped
        var exited = await PollingHelpers.WaitForProcessExitAsync(
            process,
            timeoutMs: E2EConstants.Timeouts.ProcessExitMs,
            cancellationToken: TestContext.Current.CancellationToken,
            logger: _logger);

        exited.Should().BeTrue("server should have exited after shutdown");
        process.HasExited.Should().BeTrue("server should have exited after shutdown");
        _logger.LogInformation("Test completed successfully");
    }

    [Fact]
    public async Task Shutdown_ShouldCompleteResponseBeforeTermination()
    {
        // Arrange
        var port = TestHelpers.GetRandomPort();
        _portsToRelease.Add(port);
        _logger.LogInformation("Test starting with port {Port}", port);

        await TestHelpers.EnsureNoInstanceRunningAsync(port);

        var process = await TestHelpers.StartServerAsync(_executablePath, port, _solutionRoot);
        _processesToCleanup.Add(process);
        _logger.LogInformation("Server started (PID: {ProcessId})", process.Id);

        await TestHelpers.WaitForServerHealthyAsync(port);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var shutdownUrl = $"http://{ApiConstants.Network.LocalhostIp}:{port}/api/stop";

        // Act
        _logger.LogInformation("Measuring shutdown response time");
        var stopwatch = Stopwatch.StartNew();
        var response = await client.PostAsync(shutdownUrl, null, TestContext.Current.CancellationToken);
        stopwatch.Stop();
        _logger.LogInformation("Shutdown request completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue("shutdown request should complete successfully");

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().NotBeEmpty("response should contain shutdown message");

        // The response should complete quickly (before actual termination)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000,
            "shutdown endpoint should respond before server terminates");

        // Verify server eventually stopped
        var exited = await PollingHelpers.WaitForProcessExitAsync(
            process,
            timeoutMs: 5000,
            cancellationToken: TestContext.Current.CancellationToken,
            logger: _logger);

        exited.Should().BeTrue("server should terminate after responding");
        process.HasExited.Should().BeTrue("server should terminate after responding");
        _logger.LogInformation("Test completed successfully");
    }

    [Fact]
    public async Task Shutdown_DuringConcurrentRequests_ShouldAllowCompletion()
    {
        // Arrange
        var port = TestHelpers.GetRandomPort();
        _portsToRelease.Add(port);
        _logger.LogInformation("Test starting with port {Port}", port);

        await TestHelpers.EnsureNoInstanceRunningAsync(port);

        var process = await TestHelpers.StartServerAsync(_executablePath, port, _solutionRoot);
        _processesToCleanup.Add(process);
        _logger.LogInformation("Server started (PID: {ProcessId})", process.Id);

        await TestHelpers.WaitForServerHealthyAsync(port);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var healthUrl = $"http://{ApiConstants.Network.LocalhostIp}:{port}/healthz";
        var shutdownUrl = $"http://{ApiConstants.Network.LocalhostIp}:{port}/api/stop";

        // Act - Start several concurrent requests, then shutdown
        _logger.LogInformation("Sending 5 concurrent health check requests");
        var healthTasks = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < 5; i++)
        {
            healthTasks.Add(client.GetAsync(healthUrl, TestContext.Current.CancellationToken));
            await Task.Delay(E2EConstants.Delays.ShortCoordinationMs, TestContext.Current.CancellationToken);
        }

        // Trigger shutdown while requests are in flight
        _logger.LogInformation("Triggering shutdown while requests in flight");
        var shutdownTask = client.PostAsync(shutdownUrl, null, TestContext.Current.CancellationToken);

        // Wait for all tasks to complete
        await Task.WhenAll(healthTasks.Concat(new[] { shutdownTask }));
        _logger.LogInformation("All requests completed");

        // Assert
        var shutdownResponse = await shutdownTask;
        shutdownResponse.IsSuccessStatusCode.Should().BeTrue("shutdown should succeed");

        // All health check requests should have completed (either success or graceful failure)
        foreach (var healthTask in healthTasks)
        {
            healthTask.IsCompletedSuccessfully.Should().BeTrue(
                "concurrent requests should complete before shutdown takes effect");
        }

        // Verify server stopped
        var exited = await PollingHelpers.WaitForProcessExitAsync(
            process,
            timeoutMs: 5000,
            cancellationToken: TestContext.Current.CancellationToken,
            logger: _logger);

        exited.Should().BeTrue("server should stop after shutdown");
        process.HasExited.Should().BeTrue("server should stop after shutdown");
        _logger.LogInformation("Test completed successfully");
    }

    public void Dispose()
    {
        // Cleanup any processes that didn't exit gracefully
        foreach (var process in _processesToCleanup)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                }
                process.Dispose();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        // Release all allocated ports back to the pool
        foreach (var port in _portsToRelease)
        {
            PortAllocator.Release(port);
        }
    }
}

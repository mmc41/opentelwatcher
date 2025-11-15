using System.Diagnostics;
using System.Net;
using Xunit;
using FluentAssertions;
using OpenTelWatcher.Configuration;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// Tests for the /api/shutdown endpoint.
/// Each test starts its own server instance to avoid interference with other tests.
/// </summary>
public class ShutdownEndpointTests : IDisposable
{
    private readonly List<Process> _processesToCleanup = new();
    private readonly string _solutionRoot;
    private readonly string _executablePath;

    public ShutdownEndpointTests()
    {
        _solutionRoot = TestHelpers.FindSolutionRoot();
        _executablePath = TestHelpers.GetWatcherExecutablePath(_solutionRoot);
    }

    [Fact]
    public async Task Shutdown_ShouldReturn200WithSuccessMessage()
    {
        // Arrange
        var port = TestHelpers.GetRandomPort();
        await TestHelpers.EnsureNoInstanceRunningAsync(port);

        var process = await TestHelpers.StartServerAsync(_executablePath, port, _solutionRoot);
        _processesToCleanup.Add(process);

        await TestHelpers.WaitForServerHealthyAsync(port);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var shutdownUrl = $"http://{ApiConstants.Network.LocalhostIp}:{port}/api/shutdown";

        // Act
        var response = await client.PostAsync(shutdownUrl, null, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().Contain("Shutdown initiated");

        // Verify server actually stopped
        await Task.Delay(3000, TestContext.Current.CancellationToken); // Give time for graceful shutdown
        process.HasExited.Should().BeTrue("server should have exited after shutdown");
    }

    [Fact]
    public async Task Shutdown_ShouldCompleteResponseBeforeTermination()
    {
        // Arrange
        var port = TestHelpers.GetRandomPort();
        await TestHelpers.EnsureNoInstanceRunningAsync(port);

        var process = await TestHelpers.StartServerAsync(_executablePath, port, _solutionRoot);
        _processesToCleanup.Add(process);

        await TestHelpers.WaitForServerHealthyAsync(port);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var shutdownUrl = $"http://{ApiConstants.Network.LocalhostIp}:{port}/api/shutdown";

        // Act
        var stopwatch = Stopwatch.StartNew();
        var response = await client.PostAsync(shutdownUrl, null, TestContext.Current.CancellationToken);
        stopwatch.Stop();

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue("shutdown request should complete successfully");

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().NotBeEmpty("response should contain shutdown message");

        // The response should complete quickly (before actual termination)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000,
            "shutdown endpoint should respond before server terminates");

        // Verify server eventually stopped
        await Task.Delay(3000, TestContext.Current.CancellationToken);
        process.HasExited.Should().BeTrue("server should terminate after responding");
    }

    [Fact]
    public async Task Shutdown_DuringConcurrentRequests_ShouldAllowCompletion()
    {
        // Arrange
        var port = TestHelpers.GetRandomPort();
        await TestHelpers.EnsureNoInstanceRunningAsync(port);

        var process = await TestHelpers.StartServerAsync(_executablePath, port, _solutionRoot);
        _processesToCleanup.Add(process);

        await TestHelpers.WaitForServerHealthyAsync(port);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var healthUrl = $"http://{ApiConstants.Network.LocalhostIp}:{port}/healthz";
        var shutdownUrl = $"http://{ApiConstants.Network.LocalhostIp}:{port}/api/shutdown";

        // Act - Start several concurrent requests, then shutdown
        var healthTasks = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < 5; i++)
        {
            healthTasks.Add(client.GetAsync(healthUrl, TestContext.Current.CancellationToken));
            await Task.Delay(50, TestContext.Current.CancellationToken); // Stagger the requests slightly
        }

        // Trigger shutdown while requests are in flight
        var shutdownTask = client.PostAsync(shutdownUrl, null, TestContext.Current.CancellationToken);

        // Wait for all tasks to complete
        await Task.WhenAll(healthTasks.Concat(new[] { shutdownTask }));

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
        await Task.Delay(3000, TestContext.Current.CancellationToken);
        process.HasExited.Should().BeTrue("server should stop after shutdown");
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
    }
}

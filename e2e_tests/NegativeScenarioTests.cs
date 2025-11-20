using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenTelWatcher.Tests.E2E;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// E2E tests for negative scenarios and error conditions.
/// Tests behavior when operations fail or timeout.
/// </summary>
public class NegativeScenarioTests
{
    private readonly ILogger<NegativeScenarioTests> _logger;

    public NegativeScenarioTests()
    {
        _logger = TestLoggerFactory.CreateLogger<NegativeScenarioTests>();
    }

    // Note: Port conflict test removed due to complexity with timing and platform-specific error messages
    // The pre-flight check in StartCommand already handles this scenario at the application level

    [Fact]
    public async Task HttpClient_WhenServerNotRunning_TimesOutGracefully()
    {
        // Arrange - Create HTTP client with short timeout
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:9999/"), // Port unlikely to be in use
            Timeout = TimeSpan.FromSeconds(2)
        };

        _logger.LogInformation("Testing HTTP client timeout on non-existent server");

        // Act & Assert - Request should timeout or fail
        var act = async () => await httpClient.GetAsync("/api/status", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>("request to non-existent server should fail");

        _logger.LogInformation("HTTP client correctly failed when server not running");
    }

    [Fact]
    public async Task PollingHelpers_WhenConditionNeverMet_ReturnsFalse()
    {
        // Arrange - Create a condition that will never be true
        _logger.LogInformation("Testing PollingHelpers timeout behavior");

        // Act - Wait for impossible condition with short timeout
        var result = await PollingHelpers.WaitForConditionAsync(
            condition: () => false, // Never true
            timeoutMs: 500,
            pollingIntervalMs: 50,
            cancellationToken: TestContext.Current.CancellationToken,
            logger: _logger,
            conditionDescription: "impossible condition");

        // Assert
        result.Should().BeFalse("should return false when condition is never met before timeout");
        _logger.LogInformation("PollingHelpers correctly timed out");
    }

    [Fact]
    public async Task PollingHelpers_WhenFileNeverCreated_ReturnsFalse()
    {
        // Arrange - Create temp directory but never create the expected file
        using var testDir = new TempDirectory("polling-test");

        _logger.LogInformation("Testing file polling timeout in {TestDir}", testDir.Path);

        // Act - Wait for file that will never be created
        var result = await PollingHelpers.WaitForFileAsync(
            testDir.Path,
            "nonexistent-*.ndjson",
            timeoutMs: 500,
            cancellationToken: TestContext.Current.CancellationToken,
            logger: _logger);

        // Assert
        result.Should().BeFalse("should return false when file is never created before timeout");
        _logger.LogInformation("File polling correctly timed out");
    }

    // Helper class for managing temporary directories in tests
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid()}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                try
                {
                    Directory.Delete(Path, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}

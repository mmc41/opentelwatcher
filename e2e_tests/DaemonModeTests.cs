using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// E2E tests for daemon mode (--daemon flag).
/// These tests verify that the watcher works correctly when started in daemon/background mode.
/// Uses DaemonModeFixture which starts the watcher subprocess with --daemon flag.
/// </summary>
[Collection("Watcher Server Daemon")]
public class DaemonModeTests
{
    private readonly DaemonModeFixture _fixture;
    private readonly HttpClient _client;
    private readonly ILogger<DaemonModeTests> _logger;

    public DaemonModeTests(DaemonModeFixture fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
        _client = fixture.Client;
        _logger = TestLoggerFactory.CreateLogger<DaemonModeTests>();
    }

    #region Daemon Mode Tests

    [Fact]
    public async Task DaemonMode_ServerRespondsToRequests()
    {
        // Act - Verify daemon-started server is accessible
        _logger.LogInformation("Testing daemon mode server accessibility");
        var response = await _client.GetAsync("/api/status", TestContext.Current.CancellationToken);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue("daemon-started server should respond to API requests");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var application = json.GetProperty("application").GetString();

        _logger.LogInformation("Daemon server confirmed: Application={Application}", application);

        application.Should().Be("OpenTelWatcher", "should be running the correct application");
    }

    #endregion
}
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
    public async Task DaemonMode_VersionEndpoint_ReturnsVersionInfo()
    {
        // Act
        _logger.LogInformation("Testing daemon mode version endpoint");
        var response = await _client.GetAsync("/api/status", TestContext.Current.CancellationToken);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue("daemon-started server should respond to version endpoint");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var application = json.GetProperty("application").GetString();
        var version = json.GetProperty("version").GetString();
        var major = json.GetProperty("versionComponents").GetProperty("major").GetInt32();

        _logger.LogInformation("Daemon server: Application={Application}, Version={Version}, Major={Major}",
            application, version, major);

        application.Should().Be("OpenTelWatcher", "should return application name");
        version.Should().NotBeNullOrWhiteSpace("should return version");
        major.Should().BeGreaterThanOrEqualTo(0, "major version should be valid");
    }

    [Fact]
    public async Task DaemonMode_DiagnoseEndpoint_ReturnsHealthStatus()
    {
        // Act
        var response = await _client.GetAsync("/api/status", TestContext.Current.CancellationToken);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue("daemon-started server should respond to diagnose endpoint");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("health").GetProperty("status").GetString().Should().NotBeNullOrWhiteSpace("should return health status");
        json.GetProperty("configuration").GetProperty("outputDirectory").GetString().Should().NotBeNullOrWhiteSpace("should return output directory");
    }

    [Fact]
    public async Task DaemonMode_StatusPage_IsAccessible()
    {
        // Act
        var response = await _client.GetAsync("/", TestContext.Current.CancellationToken);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue("daemon-started server should serve status page");

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().Contain("OpenTelWatcher Status", "should display status page title");
    }

    [Fact]
    public async Task DaemonMode_SwaggerEndpoint_IsAccessible()
    {
        // Act
        var response = await _client.GetAsync("/swagger/index.html", TestContext.Current.CancellationToken);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue("daemon-started server should serve Swagger UI");
    }

    [Fact]
    public async Task DaemonMode_OpenApiSpec_IsAccessible()
    {
        // Act
        var response = await _client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue("daemon-started server should serve OpenAPI spec");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("openapi").GetString().Should().StartWith("3.", "should be OpenAPI 3.x spec");
        json.GetProperty("info").GetProperty("title").GetString().Should().Contain("OpenTelWatcher", "should have correct API title");
    }

    [Fact]
    public async Task DaemonMode_MultipleRequests_HandledSuccessfully()
    {
        // Act - make multiple concurrent requests
        _logger.LogInformation("Sending 10 concurrent requests to daemon server");
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _client.GetAsync("/api/status", TestContext.Current.CancellationToken))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        _logger.LogInformation("All {Count} concurrent requests completed", responses.Length);

        // Assert
        responses.Should().AllSatisfy(r => r.IsSuccessStatusCode.Should().BeTrue("all requests should succeed"));
    }

    #endregion
}
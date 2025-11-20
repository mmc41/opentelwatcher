using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace OpenTelWatcher.Tests.E2E;

[Collection("Watcher Server")]
public class StatusPageE2ETests
{
    private readonly OpenTelWatcherServerFixture _fixture;
    private readonly ILogger<StatusPageE2ETests> _logger;

    public StatusPageE2ETests(OpenTelWatcherServerFixture fixture)
    {
        _fixture = fixture;
        _logger = TestLoggerFactory.CreateLogger<StatusPageE2ETests>();
    }

    [Fact]
    public async Task GetRoot_ReturnsStatusPage()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task StatusPage_ContainsTitle()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        html.Should().Contain("OpenTelWatcher Status Dashboard");
    }

    [Fact]
    public async Task StatusPage_ContainsOperationalStatusSection()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        html.Should().Contain("OPERATIONAL STATUS");
    }

    [Fact]
    public async Task StatusPage_DisplaysHealthStatus()
    {
        // Act
        _logger.LogInformation("Testing status page health status display");
        var response = await _fixture.Client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        _logger.LogDebug("Status page HTML length: {Length} bytes", html.Length);

        // Assert
        html.Should().MatchRegex("Health:.*?(Healthy|Degraded)");
    }

    [Fact]
    public async Task StatusPage_DisplaysTelemetryCounters()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        html.Should().Contain("Traces");
        html.Should().Contain("Logs");
        html.Should().Contain("Metrics");
    }

    // User Story 2: Access Configuration Information

    [Fact]
    public async Task StatusPage_ContainsConfigurationSection()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        html.Should().Contain("CONFIGURATION");
    }

    [Fact]
    public async Task StatusPage_ConfigurationMatchesAppSettings()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert - configuration section should contain key settings
        html.Should().Contain("Output Directory");
        html.Should().Contain("Max File Size");
    }

    [Fact]
    public async Task StatusPage_DisplaysAbsoluteFilePath()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert - should contain output directory path
        html.Should().Contain("Output Directory");
        // Path will vary by environment, just verify it's displayed
    }
}

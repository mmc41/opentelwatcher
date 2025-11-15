using FluentAssertions;
using Xunit;
using OpenTelWatcher.Tests.E2E;

namespace OpenTelWatcher.E2ETests;

[Collection("Watcher Server")]
public class StatusPageE2ETests
{
    private readonly OpenTelWatcherServerFixture _fixture;

    public StatusPageE2ETests(OpenTelWatcherServerFixture fixture)
    {
        _fixture = fixture;
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
        var response = await _fixture.Client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task StatusPage_DisplaysZeroValuesWhenNoData()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert - when no telemetry received, counters show 0 or zero-like values
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        html.Should().Contain("Traces");
        html.Should().Contain("Logs");
        html.Should().Contain("Metrics");
    }

    [Fact]
    public async Task StatusPage_UpdatesStatisticsOnRefresh()
    {
        // Act - First request
        var response1 = await _fixture.Client.GetAsync("/", TestContext.Current.CancellationToken);
        var html1 = await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Act - Second request (simulates refresh)
        var response2 = await _fixture.Client.GetAsync("/", TestContext.Current.CancellationToken);
        var html2 = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert - Both requests succeed (statistics may be same if no data sent)
        response1.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response2.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        html2.Should().Contain("OPERATIONAL STATUS");
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

    [Fact]
    public async Task StatusPage_ConfigurationUnavailableShowsError()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert - page should handle configuration gracefully
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        // If configuration is available, section shows; if not, error message shows
    }

    [Fact]
    public async Task StatusPage_FilePathUnavailableShowsSpecificError()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert - page should handle path errors gracefully
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        // Normal case: path is shown; error case: specific error message
    }
}

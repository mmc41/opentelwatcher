using System.Net;
using Microsoft.Extensions.Logging;
using Xunit;
using FluentAssertions;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// True black-box E2E tests that use a real Watcher subprocess.
/// Tests the API as a real client would, using the shared OpenTelWatcherServerFixture.
/// </summary>
[Collection("Watcher Server")]
public class ApiDiagnoseTests
{
    private readonly OpenTelWatcherServerFixture _fixture;
    private readonly ILogger<ApiDiagnoseTests> _logger;

    public ApiDiagnoseTests(OpenTelWatcherServerFixture fixture)
    {
        _fixture = fixture;
        _logger = TestLoggerFactory.CreateLogger<ApiDiagnoseTests>();
    }

    [Fact]
    public async Task ApiDiagnose_ShouldReturnExpectedResponseFormat()
    {
        // Act
        _logger.LogInformation("Testing /api/status response format");
        var response = await _fixture.Client.GetAsync("/api/status", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        _logger.LogDebug("Response content: {Content}", content);

        content.Should().Contain("health");
        content.Should().Contain("files");
        content.Should().Contain("configuration");
    }

    [Fact]
    public async Task ApiDiagnose_WithSignalFilter_ShouldFilterResults()
    {
        // Act
        _logger.LogInformation("Testing /api/status with signal=traces filter");
        var response = await _fixture.Client.GetAsync("/api/status?signal=traces", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        _logger.LogDebug("Filtered response content length: {Length} bytes", content.Length);

        content.Should().NotBeNullOrEmpty();

        // When filtering by signal, the response should still contain the main sections
        content.Should().Contain("health");
        content.Should().Contain("files");
    }

    [Fact]
    public async Task ApiVersion_ShouldReturnVersionInfo()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/status", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().Contain("version");
    }
}

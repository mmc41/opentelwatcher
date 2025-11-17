using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Xunit;
using FluentAssertions;

namespace OpenTelWatcher.Tests.E2E;

[Collection("Watcher Server")]
public class SwaggerUiTests
{
    private readonly OpenTelWatcherServerFixture _fixture;
    private readonly ILogger<SwaggerUiTests> _logger;

    public SwaggerUiTests(OpenTelWatcherServerFixture fixture)
    {
        _fixture = fixture;
        _logger = TestLoggerFactory.CreateLogger<SwaggerUiTests>();
    }

    [Fact]
    public async Task SwaggerUI_ShouldLoadSuccessfully()
    {
        // Act
        _logger.LogInformation("Testing Swagger UI loads successfully");
        var response = await _fixture.Client.GetAsync("/swagger/index.html", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        _logger.LogDebug("Swagger UI content length: {Length} bytes", content.Length);

        content.Should().Contain("swagger-ui");
        content.Should().Contain("swagger-ui-bundle.js");
    }

    [Fact]
    public async Task OpenApiSpec_ShouldBeAvailable()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().Contain("\"title\"");
        content.Should().Contain("OpenTelWatcher API");
    }

    [Fact]
    public async Task OpenApiSpec_ShouldContainAllEndpoints()
    {
        // Arrange
        var expectedPaths = new[] { "/v1/traces", "/v1/logs", "/v1/metrics", "/healthz", "/api/status", "/api/stop" };

        // Act
        var response = await _fixture.Client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var spec = JsonDocument.Parse(content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var paths = spec.RootElement.GetProperty("paths");
        foreach (var expectedPath in expectedPaths)
        {
            paths.TryGetProperty(expectedPath, out _).Should().BeTrue($"OpenAPI spec should contain path {expectedPath}");
        }
    }
}

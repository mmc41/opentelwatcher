using OpenTelWatcher.E2ETests;
using System.Net;
using System.Text.Json;
using Xunit;
using FluentAssertions;

namespace OpenTelWatcher.Tests.E2E;

[Collection("Watcher Server")]
public class SwaggerUiTests
{
    private readonly OpenTelWatcherServerFixture _fixture;

    public SwaggerUiTests(OpenTelWatcherServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SwaggerUI_ShouldLoadSuccessfully()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/swagger/index.html", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
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
        var expectedPaths = new[] { "/v1/traces", "/v1/logs", "/v1/metrics", "/healthz", "/api/info", "/api/shutdown" };

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

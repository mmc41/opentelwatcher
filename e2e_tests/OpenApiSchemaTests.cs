using OpenTelWatcher.E2ETests;
using System.Net;
using System.Text.Json;
using Xunit;
using FluentAssertions;

namespace OpenTelWatcher.Tests.E2E;

[Collection("Watcher Server")]
public class OpenApiSchemaTests
{
    private readonly OpenTelWatcherServerFixture _fixture;

    public OpenApiSchemaTests(OpenTelWatcherServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OpenApiSchema_ShouldHaveRequiredProperties()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var spec = JsonDocument.Parse(content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var root = spec.RootElement;
        root.TryGetProperty("openapi", out _).Should().BeTrue("Schema should have 'openapi' version");
        root.TryGetProperty("info", out _).Should().BeTrue("Schema should have 'info' section");
        root.TryGetProperty("paths", out _).Should().BeTrue("Schema should have 'paths' section");

        // Verify info section
        var info = root.GetProperty("info");
        info.TryGetProperty("title", out _).Should().BeTrue("Info should have 'title'");
        info.TryGetProperty("version", out _).Should().BeTrue("Info should have 'version'");
        info.TryGetProperty("description", out _).Should().BeTrue("Info should have 'description'");
    }

    [Fact]
    public async Task OpenApiSchema_EndpointsShouldHaveSummaryAndDescription()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var spec = JsonDocument.Parse(content);

        // Assert
        var paths = spec.RootElement.GetProperty("paths");
        var managementEndpoints = new[] { "/api/info", "/api/shutdown" };

        foreach (var endpoint in managementEndpoints)
        {
            paths.TryGetProperty(endpoint, out var endpointSpec).Should().BeTrue($"Should have {endpoint}");

            // Get the HTTP method (get or post)
            var method = endpointSpec.EnumerateObject().First();
            var operation = method.Value;

            operation.TryGetProperty("summary", out _).Should().BeTrue($"{endpoint} should have summary");
            operation.TryGetProperty("description", out _).Should().BeTrue($"{endpoint} should have description");
        }
    }
}

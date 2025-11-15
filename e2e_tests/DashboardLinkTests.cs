using OpenTelWatcher.E2ETests;
using System.Net;
using Xunit;
using FluentAssertions;

namespace OpenTelWatcher.Tests.E2E;

[Collection("Watcher Server")]
public class DashboardLinkTests
{
    private readonly OpenTelWatcherServerFixture _fixture;

    public DashboardLinkTests(OpenTelWatcherServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Dashboard_ShouldContainSwaggerLink()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("/swagger");
        content.Should().Contain("Swagger UI");
    }

    [Fact]
    public async Task Dashboard_ShouldContainOpenApiLink()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("/openapi/v1.json");
        content.Should().Contain("OpenAPI");
    }

    [Fact]
    public async Task Dashboard_ShouldHaveApiDocumentationSection()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("API DOCUMENTATION");
    }
}

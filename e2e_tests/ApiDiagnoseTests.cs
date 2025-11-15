using System.Net;
using Xunit;
using FluentAssertions;
using OpenTelWatcher.E2ETests;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// True black-box E2E tests that use a real Watcher subprocess.
/// Tests the API as a real client would, using the shared OpenTelWatcherServerFixture.
/// </summary>
[Collection("Watcher Server")]
public class ApiDiagnoseTests
{
    private readonly OpenTelWatcherServerFixture _fixture;

    public ApiDiagnoseTests(OpenTelWatcherServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ApiDiagnose_ShouldReturnExpectedResponseFormat()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/info", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().Contain("health");
        content.Should().Contain("files");
        content.Should().Contain("configuration");
    }

    [Fact]
    public async Task ApiDiagnose_WithSignalFilter_ShouldFilterResults()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/info?signal=traces", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().NotBeNullOrEmpty();

        // When filtering by signal, the response should still contain the main sections
        content.Should().Contain("health");
        content.Should().Contain("files");
    }

    [Fact]
    public async Task ApiVersion_ShouldReturnVersionInfo()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/info", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().Contain("version");
    }
}

using OpenTelWatcher.E2ETests;
using OpenTelWatcher.Tests.E2E;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace E2ETests;

[Collection("Watcher Server")]
public class VersionApiTests
{
    private readonly OpenTelWatcherServerFixture _fixture;

    public VersionApiTests(OpenTelWatcherServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task VersionEndpoint_ReturnsCorrectStructure()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/status", TestContext.Current.CancellationToken);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var info = JsonSerializer.Deserialize<StatusResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        info.Should().NotBeNull();
        info!.Application.Should().Be("OpenTelWatcher");
        info.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
        info.VersionComponents.Should().NotBeNull();
        info.VersionComponents.Major.Should().BeGreaterThanOrEqualTo(0);
        info.VersionComponents.Minor.Should().BeGreaterThanOrEqualTo(0);
        info.VersionComponents.Patch.Should().BeGreaterThanOrEqualTo(0);
        info.ProcessId.Should().BeGreaterThan(0, "process ID should be a valid positive integer");
    }

    [Fact]
    public async Task VersionEndpoint_ReturnsOpenTelWatcherIdentifier()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/status", TestContext.Current.CancellationToken);
        var info = await response.Content.ReadFromJsonAsync<StatusResponse>(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        info.Should().NotBeNull();
        info!.Application.Should().Be("OpenTelWatcher");
    }

    [Fact]
    public async Task VersionEndpoint_ReturnsConsistentVersion()
    {
        // Act - Call twice
        var response1 = await _fixture.Client.GetAsync("/api/status", TestContext.Current.CancellationToken);
        var info1 = await response1.Content.ReadFromJsonAsync<StatusResponse>(cancellationToken: TestContext.Current.CancellationToken);

        var response2 = await _fixture.Client.GetAsync("/api/status", TestContext.Current.CancellationToken);
        var info2 = await response2.Content.ReadFromJsonAsync<StatusResponse>(cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Should be identical
        info1.Should().BeEquivalentTo(info2);
    }
}

public record StatusResponse
{
    public required string Application { get; init; }
    public required string Version { get; init; }
    public required VersionComponents VersionComponents { get; init; }
    public required int ProcessId { get; init; }
    public required int Port { get; init; }
    public required HealthInfo Health { get; init; }
    public required FileStats Files { get; init; }
    public required ConfigInfo Configuration { get; init; }
}

public record VersionComponents
{
    public required int Major { get; init; }
    public required int Minor { get; init; }
    public required int Patch { get; init; }
}

public record HealthInfo
{
    public required string Status { get; init; }
    public required int ConsecutiveErrors { get; init; }
    public required List<string> RecentErrors { get; init; }
}

public record FileStats
{
    public required int Count { get; init; }
    public required long TotalSizeBytes { get; init; }
}

public record ConfigInfo
{
    public required string OutputDirectory { get; init; }
}

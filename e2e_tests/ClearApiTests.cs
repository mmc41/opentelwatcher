using OpenTelWatcher.E2ETests;
using OpenTelWatcher.Tests.E2E;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenTelWatcher.CLI.Models;
using Xunit;

namespace E2ETests;

[Collection("Watcher Server")]
public class ClearApiTests
{
    private readonly OpenTelWatcherServerFixture _fixture;

    public ClearApiTests(OpenTelWatcherServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ClearEndpoint_WithNoFiles_ReturnsSuccessWithZeroDeleted()
    {
        // Arrange - First clear any existing files
        await _fixture.Client.PostAsync("/api/clear", null, TestContext.Current.CancellationToken);

        // Act - Call clear again
        var response = await _fixture.Client.PostAsync("/api/clear", null, TestContext.Current.CancellationToken);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        var clearResponse = await response.Content.ReadFromJsonAsync<ClearResponse>(
            cancellationToken: TestContext.Current.CancellationToken);

        clearResponse.Should().NotBeNull();
        clearResponse!.Success.Should().BeTrue();
        clearResponse.FilesDeleted.Should().Be(0);
        clearResponse.Message.Should().Contain("0");
        clearResponse.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ClearEndpoint_ReturnsCorrectStructure()
    {
        // Act
        var response = await _fixture.Client.PostAsync("/api/clear", null, TestContext.Current.CancellationToken);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var clearResponse = JsonSerializer.Deserialize<ClearResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        clearResponse.Should().NotBeNull();
        clearResponse!.Success.Should().BeTrue();
        clearResponse.FilesDeleted.Should().BeGreaterThanOrEqualTo(0);
        clearResponse.Message.Should().NotBeNullOrEmpty();
        clearResponse.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ClearEndpoint_DeletesFilesAfterIngestion()
    {
        // Arrange - Get info to check current file count
        var infoBefore = await _fixture.Client.GetFromJsonAsync<InfoResponse>("/api/info",
            TestContext.Current.CancellationToken);

        // Send some telemetry data if no files exist
        if (infoBefore!.Files.Count == 0)
        {
            // Send minimal OTLP trace data (protobuf encoded)
            var traceData = new byte[] { 0x0A, 0x00 }; // Minimal valid protobuf
            await _fixture.Client.PostAsync("/v1/traces",
                new ByteArrayContent(traceData),
                TestContext.Current.CancellationToken);

            // Wait a moment for file to be written
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        // Act - Clear files
        var clearResponse = await _fixture.Client.PostAsync("/api/clear", null,
            TestContext.Current.CancellationToken);

        // Assert
        clearResponse.IsSuccessStatusCode.Should().BeTrue();
        var result = await clearResponse.Content.ReadFromJsonAsync<ClearResponse>(
            cancellationToken: TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.FilesDeleted.Should().BeGreaterThanOrEqualTo(0);

        // Verify files are actually deleted
        var infoAfter = await _fixture.Client.GetFromJsonAsync<InfoResponse>("/api/info",
            TestContext.Current.CancellationToken);

        infoAfter!.Files.Count.Should().Be(0, "all files should be deleted after clear");
        infoAfter.Files.TotalSizeBytes.Should().Be(0);
    }

    [Fact]
    public async Task ClearEndpoint_MultipleCallsSucceed()
    {
        // Act - Call clear multiple times
        var response1 = await _fixture.Client.PostAsync("/api/clear", null, TestContext.Current.CancellationToken);
        var response2 = await _fixture.Client.PostAsync("/api/clear", null, TestContext.Current.CancellationToken);
        var response3 = await _fixture.Client.PostAsync("/api/clear", null, TestContext.Current.CancellationToken);

        // Assert - All calls should succeed
        response1.IsSuccessStatusCode.Should().BeTrue();
        response2.IsSuccessStatusCode.Should().BeTrue();
        response3.IsSuccessStatusCode.Should().BeTrue();

        var result1 = await response1.Content.ReadFromJsonAsync<ClearResponse>(
            cancellationToken: TestContext.Current.CancellationToken);
        var result2 = await response2.Content.ReadFromJsonAsync<ClearResponse>(
            cancellationToken: TestContext.Current.CancellationToken);
        var result3 = await response3.Content.ReadFromJsonAsync<ClearResponse>(
            cancellationToken: TestContext.Current.CancellationToken);

        result1!.Success.Should().BeTrue();
        result2!.Success.Should().BeTrue();
        result3!.Success.Should().BeTrue();

        // Second and third calls should have zero files deleted
        result2.FilesDeleted.Should().Be(0);
        result3.FilesDeleted.Should().Be(0);
    }
}

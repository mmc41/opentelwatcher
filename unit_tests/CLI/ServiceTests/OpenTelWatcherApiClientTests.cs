using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using UnitTests.Helpers;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.CLI.Services;
using Xunit;

namespace UnitTests.CLI.ServiceTests;

public class OpenTelWatcherApiClientTests
{
    [Fact]
    public async Task GetInstanceStatusAsync_WhenServiceNotRunning_ReturnsNotRunningStatus()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            throw new HttpRequestException("Connection refused");
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:4318") };
        var client = new OpenTelWatcherApiClient(httpClient, TestLoggerFactory.CreateLogger<OpenTelWatcherApiClient>());

        // Act
        var status = await client.GetInstanceStatusAsync(new Version(1, 0, 0));

        // Assert
        status.IsRunning.Should().BeFalse();
        status.Version.Should().BeNull();
    }

    [Fact]
    public async Task GetInstanceStatusAsync_WhenVersionMatches_ReturnsCompatible()
    {
        // Arrange
        var statusResponse = new StatusResponse
        {
            Application = "OpenTelWatcher",
            Version = "1.5.0",
            VersionComponents = new VersionComponents { Major = 1, Minor = 5, Patch = 0 },
            ProcessId = 12345,
            Port = 4318,
            UptimeSeconds = 3600,
            Health = new DiagnoseHealth { Status = "healthy", ConsecutiveErrors = 0, RecentErrors = new List<string>() },
            Telemetry = new TelemetryStatistics
            {
                Traces = new TelemetryTypeStats { Requests = 0 },
                Logs = new TelemetryTypeStats { Requests = 0 },
                Metrics = new TelemetryTypeStats { Requests = 0 }
            },
            Files = new StatusFileStatistics
            {
                Count = 0,
                TotalSizeBytes = 0,
                Breakdown = new FileBreakdown
                {
                    Traces = new FileTypeStats { Count = 0, SizeBytes = 0 },
                    Logs = new FileTypeStats { Count = 0, SizeBytes = 0 },
                    Metrics = new FileTypeStats { Count = 0, SizeBytes = 0 }
                }
            },
            Configuration = new DiagnoseConfiguration { OutputDirectory = "./telemetry-data" }
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(statusResponse)
            };
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:4318") };
        var client = new OpenTelWatcherApiClient(httpClient, TestLoggerFactory.CreateLogger<OpenTelWatcherApiClient>());

        // Act
        var status = await client.GetInstanceStatusAsync(new Version(1, 0, 0));

        // Assert
        status.IsRunning.Should().BeTrue();
        status.IsCompatible.Should().BeTrue();
        status.Version.Should().NotBeNull();
        status.Version!.Application.Should().Be("OpenTelWatcher");
    }

    [Fact]
    public async Task GetInstanceStatusAsync_WhenMajorVersionDiffers_ReturnsIncompatible()
    {
        // Arrange
        var statusResponse = new StatusResponse
        {
            Application = "OpenTelWatcher",
            Version = "2.0.0",
            VersionComponents = new VersionComponents { Major = 2, Minor = 0, Patch = 0 },
            ProcessId = 12345,
            Port = 4318,
            UptimeSeconds = 3600,
            Health = new DiagnoseHealth { Status = "healthy", ConsecutiveErrors = 0, RecentErrors = new List<string>() },
            Telemetry = new TelemetryStatistics
            {
                Traces = new TelemetryTypeStats { Requests = 0 },
                Logs = new TelemetryTypeStats { Requests = 0 },
                Metrics = new TelemetryTypeStats { Requests = 0 }
            },
            Files = new StatusFileStatistics
            {
                Count = 0,
                TotalSizeBytes = 0,
                Breakdown = new FileBreakdown
                {
                    Traces = new FileTypeStats { Count = 0, SizeBytes = 0 },
                    Logs = new FileTypeStats { Count = 0, SizeBytes = 0 },
                    Metrics = new FileTypeStats { Count = 0, SizeBytes = 0 }
                }
            },
            Configuration = new DiagnoseConfiguration { OutputDirectory = "./telemetry-data" }
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(statusResponse)
            };
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:4318") };
        var client = new OpenTelWatcherApiClient(httpClient, TestLoggerFactory.CreateLogger<OpenTelWatcherApiClient>());

        // Act
        var status = await client.GetInstanceStatusAsync(new Version(1, 0, 0));

        // Assert
        status.IsRunning.Should().BeTrue();
        status.IsCompatible.Should().BeFalse();
        status.IncompatibilityReason.Should().Contain("major version");
    }

    [Fact]
    public async Task GetInstanceStatusAsync_WhenApplicationNameWrong_ReturnsIncompatible()
    {
        // Arrange
        var statusResponse = new StatusResponse
        {
            Application = "SomeOtherApp",
            Version = "1.0.0",
            VersionComponents = new VersionComponents { Major = 1, Minor = 0, Patch = 0 },
            ProcessId = 12345,
            Port = 4318,
            UptimeSeconds = 3600,
            Health = new DiagnoseHealth { Status = "healthy", ConsecutiveErrors = 0, RecentErrors = new List<string>() },
            Telemetry = new TelemetryStatistics
            {
                Traces = new TelemetryTypeStats { Requests = 0 },
                Logs = new TelemetryTypeStats { Requests = 0 },
                Metrics = new TelemetryTypeStats { Requests = 0 }
            },
            Files = new StatusFileStatistics
            {
                Count = 0,
                TotalSizeBytes = 0,
                Breakdown = new FileBreakdown
                {
                    Traces = new FileTypeStats { Count = 0, SizeBytes = 0 },
                    Logs = new FileTypeStats { Count = 0, SizeBytes = 0 },
                    Metrics = new FileTypeStats { Count = 0, SizeBytes = 0 }
                }
            },
            Configuration = new DiagnoseConfiguration { OutputDirectory = "./telemetry-data" }
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(statusResponse)
            };
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:4318") };
        var client = new OpenTelWatcherApiClient(httpClient, TestLoggerFactory.CreateLogger<OpenTelWatcherApiClient>());

        // Act
        var status = await client.GetInstanceStatusAsync(new Version(1, 0, 0));

        // Assert
        status.IsRunning.Should().BeTrue();
        status.IsCompatible.Should().BeFalse();
        status.IncompatibilityReason.Should().Contain("OpenTelWatcher");
    }

}

// Mock HTTP message handler for testing
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(_handler(request));
        }
        catch (Exception ex)
        {
            throw new HttpRequestException(ex.Message, ex);
        }
    }
}

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
    public async Task GetInfoAsync_WhenServiceRunning_ReturnsInfo()
    {
        // Arrange
        var expectedInfo = new InfoResponse
        {
            Application = "OpenTelWatcher",
            Version = "1.0.0",
            VersionComponents = new VersionComponents { Major = 1, Minor = 0, Patch = 0 },
            ProcessId = 12345,
            Port = 4318,
            Health = new DiagnoseHealth
            {
                Status = "healthy",
                ConsecutiveErrors = 0,
                RecentErrors = new List<string>()
            },
            Files = new FileStatistics
            {
                Count = 5,
                TotalSizeBytes = 1024
            },
            Configuration = new DiagnoseConfiguration
            {
                OutputDirectory = "./telemetry-data"
            }
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri?.PathAndQuery == "/api/info")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(expectedInfo)
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:4318") };
        var client = new OpenTelWatcherApiClient(httpClient, TestLoggerFactory.CreateLogger<OpenTelWatcherApiClient>());

        // Act
        var result = await client.GetInfoAsync();

        // Assert
        result.Should().NotBeNull();
        result!.Application.Should().Be("OpenTelWatcher");
        result.Version.Should().Be("1.0.0");
        result.Health.Status.Should().Be("healthy");
        result.Files.Count.Should().Be(5);
    }

    [Fact]
    public async Task GetInfoAsync_WhenServiceNotRunning_ReturnsNull()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            throw new HttpRequestException("Connection refused");
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:4318") };
        var client = new OpenTelWatcherApiClient(httpClient, TestLoggerFactory.CreateLogger<OpenTelWatcherApiClient>());

        // Act
        var result = await client.GetInfoAsync();

        // Assert
        result.Should().BeNull();
    }

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
        var infoResponse = new InfoResponse
        {
            Application = "OpenTelWatcher",
            Version = "1.5.0",
            VersionComponents = new VersionComponents { Major = 1, Minor = 5, Patch = 0 },
            ProcessId = 12345,
            Port = 4318,
            Health = new DiagnoseHealth { Status = "healthy", ConsecutiveErrors = 0, RecentErrors = new List<string>() },
            Files = new FileStatistics { Count = 0, TotalSizeBytes = 0 },
            Configuration = new DiagnoseConfiguration { OutputDirectory = "./telemetry-data" }
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(infoResponse)
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
        var infoResponse = new InfoResponse
        {
            Application = "OpenTelWatcher",
            Version = "2.0.0",
            VersionComponents = new VersionComponents { Major = 2, Minor = 0, Patch = 0 },
            ProcessId = 12345,
            Port = 4318,
            Health = new DiagnoseHealth { Status = "healthy", ConsecutiveErrors = 0, RecentErrors = new List<string>() },
            Files = new FileStatistics { Count = 0, TotalSizeBytes = 0 },
            Configuration = new DiagnoseConfiguration { OutputDirectory = "./telemetry-data" }
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(infoResponse)
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
        var infoResponse = new InfoResponse
        {
            Application = "SomeOtherApp",
            Version = "1.0.0",
            VersionComponents = new VersionComponents { Major = 1, Minor = 0, Patch = 0 },
            ProcessId = 12345,
            Port = 4318,
            Health = new DiagnoseHealth { Status = "healthy", ConsecutiveErrors = 0, RecentErrors = new List<string>() },
            Files = new FileStatistics { Count = 0, TotalSizeBytes = 0 },
            Configuration = new DiagnoseConfiguration { OutputDirectory = "./telemetry-data" }
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(infoResponse)
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

    [Fact]
    public async Task ShutdownAsync_WhenServiceRunning_ReturnsTrue()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri?.PathAndQuery == "/api/shutdown")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { message = "Shutdown initiated", timestamp = DateTime.UtcNow })
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:4318") };
        var client = new OpenTelWatcherApiClient(httpClient, TestLoggerFactory.CreateLogger<OpenTelWatcherApiClient>());

        // Act
        var result = await client.ShutdownAsync();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ShutdownAsync_WhenServiceNotRunning_ReturnsFalse()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(req =>
        {
            throw new HttpRequestException("Connection refused");
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:4318") };
        var client = new OpenTelWatcherApiClient(httpClient, TestLoggerFactory.CreateLogger<OpenTelWatcherApiClient>());

        // Act
        var result = await client.ShutdownAsync();

        // Assert
        result.Should().BeFalse();
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

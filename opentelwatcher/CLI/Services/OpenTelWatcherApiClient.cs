using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.CLI.Models;

namespace OpenTelWatcher.CLI.Services;

/// <summary>
/// HTTP client implementation for watcher API
/// </summary>
public sealed class OpenTelWatcherApiClient : IOpenTelWatcherApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenTelWatcherApiClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OpenTelWatcherApiClient(HttpClient httpClient, ILogger<OpenTelWatcherApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<InfoResponse?> GetInfoAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/info");
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<InfoResponse>(JsonOptions);
        }
        catch (HttpRequestException ex)
        {
            // Connection refused - service not running
            _logger.LogDebug(ex, "HTTP request failed (service likely not running)");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            // Timeout - service not running or not responding
            _logger.LogDebug(ex, "Request timed out (service not responding)");
            return null;
        }
    }

    public async Task<InstanceStatus> GetInstanceStatusAsync(Version cliVersion)
    {
        var info = await GetInfoAsync();

        if (info is null)
        {
            return new InstanceStatus
            {
                IsRunning = false,
                Version = null,
                Pid = null,
                IsCompatible = false,
                IncompatibilityReason = null,
                DetectionError = null
            };
        }

        // Create VersionResponse for compatibility with InstanceStatus
        var versionResponse = new VersionResponse
        {
            Application = info.Application,
            Version = info.Version,
            VersionComponents = info.VersionComponents,
            ProcessId = info.ProcessId
        };

        // Verify application identifier
        if (info.Application != "OpenTelWatcher")
        {
            return new InstanceStatus
            {
                IsRunning = true,
                Version = versionResponse,
                Pid = info.ProcessId,
                IsCompatible = false,
                IncompatibilityReason = $"Expected 'OpenTelWatcher' but found '{info.Application}'",
                DetectionError = null
            };
        }

        // Check major version compatibility
        var instanceMajor = info.VersionComponents.Major;
        var cliMajor = cliVersion.Major;

        if (instanceMajor != cliMajor)
        {
            return new InstanceStatus
            {
                IsRunning = true,
                Version = versionResponse,
                Pid = info.ProcessId,
                IsCompatible = false,
                IncompatibilityReason = $"Version mismatch: CLI major version {cliMajor} does not match instance major version {instanceMajor}",
                DetectionError = null
            };
        }

        // Compatible
        return new InstanceStatus
        {
            IsRunning = true,
            Version = versionResponse,
            Pid = info.ProcessId,
            IsCompatible = true,
            IncompatibilityReason = null,
            DetectionError = null
        };
    }

    public async Task<bool> ShutdownAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("/api/shutdown", null);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "HTTP request failed when calling /api/shutdown");
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogDebug(ex, "Request timed out when calling /api/shutdown");
            return false;
        }
    }

    public async Task<bool> WaitForShutdownAsync(int timeoutSeconds = ApiConstants.Timeouts.ShutdownWaitSeconds)
    {
        var endTime = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < endTime)
        {
            var info = await GetInfoAsync();
            if (info is null)
            {
                // Service stopped
                return true;
            }

            await Task.Delay(ApiConstants.Timeouts.HealthCheckPollIntervalMs);
        }

        return false; // Timeout
    }

    public async Task<ClearResponse?> ClearAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("/api/clear", null);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<ClearResponse>(JsonOptions);
        }
        catch (HttpRequestException ex)
        {
            // Connection refused - service not running
            _logger.LogDebug(ex, "HTTP request failed (service likely not running)");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            // Timeout - service not running or not responding
            _logger.LogDebug(ex, "Request timed out (service not responding)");
            return null;
        }
    }

    public async Task<StatsResponse?> GetStatsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/stats");
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<StatsResponse>(JsonOptions);
        }
        catch (HttpRequestException ex)
        {
            // Connection refused - service not running
            _logger.LogDebug(ex, "HTTP request failed (service likely not running)");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            // Timeout - service not running or not responding
            _logger.LogDebug(ex, "Request timed out (service not responding)");
            return null;
        }
    }
}

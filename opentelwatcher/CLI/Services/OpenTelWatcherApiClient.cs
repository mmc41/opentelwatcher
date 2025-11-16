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

    public async Task<StatusResponse?> GetStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/status");
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<StatusResponse>(JsonOptions);
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
        var status = await GetStatusAsync();
        if (status is null)
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
            Application = status.Application,
            Version = status.Version,
            VersionComponents = status.VersionComponents,
            ProcessId = status.ProcessId
        };

        // Verify application identifier
        if (status.Application != "OpenTelWatcher")
        {
            return new InstanceStatus
            {
                IsRunning = true,
                Version = versionResponse,
                Pid = status.ProcessId,
                IsCompatible = false,
                IncompatibilityReason = $"Expected 'OpenTelWatcher' but found '{status.Application}'",
                DetectionError = null
            };
        }

        // Check major version compatibility
        var instanceMajor = status.VersionComponents.Major;
        var cliMajor = cliVersion.Major;

        if (instanceMajor != cliMajor)
        {
            return new InstanceStatus
            {
                IsRunning = true,
                Version = versionResponse,
                Pid = status.ProcessId,
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
            Pid = status.ProcessId,
            IsCompatible = true,
            IncompatibilityReason = null,
            DetectionError = null
        };
    }

    [Obsolete("Use GetStatusAsync instead")]
    public async Task<InfoResponse?> GetInfoAsync()
    {
        var status = await GetStatusAsync();
        if (status is null)
            return null;

        // Map StatusResponse back to InfoResponse for backward compatibility
        return new InfoResponse
        {
            Application = status.Application,
            Version = status.Version,
            VersionComponents = status.VersionComponents,
            ProcessId = status.ProcessId,
            Port = status.Port,
            Health = status.Health,
            Files = new FileStatistics
            {
                Count = status.Files.Count,
                TotalSizeBytes = status.Files.TotalSizeBytes
            },
            Configuration = status.Configuration
        };
    }

    public async Task<bool> StopAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("/api/stop", null);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "HTTP request failed when calling /api/stop");
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogDebug(ex, "Request timed out when calling /api/stop");
            return false;
        }
    }

    [Obsolete("Use StopAsync instead")]
    public async Task<bool> ShutdownAsync()
    {
        return await StopAsync();
    }

    public async Task<bool> WaitForStopAsync(int timeoutSeconds = ApiConstants.Timeouts.ShutdownWaitSeconds)
    {
        var endTime = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < endTime)
        {
            var status = await GetStatusAsync();
            if (status is null)
            {
                // Service stopped
                return true;
            }

            await Task.Delay(ApiConstants.Timeouts.HealthCheckPollIntervalMs);
        }

        return false; // Timeout
    }

    [Obsolete("Use WaitForStopAsync instead")]
    public async Task<bool> WaitForShutdownAsync(int timeoutSeconds = ApiConstants.Timeouts.ShutdownWaitSeconds)
    {
        return await WaitForStopAsync(timeoutSeconds);
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

    [Obsolete("Use GetStatusAsync instead. Statistics are now included in the status response.")]
    public async Task<StatsResponse?> GetStatsAsync()
    {
        var status = await GetStatusAsync();
        if (status is null)
            return null;

        // Map StatusResponse to StatsResponse for backward compatibility
        return new StatsResponse
        {
            Telemetry = status.Telemetry,
            Files = status.Files.Breakdown,
            UptimeSeconds = status.UptimeSeconds
        };
    }
}

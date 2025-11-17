using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.CLI.Services;

/// <summary>
/// HTTP client implementation for watcher API
/// </summary>
public sealed class OpenTelWatcherApiClient : IOpenTelWatcherApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenTelWatcherApiClient> _logger;
    private readonly ITimeProvider _timeProvider;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OpenTelWatcherApiClient(HttpClient httpClient, ILogger<OpenTelWatcherApiClient> logger, ITimeProvider timeProvider)
    {
        _httpClient = httpClient;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
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

    public async Task<bool> WaitForStopAsync(int timeoutSeconds = ApiConstants.Timeouts.ShutdownWaitSeconds)
    {
        var endTime = _timeProvider.UtcNow.AddSeconds(timeoutSeconds);

        while (_timeProvider.UtcNow < endTime)
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
}

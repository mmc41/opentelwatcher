using OpenTelWatcher.CLI.Models;
using OpenTelWatcher.CLI.Services;

namespace UnitTests.Mocks;

/// <summary>
/// Mock implementation of IOpenTelWatcherApiClient for unit testing.
/// Returns configurable responses for all API calls.
/// </summary>
public class MockOpenTelWatcherApiClient : IOpenTelWatcherApiClient
{
    /// <summary>
    /// The instance status to return from GetInstanceStatusAsync().
    /// </summary>
    public InstanceStatus InstanceStatus { get; set; } = new() { IsRunning = false };

    /// <summary>
    /// The status response to return from GetStatusAsync().
    /// </summary>
    public StatusResponse? StatusResponse { get; set; }

    /// <summary>
    /// The success status to return from StopAsync().
    /// </summary>
    public bool StopSuccess { get; set; } = true;

    /// <summary>
    /// The wait for stop result to return from WaitForStopAsync().
    /// </summary>
    public bool WaitForStopResult { get; set; } = true;

    /// <summary>
    /// The clear response to return from ClearAsync().
    /// </summary>
    public ClearResponse? ClearResponse { get; set; }

    /// <summary>
    /// Records all calls to GetInstanceStatusAsync with the CLI version parameter.
    /// </summary>
    public List<Version> GetInstanceStatusCalls { get; } = new();

    public Task<StatusResponse?> GetStatusAsync()
    {
        return Task.FromResult(StatusResponse);
    }

    public Task<InstanceStatus> GetInstanceStatusAsync(Version cliVersion)
    {
        GetInstanceStatusCalls.Add(cliVersion);
        return Task.FromResult(InstanceStatus);
    }

    public Task<bool> StopAsync()
    {
        return Task.FromResult(StopSuccess);
    }

    public Task<bool> WaitForStopAsync(int timeoutSeconds = 30)
    {
        return Task.FromResult(WaitForStopResult);
    }

    public Task<ClearResponse?> ClearAsync()
    {
        return Task.FromResult(ClearResponse);
    }
}

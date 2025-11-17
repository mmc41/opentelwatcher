using Microsoft.Extensions.Logging;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// Polling helpers to replace hardcoded Task.Delay calls with condition-based waiting.
/// Eliminates flaky tests caused by timing assumptions.
/// </summary>
public static class PollingHelpers
{
    private const int DefaultTimeoutMs = 5000;
    private const int DefaultPollingIntervalMs = 100;

    /// <summary>
    /// Waits until a condition becomes true, polling at regular intervals.
    /// </summary>
    /// <param name="condition">The condition to check</param>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds (default: 5000ms)</param>
    /// <param name="pollingIntervalMs">Time between checks in milliseconds (default: 100ms)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="logger">Optional logger for diagnostic output</param>
    /// <param name="conditionDescription">Optional description of what we're waiting for (for logging)</param>
    /// <returns>True if condition met, false if timeout</returns>
    public static async Task<bool> WaitForConditionAsync(
        Func<bool> condition,
        int timeoutMs = DefaultTimeoutMs,
        int pollingIntervalMs = DefaultPollingIntervalMs,
        CancellationToken cancellationToken = default,
        ILogger? logger = null,
        string? conditionDescription = null)
    {
        var description = conditionDescription ?? "condition";
        logger?.LogDebug("Waiting for {Description} (timeout: {TimeoutMs}ms, interval: {IntervalMs}ms)",
            description, timeoutMs, pollingIntervalMs);

        var startTime = DateTime.UtcNow;
        var endTime = startTime.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < endTime)
        {
            if (condition())
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                logger?.LogDebug("{Description} met after {ElapsedMs:F0}ms", description, elapsed);
                return true;
            }

            await Task.Delay(pollingIntervalMs, cancellationToken);
        }

        var totalElapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
        logger?.LogWarning("{Description} not met after {ElapsedMs:F0}ms timeout", description, totalElapsed);
        return false;
    }

    /// <summary>
    /// Waits until an async condition becomes true, polling at regular intervals.
    /// </summary>
    public static async Task<bool> WaitForConditionAsync(
        Func<Task<bool>> conditionAsync,
        int timeoutMs = DefaultTimeoutMs,
        int pollingIntervalMs = DefaultPollingIntervalMs,
        CancellationToken cancellationToken = default,
        ILogger? logger = null,
        string? conditionDescription = null)
    {
        var description = conditionDescription ?? "condition";
        logger?.LogDebug("Waiting for {Description} (timeout: {TimeoutMs}ms, interval: {IntervalMs}ms)",
            description, timeoutMs, pollingIntervalMs);

        var startTime = DateTime.UtcNow;
        var endTime = startTime.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < endTime)
        {
            if (await conditionAsync())
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                logger?.LogDebug("{Description} met after {ElapsedMs:F0}ms", description, elapsed);
                return true;
            }

            await Task.Delay(pollingIntervalMs, cancellationToken);
        }

        var totalElapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
        logger?.LogWarning("{Description} not met after {ElapsedMs:F0}ms timeout", description, totalElapsed);
        return false;
    }

    /// <summary>
    /// Waits until a file exists in the specified directory matching the pattern.
    /// </summary>
    /// <param name="directory">Directory to search</param>
    /// <param name="filePattern">File pattern (e.g., "traces.*.ndjson")</param>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds (default: 5000ms)</param>
    /// <param name="pollingIntervalMs">Time between checks in milliseconds (default: 100ms)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="logger">Optional logger for diagnostic output</param>
    /// <returns>True if file found, false if timeout</returns>
    public static async Task<bool> WaitForFileAsync(
        string directory,
        string filePattern,
        int timeoutMs = DefaultTimeoutMs,
        int pollingIntervalMs = DefaultPollingIntervalMs,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        return await WaitForConditionAsync(
            condition: () => Directory.Exists(directory) && Directory.GetFiles(directory, filePattern).Length > 0,
            timeoutMs: timeoutMs,
            pollingIntervalMs: pollingIntervalMs,
            cancellationToken: cancellationToken,
            logger: logger,
            conditionDescription: $"file matching '{filePattern}' in '{directory}'");
    }

    /// <summary>
    /// Waits until a process has exited.
    /// </summary>
    /// <param name="process">The process to monitor</param>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds (default: 5000ms)</param>
    /// <param name="pollingIntervalMs">Time between checks in milliseconds (default: 100ms)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="logger">Optional logger for diagnostic output</param>
    /// <returns>True if process exited, false if timeout</returns>
    public static async Task<bool> WaitForProcessExitAsync(
        System.Diagnostics.Process process,
        int timeoutMs = DefaultTimeoutMs,
        int pollingIntervalMs = DefaultPollingIntervalMs,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        return await WaitForConditionAsync(
            condition: () => process.HasExited,
            timeoutMs: timeoutMs,
            pollingIntervalMs: pollingIntervalMs,
            cancellationToken: cancellationToken,
            logger: logger,
            conditionDescription: $"process {process.Id} to exit");
    }
}

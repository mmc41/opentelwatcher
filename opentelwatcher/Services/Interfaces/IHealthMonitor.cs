namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Health status of the service.
/// </summary>
public enum HealthStatus
{
    /// <summary>Service is operating normally.</summary>
    Healthy,

    /// <summary>Service is degraded due to errors.</summary>
    Degraded
}

/// <summary>
/// Service for monitoring application health and error tracking.
/// </summary>
public interface IHealthMonitor
{
    /// <summary>
    /// Gets the current health status.
    /// </summary>
    HealthStatus Status { get; }

    /// <summary>
    /// Records a successful file write operation.
    /// </summary>
    void RecordSuccess();

    /// <summary>
    /// Records a failed file write operation.
    /// </summary>
    /// <param name="error">Error message or exception details.</param>
    void RecordError(string error);

    /// <summary>
    /// Gets the count of consecutive errors.
    /// </summary>
    int ConsecutiveErrorCount { get; }

    /// <summary>
    /// Gets recent error history (limited by MaxErrorHistorySize).
    /// </summary>
    IReadOnlyList<string> GetRecentErrors();

    /// <summary>
    /// Resets health status to healthy and clears error counters.
    /// </summary>
    void Reset();
}

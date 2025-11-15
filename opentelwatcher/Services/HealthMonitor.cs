using OpenTelWatcher.Configuration;
using OpenTelWatcher.Services.Interfaces;
using System.Collections.Concurrent;

namespace OpenTelWatcher.Services;

/// <summary>
/// Service for monitoring application health and tracking file write errors.
/// </summary>
public class HealthMonitor : IHealthMonitor
{
    private readonly OpenTelWatcherOptions _options;
    private readonly ConcurrentQueue<string> _errorHistory = new();
    private int _consecutiveErrorCount;
    private readonly object _lock = new();

    public HealthMonitor(OpenTelWatcherOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public HealthStatus Status
    {
        get
        {
            lock (_lock)
            {
                return _consecutiveErrorCount >= _options.MaxConsecutiveFileErrors
                    ? HealthStatus.Degraded
                    : HealthStatus.Healthy;
            }
        }
    }

    /// <inheritdoc/>
    public int ConsecutiveErrorCount
    {
        get
        {
            lock (_lock)
            {
                return _consecutiveErrorCount;
            }
        }
    }

    /// <inheritdoc/>
    public void RecordSuccess()
    {
        lock (_lock)
        {
            _consecutiveErrorCount = 0;
        }
    }

    /// <inheritdoc/>
    public void RecordError(string error)
    {
        lock (_lock)
        {
            _consecutiveErrorCount++;

            // Trim history to max size BEFORE adding new error to maintain constant size
            // This prevents performance degradation under high error rates
            while (_errorHistory.Count >= _options.MaxErrorHistorySize)
            {
                _errorHistory.TryDequeue(out _);
            }

            // Add to error history
            _errorHistory.Enqueue(error);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetRecentErrors()
    {
        lock (_lock)
        {
            return _errorHistory.ToList();
        }
    }

    /// <inheritdoc/>
    public void Reset()
    {
        lock (_lock)
        {
            _consecutiveErrorCount = 0;
            _errorHistory.Clear();
        }
    }
}

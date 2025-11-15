using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services;

public class TelemetryStatisticsService : ITelemetryStatistics
{
    private long _tracesReceived;
    private long _logsReceived;
    private long _metricsReceived;

    public long TracesReceived => Interlocked.Read(ref _tracesReceived);
    public long LogsReceived => Interlocked.Read(ref _logsReceived);
    public long MetricsReceived => Interlocked.Read(ref _metricsReceived);

    public void IncrementTraces() => Interlocked.Increment(ref _tracesReceived);
    public void IncrementLogs() => Interlocked.Increment(ref _logsReceived);
    public void IncrementMetrics() => Interlocked.Increment(ref _metricsReceived);
}

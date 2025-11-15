namespace OpenTelWatcher.Services.Interfaces;

public interface ITelemetryStatistics
{
    long TracesReceived { get; }
    long LogsReceived { get; }
    long MetricsReceived { get; }
    void IncrementTraces();
    void IncrementLogs();
    void IncrementMetrics();
}

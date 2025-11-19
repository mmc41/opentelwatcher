using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Interfaces;

namespace UnitTests.Helpers;

/// <summary>
/// Mock telemetry receiver for testing.
/// </summary>
public class MockTelemetryReceiver : ITelemetryReceiver
{
    public List<TelemetryItem> ReceivedItems { get; } = new();
    public int WriteCallCount { get; private set; }

    public Task WriteAsync(TelemetryItem item, CancellationToken cancellationToken)
    {
        WriteCallCount++;
        ReceivedItems.Add(item);
        return Task.CompletedTask;
    }
}

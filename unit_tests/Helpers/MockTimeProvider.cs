using OpenTelWatcher.Services.Interfaces;

namespace UnitTests.Helpers;

/// <summary>
/// Mock implementation of ITimeProvider for testing.
/// Allows controlling time for deterministic tests.
/// </summary>
public class MockTimeProvider : ITimeProvider
{
    /// <summary>
    /// The current UTC time to return. Can be set for testing.
    /// </summary>
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Total milliseconds slept. Use for verifying Sleep() was called.
    /// </summary>
    public int TotalSleptMilliseconds { get; private set; }

    /// <summary>
    /// Number of times Sleep() was called.
    /// </summary>
    public int SleepCallCount { get; private set; }

    public void Sleep(int milliseconds)
    {
        SleepCallCount++;
        TotalSleptMilliseconds += milliseconds;
        // In tests, we don't actually sleep - just record the call
    }

    /// <summary>
    /// Reset counters for a fresh test.
    /// </summary>
    public void Reset()
    {
        SleepCallCount = 0;
        TotalSleptMilliseconds = 0;
        UtcNow = DateTime.UtcNow;
    }

    /// <summary>
    /// Advance time by the specified duration.
    /// </summary>
    public void AdvanceTime(TimeSpan duration)
    {
        UtcNow = UtcNow.Add(duration);
    }
}

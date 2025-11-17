using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services;

/// <summary>
/// Production implementation of ITimeProvider that uses system time.
/// </summary>
public sealed class SystemTimeProvider : ITimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;

    public void Sleep(int milliseconds) => Thread.Sleep(milliseconds);
}

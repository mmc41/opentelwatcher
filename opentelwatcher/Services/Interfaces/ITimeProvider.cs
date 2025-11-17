namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Provides access to time-related operations (current time, delays).
/// Used for timestamps, age calculations, and retry delays.
/// </summary>
/// <remarks>
/// Scope: Provides time operations for the current application's execution context.
///
/// Design Purpose:
/// - Abstraction for DateTime.UtcNow and Thread.Sleep to enable unit testing
/// - Allows mocking time for deterministic tests (fixed timestamps, instant delays)
/// - Used by PidFileService for timestamps, retry delays, and entry age calculations
/// </remarks>
public interface ITimeProvider
{
    /// <summary>
    /// Gets the current UTC date and time.
    /// </summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// Suspends the current thread for the specified number of milliseconds.
    /// </summary>
    /// <param name="milliseconds">The number of milliseconds to sleep.</param>
    void Sleep(int milliseconds);
}

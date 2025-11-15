namespace OpenTelWatcher.Configuration;

/// <summary>
/// Constants for OpenTelemetry signal types.
/// </summary>
public static class SignalTypes
{
    /// <summary>
    /// Traces signal type.
    /// </summary>
    public const string Traces = "traces";

    /// <summary>
    /// Logs signal type.
    /// </summary>
    public const string Logs = "logs";

    /// <summary>
    /// Metrics signal type.
    /// </summary>
    public const string Metrics = "metrics";

    /// <summary>
    /// Array of all valid signal types.
    /// </summary>
    public static readonly string[] All = { Traces, Logs, Metrics };

    /// <summary>
    /// Checks if a signal type is valid.
    /// </summary>
    /// <param name="signalType">The signal type to validate.</param>
    /// <returns>True if the signal type is valid, false otherwise.</returns>
    public static bool IsValid(string signalType)
    {
        return All.Contains(signalType, StringComparer.OrdinalIgnoreCase);
    }
}

using System.Text.Json.Serialization;

namespace OpenTelWatcher.Configuration;

/// <summary>
/// Represents the type of OpenTelemetry signal.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SignalType
{
    /// <summary>
    /// Unspecified signal type. Used for filtering all signals or as a default/invalid value.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Trace signals (spans).
    /// </summary>
    Traces = 1,

    /// <summary>
    /// Log signals (log records).
    /// </summary>
    Logs = 2,

    /// <summary>
    /// Metric signals (data points).
    /// </summary>
    Metrics = 3
}

/// <summary>
/// Extension methods for SignalType enum.
/// </summary>
public static class SignalTypeExtensions
{
    /// <summary>
    /// Converts the signal type to lowercase string representation for file naming and HTTP parameters.
    /// </summary>
    /// <param name="signalType">The signal type to convert.</param>
    /// <returns>Lowercase string representation (e.g., "traces", "logs", "metrics").</returns>
    /// <exception cref="ArgumentException">Thrown if signal type is Unspecified.</exception>
    public static string ToLowerString(this SignalType signalType)
    {
        return signalType switch
        {
            SignalType.Traces => "traces",
            SignalType.Logs => "logs",
            SignalType.Metrics => "metrics",
            SignalType.Unspecified => throw new ArgumentException("Cannot convert Unspecified signal type to string", nameof(signalType)),
            _ => throw new ArgumentOutOfRangeException(nameof(signalType), signalType, "Unknown signal type")
        };
    }

    /// <summary>
    /// Parses a string to a SignalType enum value (case-insensitive).
    /// </summary>
    /// <param name="value">The string value to parse.</param>
    /// <returns>The corresponding SignalType, or Unspecified if the value is null, empty, or invalid.</returns>
    public static SignalType FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SignalType.Unspecified;
        }

        return value.ToLowerInvariant() switch
        {
            "traces" => SignalType.Traces,
            "logs" => SignalType.Logs,
            "metrics" => SignalType.Metrics,
            _ => SignalType.Unspecified
        };
    }

    /// <summary>
    /// Checks if the signal type is valid (not Unspecified).
    /// </summary>
    /// <param name="signalType">The signal type to check.</param>
    /// <returns>True if the signal type is Traces, Logs, or Metrics; false if Unspecified.</returns>
    public static bool IsValid(this SignalType signalType)
    {
        return signalType != SignalType.Unspecified;
    }

    /// <summary>
    /// Checks if the signal type is specified (not Unspecified). Alias for IsValid() for better readability.
    /// </summary>
    /// <param name="signalType">The signal type to check.</param>
    /// <returns>True if the signal type is Traces, Logs, or Metrics; false if Unspecified.</returns>
    public static bool IsSpecified(this SignalType signalType)
    {
        return signalType != SignalType.Unspecified;
    }
}

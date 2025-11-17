namespace OpenTelWatcher.Configuration;

/// <summary>
/// Default port numbers used throughout the application.
/// </summary>
public static class DefaultPorts
{
    /// <summary>
    /// Default port for OTLP (OpenTelemetry Protocol) receiver.
    /// Standard OTLP/HTTP port as defined by OpenTelemetry specification.
    /// </summary>
    public const int Otlp = 4318;
}

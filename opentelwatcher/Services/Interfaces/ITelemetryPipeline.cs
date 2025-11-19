using Google.Protobuf;
using OpenTelWatcher.Configuration;

namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Central pipeline for processing telemetry data through multiple receivers.
/// </summary>
public interface ITelemetryPipeline
{
    /// <summary>
    /// Writes a telemetry message through the pipeline.
    /// </summary>
    Task WriteAsync<T>(T message, SignalType signal, CancellationToken cancellationToken) where T : IMessage;

    /// <summary>
    /// Registers a receiver with one or more filters to process telemetry items.
    /// All filters must return true for the item to be written.
    /// </summary>
    void RegisterReceiver(ITelemetryReceiver receiver, params ITelemetryFilter[] filters);
}

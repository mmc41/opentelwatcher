using Google.Protobuf;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Models;
using OpenTelWatcher.Serialization;
using OpenTelWatcher.Services.Filters;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services;

/// <summary>
/// Central pipeline for processing telemetry through multiple receivers with filters.
/// </summary>
public sealed class TelemetryPipeline : ITelemetryPipeline
{
    private readonly IProtobufJsonSerializer _serializer;
    private readonly IErrorDetectionService _errorDetection;
    private readonly ITimeProvider _timeProvider;
    private readonly List<(ITelemetryReceiver Receiver, ITelemetryFilter[] Filters)> _receivers;
    private readonly object _lock = new();
    private readonly ILogger<TelemetryPipeline> _logger;

    public TelemetryPipeline(
        IProtobufJsonSerializer serializer,
        IErrorDetectionService errorDetection,
        ITimeProvider timeProvider,
        ILogger<TelemetryPipeline> logger)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _errorDetection = errorDetection ?? throw new ArgumentNullException(nameof(errorDetection));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _receivers = new List<(ITelemetryReceiver, ITelemetryFilter[])>();
    }

    public void RegisterReceiver(ITelemetryReceiver receiver, params ITelemetryFilter[] filters)
    {
        ArgumentNullException.ThrowIfNull(receiver);

        // Default to AllSignalsFilter if no filters provided
        var filterArray = filters.Length > 0 ? filters : new ITelemetryFilter[] { new AllSignalsFilter() };

        lock (_lock)
        {
            _receivers.Add((receiver, filterArray));
        }
    }

    public async Task WriteAsync<T>(T message, SignalType signal, CancellationToken cancellationToken) where T : IMessage
    {
        ArgumentNullException.ThrowIfNull(message);

        if (signal == SignalType.Unspecified)
        {
            throw new ArgumentException("Signal type must be specified", nameof(signal));
        }

        // Validate message type matches signal type
        ValidateMessageType(message, signal);

        // Serialize
        var json = _serializer.Serialize(message);
        var ndjsonLine = json + "\n";

        // Detect errors based on signal type
        var isError = DetectErrors(message, signal);

        // Create item
        var item = new TelemetryItem(signal, ndjsonLine, isError, _timeProvider.UtcNow);

        // Get receivers snapshot
        List<(ITelemetryReceiver Receiver, ITelemetryFilter[] Filters)> receivers;
        lock (_lock)
        {
            receivers = new List<(ITelemetryReceiver, ITelemetryFilter[])>(_receivers);
        }

        // Process receivers
        foreach (var (receiver, filters) in receivers)
        {
            try
            {
                // ALL filters must return true for item to be written
                var shouldWrite = filters.All(filter => filter.ShouldWrite(item));

                if (shouldWrite)
                {
                    await receiver.WriteAsync(item, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Receiver {ReceiverType} failed to process {Signal} telemetry",
                    receiver.GetType().Name, signal);
                // Continue processing other receivers
            }
        }
    }

    private bool DetectErrors<T>(T message, SignalType signal) where T : IMessage
    {
        return signal switch
        {
            SignalType.Traces when message is ExportTraceServiceRequest traceRequest
                => _errorDetection.ContainsErrors(traceRequest),
            SignalType.Logs when message is ExportLogsServiceRequest logsRequest
                => _errorDetection.ContainsErrors(logsRequest),
            _ => false // Metrics and other signals don't have error detection
        };
    }

    /// <summary>
    /// Validates that the message type matches the expected type for the given signal type.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when message type doesn't match signal type</exception>
    private static void ValidateMessageType<T>(T message, SignalType signal) where T : IMessage
    {
        var isValid = signal switch
        {
            SignalType.Traces => message is ExportTraceServiceRequest,
            SignalType.Logs => message is ExportLogsServiceRequest,
            SignalType.Metrics => message is ExportMetricsServiceRequest,
            _ => false
        };

        if (!isValid)
        {
            var expectedType = signal switch
            {
                SignalType.Traces => nameof(ExportTraceServiceRequest),
                SignalType.Logs => nameof(ExportLogsServiceRequest),
                SignalType.Metrics => nameof(ExportMetricsServiceRequest),
                _ => "unknown"
            };

            throw new ArgumentException(
                $"Signal type {signal} requires {expectedType}, but received {message.GetType().Name}",
                nameof(message));
        }
    }
}

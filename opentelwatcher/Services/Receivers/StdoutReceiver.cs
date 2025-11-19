using OpenTelWatcher.Configuration;
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services.Receivers;

/// <summary>
/// Writes telemetry items to stdout with colorized, timestamped formatting.
/// </summary>
public sealed class StdoutReceiver : ITelemetryReceiver, IDisposable
{
    private readonly SemaphoreSlim _consoleLock = new(1, 1);
    private readonly ILogger<StdoutReceiver> _logger;

    public StdoutReceiver(ILogger<StdoutReceiver> logger)
    {
        _logger = logger;
    }

    public async Task WriteAsync(TelemetryItem item, CancellationToken cancellationToken)
    {
        await _consoleLock.WaitAsync(cancellationToken);
        try
        {
            var color = GetColor(item);
            var timestamp = item.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
            var ndjson = item.NdjsonLine.TrimEnd('\n');
            var signalName = item.Signal.ToLowerString(); // Convert enum to lowercase string
            var output = $"{color}[{timestamp}] [{signalName}] {ndjson}\x1b[0m";

            Console.WriteLine(output);
        }
        finally
        {
            _consoleLock.Release();
        }
    }

    private static string GetColor(TelemetryItem item)
    {
        // Errors always red, regardless of signal type
        if (item.IsError)
        {
            return "\x1b[31m"; // Red
        }

        // Color by signal type (using enum)
        return item.Signal switch
        {
            SignalType.Traces => "\x1b[36m",  // Cyan
            SignalType.Logs => "\x1b[37m",    // White
            SignalType.Metrics => "\x1b[32m", // Green
            _ => "\x1b[37m"                   // Default: White (for Unspecified)
        };
    }

    public void Dispose()
    {
        _consoleLock.Dispose();
    }
}

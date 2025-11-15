using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace UnitTests.Helpers;

/// <summary>
/// Provides real ILogger instances configured with NLog for unit testing.
/// This allows tests to use actual logging infrastructure instead of mocks.
/// </summary>
public static class TestLoggerFactory
{
    private static readonly ILoggerFactory _loggerFactory = CreateLoggerFactory();

    /// <summary>
    /// Gets the shared ILoggerFactory instance for tests.
    /// </summary>
    public static ILoggerFactory Instance => _loggerFactory;

    private static ILoggerFactory CreateLoggerFactory()
    {
        return Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddNLog();
        });
    }

    /// <summary>
    /// Creates a real ILogger instance for the specified type.
    /// </summary>
    public static ILogger<T> CreateLogger<T>()
    {
        return _loggerFactory.CreateLogger<T>();
    }
}

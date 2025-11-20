using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// Provides real ILogger instances configured with NLog for E2E testing.
/// This allows tests to use actual logging infrastructure instead of mocks.
/// Note: This is a duplicate of UnitTests.Helpers.TestLoggerFactory but with different namespace.
/// The duplication is intentional to keep test projects independent.
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

    /// <summary>
    /// Creates a real ILogger instance for the specified type.
    /// </summary>
    public static ILogger CreateLogger(Type type)
    {
        return _loggerFactory.CreateLogger(type);
    }
}

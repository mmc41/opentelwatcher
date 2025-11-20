using Microsoft.Extensions.Logging;

namespace UnitTests.Helpers;

/// <summary>
/// Base class for tests that need temporary file system directories.
/// Automatically creates and cleans up a unique temp directory per test class.
/// </summary>
public abstract class FileBasedTestBase : IDisposable
{
    protected readonly string TestOutputDir;
    private bool _disposed;
    private readonly ILogger _logger;

    protected FileBasedTestBase()
    {
        TestOutputDir = Path.Combine(Path.GetTempPath(), $"{GetType().Name}-{Guid.NewGuid()}");
        Directory.CreateDirectory(TestOutputDir);
        _logger = TestLoggerFactory.CreateLogger(GetType());
        _logger.LogDebug("Created test directory: {TestOutputDir}", TestOutputDir);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing && Directory.Exists(TestOutputDir))
        {
            try
            {
                Directory.Delete(TestOutputDir, recursive: true);
            }
            catch (Exception ex)
            {
                // Log cleanup errors but don't throw to prevent test failures during disposal
                _logger.LogWarning(ex, "Failed to cleanup test directory {TestOutputDir}", TestOutputDir);
            }
        }

        _disposed = true;
    }
}

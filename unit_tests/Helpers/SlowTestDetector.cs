using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace UnitTests.Helpers;

/// <summary>
/// Automatically detects and reports slow-running tests.
/// Helps identify performance regressions and bottlenecks in the test suite.
/// </summary>
/// <example>
/// <code>
/// [Fact]
/// public async Task MyTest()
/// {
///     using var _ = new SlowTestDetector(); // Automatically gets test name
///     // Test code...
/// }
/// </code>
/// </example>
public class SlowTestDetector : IDisposable
{
    /// <summary>
    /// Tests taking longer than this threshold will generate a warning.
    /// </summary>
    private static readonly TimeSpan WarningThreshold = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Tests taking longer than this threshold will generate an error-level warning.
    /// </summary>
    private static readonly TimeSpan ErrorThreshold = TimeSpan.FromSeconds(5);

    private readonly Stopwatch _stopwatch;
    private readonly string _testName;

    /// <summary>
    /// Creates a new slow test detector.
    /// </summary>
    /// <param name="testName">The test name (automatically filled by CallerMemberName)</param>
    public SlowTestDetector([CallerMemberName] string testName = "")
    {
        _testName = testName;
        _stopwatch = Stopwatch.StartNew();
    }

    /// <summary>
    /// Stops timing and reports if the test was slow.
    /// </summary>
    public void Dispose()
    {
        _stopwatch.Stop();
        var elapsed = _stopwatch.Elapsed;

        var logger = TestLoggerFactory.CreateLogger<SlowTestDetector>();

        if (elapsed > ErrorThreshold)
        {
            // Very slow test - log as warning for visibility in test logs
            logger.LogWarning("🔴 VERY SLOW TEST: {TestName} took {ElapsedSeconds:F2}s (threshold: {ThresholdSeconds}s)",
                _testName, elapsed.TotalSeconds, ErrorThreshold.TotalSeconds);
        }
        else if (elapsed > WarningThreshold)
        {
            // Slow test - log as information (less noisy than warning)
            logger.LogInformation("🟡 SLOW TEST: {TestName} took {ElapsedSeconds:F2}s (threshold: {ThresholdSeconds}s)",
                _testName, elapsed.TotalSeconds, WarningThreshold.TotalSeconds);
        }
    }
}

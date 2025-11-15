using NLog;
using NLog.Targets;
using Xunit;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// Custom NLog target that routes log messages to xUnit's TestContext for display in test output.
/// Uses TestContext.Current.SendDiagnosticMessage which works from anywhere, including fixtures.
/// </summary>
[Target("TestContext")]
public sealed class TestContextTarget : TargetWithLayout
{
    protected override void Write(LogEventInfo logEvent)
    {
        var message = RenderLogEvent(Layout, logEvent);

        try
        {
            // TestContext.Current is available from anywhere in xUnit v3
            TestContext.Current.SendDiagnosticMessage(message);
        }
        catch
        {
            // TestContext might not be available in some scenarios (e.g., outside test execution)
            // Silently ignore to avoid breaking non-test scenarios
        }
    }
}

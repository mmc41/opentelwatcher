namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// Default WatcherServer fixture that uses direct subprocess mode.
/// This is the fixture used by the main test collection.
///
/// For daemon mode testing, use <see cref="DaemonModeFixture"/> instead.
/// </summary>
public class OpenTelWatcherServerFixture : DirectSubprocessFixture
{
}

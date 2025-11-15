using OpenTelWatcher.Tests.E2E;

namespace E2ETests;

/// <summary>
/// xUnit collection definition for daemon mode E2E tests.
/// Uses DaemonModeFixture which starts the watcher with --daemon flag.
/// </summary>
[CollectionDefinition("Watcher Server Daemon")]
public class DaemonModeOpenTelWatcherServerCollection : ICollectionFixture<DaemonModeFixture>
{
}

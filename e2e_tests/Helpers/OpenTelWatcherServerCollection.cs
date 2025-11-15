using Xunit;

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// xUnit collection definition for black-box E2E tests that require a running Watcher subprocess.
///
/// Test classes that want to use the shared Watcher subprocess should be decorated with:
/// [Collection("Watcher Server")]
///
/// Benefits:
/// - One subprocess shared across all test classes in the collection
/// - Sequential execution within the collection (no parallelism)
/// - Automatic lifecycle management (startup/shutdown)
/// - Test classes can opt-in or opt-out by adding/removing the [Collection] attribute
///
/// Example:
/// <code>
/// [Collection("Watcher Server")]
/// public class MyBlackBoxTests
/// {
///     private readonly OpenTelWatcherServerFixture _fixture;
///
///     public MyBlackBoxTests(OpenTelWatcherServerFixture fixture)
///     {
///         _fixture = fixture;
///     }
///
///     [Fact]
///     public async Task MyTest()
///     {
///         var response = await _fixture.Client.GetAsync("/api/diagnose");
///         // assertions...
///     }
/// }
/// </code>
/// </summary>
[CollectionDefinition("Watcher Server")]
public class OpenTelWatcherServerCollection : ICollectionFixture<OpenTelWatcherServerFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}

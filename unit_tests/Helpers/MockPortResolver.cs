using OpenTelWatcher.CLI.Services;

namespace UnitTests.Helpers;

/// <summary>
/// Mock implementation of IPortResolver for unit testing.
/// Returns a fixed port number for all resolution requests.
/// </summary>
public class MockPortResolver : IPortResolver
{
    private readonly int _port;

    public MockPortResolver(int port = 4318)
    {
        _port = port;
    }

    public int ResolvePort(int? explicitPort)
    {
        return explicitPort ?? _port;
    }
}

namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// Thread-safe port allocator for E2E tests.
/// Prevents race conditions by atomically reserving ports from a shared pool.
/// </summary>
public static class PortAllocator
{
    private static readonly HashSet<int> _allocatedPorts = new();
    private static readonly object _lock = new();
    private const int MinPort = 6000;
    private const int MaxPort = 7000;
    private const int MaxAttempts = 100;

    /// <summary>
    /// Allocates a random port from the available pool (6000-7000).
    /// Thread-safe via locking.
    /// </summary>
    /// <returns>An allocated port number</returns>
    /// <exception cref="InvalidOperationException">Thrown if no ports are available after max attempts</exception>
    public static int Allocate()
    {
        lock (_lock)
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var port = Random.Shared.Next(MinPort, MaxPort);

                if (!_allocatedPorts.Contains(port))
                {
                    _allocatedPorts.Add(port);
                    return port;
                }
            }

            throw new InvalidOperationException(
                $"Failed to allocate port after {MaxAttempts} attempts. " +
                $"Allocated ports: {_allocatedPorts.Count}");
        }
    }

    /// <summary>
    /// Releases a previously allocated port back to the pool.
    /// Thread-safe via locking.
    /// </summary>
    /// <param name="port">The port to release</param>
    public static void Release(int port)
    {
        lock (_lock)
        {
            _allocatedPorts.Remove(port);
        }
    }

    /// <summary>
    /// Gets the count of currently allocated ports (for diagnostics).
    /// </summary>
    public static int AllocatedCount
    {
        get
        {
            lock (_lock)
            {
                return _allocatedPorts.Count;
            }
        }
    }

    /// <summary>
    /// Clears all allocated ports (use with caution - only for cleanup between test runs).
    /// </summary>
    internal static void Clear()
    {
        lock (_lock)
        {
            _allocatedPorts.Clear();
        }
    }
}

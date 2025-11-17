using OpenTelWatcher.Services.Interfaces;

namespace UnitTests.Helpers;

/// <summary>
/// Mock implementation of IProcessProvider for testing.
/// Allows controlling process lookup behavior for testing different scenarios.
/// </summary>
public class MockProcessProvider : IProcessProvider
{
    private readonly Dictionary<int, MockProcess> _processes = new();

    /// <summary>
    /// Add a mock process that will be returned when GetProcessById is called.
    /// </summary>
    public void AddProcess(int pid, string processName, bool hasExited = false)
    {
        _processes[pid] = new MockProcess
        {
            Id = pid,
            ProcessName = processName,
            HasExited = hasExited
        };
    }

    /// <summary>
    /// Remove a mock process from the provider.
    /// </summary>
    public void RemoveProcess(int pid)
    {
        _processes.Remove(pid);
    }

    /// <summary>
    /// Clear all mock processes.
    /// </summary>
    public void Clear()
    {
        _processes.Clear();
    }

    public IProcess? GetProcessById(int pid)
    {
        return _processes.TryGetValue(pid, out var process) ? process : null;
    }

    private class MockProcess : IProcess
    {
        public required int Id { get; init; }
        public required string ProcessName { get; init; }
        public required bool HasExited { get; init; }
    }
}

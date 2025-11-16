using OpenTelWatcher.Services.Interfaces;

namespace UnitTests.Helpers;

/// <summary>
/// Mock implementation of IPidFileService for testing.
/// </summary>
public class MockPidFileService : IPidFileService
{
    public List<PidEntry> Entries { get; set; } = new();
    public int RegisterCalls { get; private set; }
    public int UnregisterCalls { get; private set; }
    public int CleanStaleEntriesCalls { get; private set; }
    public int CleanedCount { get; set; }

    public string PidFilePath => "/mock/pid/file.pid";

    public void Register(int port)
    {
        RegisterCalls++;
        Entries.Add(new PidEntry
        {
            Pid = Environment.ProcessId,
            Port = port,
            Timestamp = DateTime.UtcNow
        });
    }

    public void Unregister()
    {
        UnregisterCalls++;
        Entries.RemoveAll(e => e.Pid == Environment.ProcessId);
    }

    public IReadOnlyList<PidEntry> GetRegisteredEntries() => Entries;

    public IReadOnlyList<PidEntry> GetRegisteredEntriesForPort(int port) =>
        Entries.Where(e => e.Port == port).ToList();

    public PidEntry? GetEntryByPid(int pid) =>
        Entries.FirstOrDefault(e => e.Pid == pid);

    public PidEntry? GetEntryByPort(int port) =>
        Entries.FirstOrDefault(e => e.Port == port);

    public int CleanStaleEntries()
    {
        CleanStaleEntriesCalls++;
        var removed = Entries.RemoveAll(e => !e.IsRunning());
        return CleanedCount > 0 ? CleanedCount : removed;
    }
}

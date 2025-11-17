using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services;

/// <summary>
/// Production implementation of IEnvironment that delegates to system environment classes.
/// </summary>
public sealed class EnvironmentAdapter : IEnvironment
{
    public int CurrentProcessId => Environment.ProcessId;

    public string BaseDirectory => AppContext.BaseDirectory;

    public string CurrentDirectory => Environment.CurrentDirectory;

    public string? ProcessPath => Environment.ProcessPath;

    public string TempPath => Path.GetTempPath();

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
}

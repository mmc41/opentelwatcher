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

    public string GetRuntimeDirectory()
    {
        // For development/testing: Use executable directory if running from artifacts
        var executableDir = BaseDirectory;
        if (executableDir.Contains("artifacts"))
        {
            return executableDir;
        }

        // For production deployments: Use platform-appropriate temp directory
        // Linux/macOS: XDG_RUNTIME_DIR provides per-user runtime directory
        // Windows: Path.GetTempPath() provides user temp directory
        var xdgRuntimeDir = GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(xdgRuntimeDir) && Directory.Exists(xdgRuntimeDir))
        {
            return xdgRuntimeDir;
        }

        // Fallback to OS temp directory
        return TempPath;
    }
}

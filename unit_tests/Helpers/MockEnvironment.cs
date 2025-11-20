using OpenTelWatcher.Services.Interfaces;

namespace UnitTests.Helpers;

/// <summary>
/// Mock implementation of IEnvironment for testing.
/// Allows controlling environment properties without system dependencies.
/// </summary>
public class MockEnvironment : IEnvironment
{
    private readonly Dictionary<string, string> _environmentVariables = new();

    public int CurrentProcessId { get; set; } = Environment.ProcessId;

    public string BaseDirectory { get; set; } = AppContext.BaseDirectory;

    public string CurrentDirectory { get; set; } = Environment.CurrentDirectory;

    public string? ProcessPath { get; set; } = Environment.ProcessPath;

    public string TempPath { get; set; } = Path.GetTempPath();

    /// <summary>
    /// Set an environment variable for testing.
    /// </summary>
    public void SetEnvironmentVariable(string name, string value)
    {
        _environmentVariables[name] = value;
    }

    /// <summary>
    /// Remove an environment variable.
    /// </summary>
    public void RemoveEnvironmentVariable(string name)
    {
        _environmentVariables.Remove(name);
    }

    /// <summary>
    /// Clear all environment variables.
    /// </summary>
    public void ClearEnvironmentVariables()
    {
        _environmentVariables.Clear();
    }

    public string? GetEnvironmentVariable(string name)
    {
        return _environmentVariables.TryGetValue(name, out var value) ? value : null;
    }

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

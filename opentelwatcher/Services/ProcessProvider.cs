using System.Diagnostics;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services;

/// <summary>
/// Production implementation of IProcessProvider that delegates to System.Diagnostics.Process.
/// </summary>
public sealed class ProcessProvider : IProcessProvider
{
    public IProcess? GetProcessById(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            return new ProcessAdapter(process);
        }
        catch (ArgumentException)
        {
            // Process not found
            return null;
        }
        catch (Exception)
        {
            // Access denied or other error
            return null;
        }
    }

    /// <summary>
    /// Adapter that wraps System.Diagnostics.Process to implement IProcess.
    /// </summary>
    private sealed class ProcessAdapter : IProcess
    {
        private readonly Process _process;

        public ProcessAdapter(Process process)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
        }

        public int Id => _process.Id;

        public string ProcessName => _process.ProcessName;

        public bool HasExited => _process.HasExited;
    }
}

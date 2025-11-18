using Microsoft.Extensions.Logging;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.CLI.Services;

/// <summary>
/// Resolves the port number for CLI commands by either using an explicitly provided port
/// or auto-resolving from the PID file when a single instance is running.
/// </summary>
public class PortResolver : IPortResolver
{
    private readonly IPidFileService _pidFileService;
    private readonly IProcessProvider _processProvider;
    private readonly ILogger<PortResolver> _logger;

    public PortResolver(
        IPidFileService pidFileService,
        IProcessProvider processProvider,
        ILogger<PortResolver> logger)
    {
        _pidFileService = pidFileService ?? throw new ArgumentNullException(nameof(pidFileService));
        _processProvider = processProvider ?? throw new ArgumentNullException(nameof(processProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public int ResolvePort(int? explicitPort)
    {
        // 1. If explicit port provided, use it
        if (explicitPort.HasValue)
        {
            _logger.LogDebug("Using explicit port {Port}", explicitPort.Value);
            return explicitPort.Value;
        }

        // 2. Read PID file entries
        var entries = _pidFileService.GetRegisteredEntries();
        _logger.LogDebug("Found {Count} PID file entries", entries.Count);

        // 3. Filter out stale entries (dead processes)
        var activeEntries = entries.Where(entry => IsProcessAlive(entry.Pid)).ToList();
        _logger.LogDebug("Found {Count} active process entries after filtering stale PIDs", activeEntries.Count);

        // 4. Handle cases based on active entry count
        if (activeEntries.Count == 0)
        {
            _logger.LogWarning("No running instances found. Cannot determine port automatically.");
            throw new InvalidOperationException(
                "No running instances found. Cannot determine port automatically.\n\n" +
                "To start a new instance:\n" +
                "  opentelwatcher start --port 4318\n\n" +
                "Or specify port explicitly:\n" +
                "  opentelwatcher <command> --port 4318");
        }

        if (activeEntries.Count > 1)
        {
            var ports = string.Join(", ", activeEntries.Select(e => e.Port).OrderBy(p => p));
            _logger.LogWarning("Multiple instances running on ports: {Ports}", ports);
            throw new InvalidOperationException(
                $"Multiple instances running on ports: {ports}\n\n" +
                "Please specify which instance to target:\n" +
                string.Join("\n", activeEntries.Select(e => $"  opentelwatcher <command> --port {e.Port}")));
        }

        // 5. Single active entry found - auto-resolve to its port
        var resolvedPort = activeEntries[0].Port;
        _logger.LogInformation("Auto-resolved port {Port} from PID file (PID: {Pid})",
            resolvedPort, activeEntries[0].Pid);
        return resolvedPort;
    }

    /// <summary>
    /// Checks if a process is alive (running and not exited).
    /// </summary>
    private bool IsProcessAlive(int pid)
    {
        try
        {
            var process = _processProvider.GetProcessById(pid);
            var isAlive = process != null && !process.HasExited;

            if (!isAlive)
            {
                _logger.LogDebug("Process {Pid} is dead or has exited", pid);
            }

            return isAlive;
        }
        catch (ArgumentException)
        {
            // Process doesn't exist
            _logger.LogDebug("Process {Pid} does not exist", pid);
            return false;
        }
        catch (InvalidOperationException)
        {
            // Process has exited or access denied
            _logger.LogDebug("Process {Pid} has exited or access denied", pid);
            return false;
        }
    }
}

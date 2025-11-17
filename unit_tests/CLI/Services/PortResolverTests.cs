using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenTelWatcher.CLI.Services;
using OpenTelWatcher.Services;
using OpenTelWatcher.Services.Interfaces;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.CLI.Services;

/// <summary>
/// Unit tests for PortResolver.
/// Tests automatic port resolution from PID file when no explicit port provided.
/// </summary>
public class PortResolverTests
{
    private readonly MockPidFileService _mockPidFileService;
    private readonly MockProcessProvider _mockProcessProvider;
    private readonly ILogger<PortResolver> _logger;

    public PortResolverTests()
    {
        _mockProcessProvider = new MockProcessProvider();
        _mockPidFileService = new MockPidFileService(_mockProcessProvider);
        _logger = TestLoggerFactory.CreateLogger<PortResolver>();
    }

    [Fact]
    public void ResolvePort_WithExplicitPort_ReturnsExplicitPort()
    {
        // Arrange
        var resolver = new PortResolver(_mockPidFileService, _mockProcessProvider, _logger);
        var explicitPort = 5318;

        // Act
        var result = resolver.ResolvePort(explicitPort);

        // Assert
        result.Should().Be(explicitPort, "explicit port should always be returned");
    }

    [Fact]
    public void ResolvePort_WithSinglePidEntry_ReturnsEntryPort()
    {
        // Arrange
        var testPid = 12345;
        var testPort = 5318;
        _mockProcessProvider.AddProcess(testPid, "opentelwatcher", hasExited: false);
        _mockPidFileService.Entries.Add(new PidEntry
        {
            Pid = testPid,
            Port = testPort,
            Timestamp = DateTime.UtcNow
        });

        var resolver = new PortResolver(_mockPidFileService, _mockProcessProvider, _logger);

        // Act
        var result = resolver.ResolvePort(null);

        // Assert
        result.Should().Be(testPort, "should auto-resolve to the single PID entry port");
    }

    [Fact]
    public void ResolvePort_WithNoPidEntries_ThrowsException()
    {
        // Arrange
        var resolver = new PortResolver(_mockPidFileService, _mockProcessProvider, _logger);

        // Act
        var act = () => resolver.ResolvePort(null);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No running instances found*");
    }

    [Fact]
    public void ResolvePort_WithMultiplePidEntries_ThrowsException()
    {
        // Arrange
        var pid1 = 12345;
        var pid2 = 67890;
        var port1 = 4318;
        var port2 = 5318;

        _mockProcessProvider.AddProcess(pid1, "opentelwatcher", hasExited: false);
        _mockProcessProvider.AddProcess(pid2, "opentelwatcher", hasExited: false);
        _mockPidFileService.Entries.Add(new PidEntry { Pid = pid1, Port = port1, Timestamp = DateTime.UtcNow });
        _mockPidFileService.Entries.Add(new PidEntry { Pid = pid2, Port = port2, Timestamp = DateTime.UtcNow });

        var resolver = new PortResolver(_mockPidFileService, _mockProcessProvider, _logger);

        // Act
        var act = () => resolver.ResolvePort(null);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Multiple instances running*")
            .WithMessage("*4318*")
            .WithMessage("*5318*");
    }

    [Fact]
    public void ResolvePort_WithStalePidEntries_IgnoresThem_ReturnsValidPort()
    {
        // Arrange
        var alivePid = 12345;
        var deadPid = 99999;
        var alivePort = 5318;
        var deadPort = 6318;

        // Only alive process is registered
        _mockProcessProvider.AddProcess(alivePid, "opentelwatcher", hasExited: false);
        // Dead process not added to provider (simulates dead process)

        _mockPidFileService.Entries.Add(new PidEntry { Pid = alivePid, Port = alivePort, Timestamp = DateTime.UtcNow });
        _mockPidFileService.Entries.Add(new PidEntry { Pid = deadPid, Port = deadPort, Timestamp = DateTime.UtcNow });

        var resolver = new PortResolver(_mockPidFileService, _mockProcessProvider, _logger);

        // Act
        var result = resolver.ResolvePort(null);

        // Assert
        result.Should().Be(alivePort, "should ignore stale PID entries and return the alive process port");
    }

    [Fact]
    public void ResolvePort_WithAllStalePidEntries_ThrowsException()
    {
        // Arrange
        var deadPid1 = 88888;
        var deadPid2 = 99999;

        // No processes registered as alive
        _mockPidFileService.Entries.Add(new PidEntry { Pid = deadPid1, Port = 4318, Timestamp = DateTime.UtcNow });
        _mockPidFileService.Entries.Add(new PidEntry { Pid = deadPid2, Port = 5318, Timestamp = DateTime.UtcNow });

        var resolver = new PortResolver(_mockPidFileService, _mockProcessProvider, _logger);

        // Act
        var act = () => resolver.ResolvePort(null);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No running instances found*");
    }

    [Fact]
    public void ResolvePort_WithNoPidFile_ThrowsException()
    {
        // Arrange
        var resolver = new PortResolver(_mockPidFileService, _mockProcessProvider, _logger);

        // Act
        var act = () => resolver.ResolvePort(null);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No running instances found*");
    }

    [Fact]
    public void ResolvePort_WithExplicitPort_DoesNotReadPidFile()
    {
        // Arrange
        // Register entries that would cause multi-instance error
        var pid1 = 12345;
        var pid2 = 67890;
        _mockProcessProvider.AddProcess(pid1, "opentelwatcher", hasExited: false);
        _mockProcessProvider.AddProcess(pid2, "opentelwatcher", hasExited: false);
        _mockPidFileService.Entries.Add(new PidEntry { Pid = pid1, Port = 4318, Timestamp = DateTime.UtcNow });
        _mockPidFileService.Entries.Add(new PidEntry { Pid = pid2, Port = 5318, Timestamp = DateTime.UtcNow });

        var resolver = new PortResolver(_mockPidFileService, _mockProcessProvider, _logger);
        var explicitPort = 6318;

        // Act
        var result = resolver.ResolvePort(explicitPort);

        // Assert
        result.Should().Be(explicitPort, "explicit port should bypass PID file resolution");
    }

    [Fact]
    public void ResolvePort_WithExitedProcess_FiltersItOut()
    {
        // Arrange
        var exitedPid = 99999;
        var exitedPort = 4318;

        // Process exists but has exited
        _mockProcessProvider.AddProcess(exitedPid, "opentelwatcher", hasExited: true);
        _mockPidFileService.Entries.Add(new PidEntry { Pid = exitedPid, Port = exitedPort, Timestamp = DateTime.UtcNow });

        var resolver = new PortResolver(_mockPidFileService, _mockProcessProvider, _logger);

        // Act
        var act = () => resolver.ResolvePort(null);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No running instances found*",
                "exited processes should be filtered out, resulting in no active instances");
    }

    [Fact]
    public void ResolvePort_WithMixedExitedAndRunningProcesses_ReturnsRunningProcessPort()
    {
        // Arrange
        var exitedPid = 11111;
        var runningPid = 22222;
        var runningPort = 5318;

        // One exited process, one running process
        _mockProcessProvider.AddProcess(exitedPid, "opentelwatcher", hasExited: true);
        _mockProcessProvider.AddProcess(runningPid, "opentelwatcher", hasExited: false);
        _mockPidFileService.Entries.Add(new PidEntry { Pid = exitedPid, Port = 4318, Timestamp = DateTime.UtcNow });
        _mockPidFileService.Entries.Add(new PidEntry { Pid = runningPid, Port = runningPort, Timestamp = DateTime.UtcNow });

        var resolver = new PortResolver(_mockPidFileService, _mockProcessProvider, _logger);

        // Act
        var result = resolver.ResolvePort(null);

        // Assert
        result.Should().Be(runningPort, "should filter out exited process and return the running process port");
    }
}

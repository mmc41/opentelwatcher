using FluentAssertions;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Services;
using OpenTelWatcher.Services.Interfaces;
using UnitTests.Helpers;
using Xunit;

namespace OpenTelWatcher.Tests.Services;

public class DiagnosticsCollectorTests : IDisposable
{
    private readonly string _testOutputDir;
    private readonly ITelemetryStatistics _statistics;

    public DiagnosticsCollectorTests()
    {
        _testOutputDir = Path.Combine(Path.GetTempPath(), "diagnostics-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testOutputDir);
        _statistics = new TelemetryStatisticsService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testOutputDir))
        {
            Directory.Delete(_testOutputDir, recursive: true);
        }
    }

    [Fact]
    public void GetHealthStatus_WithHealthyMonitor_ReturnsHealthy()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = _testOutputDir,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };
        var healthMonitor = new HealthMonitor(options);
        var logger = TestLoggerFactory.CreateLogger<DiagnosticsCollector>();
        var collector = new DiagnosticsCollector(options, healthMonitor, _statistics, logger);

        // Act
        var status = collector.GetHealthStatus();

        // Assert
        status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void GetHealthStatus_WithDegradedMonitor_ReturnsDegraded()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = _testOutputDir,
            MaxConsecutiveFileErrors = 2,
            MaxErrorHistorySize = 50
        };
        var healthMonitor = new HealthMonitor(options);

        // Degrade health by recording errors
        healthMonitor.RecordError("Error 1");
        healthMonitor.RecordError("Error 2");

        var logger = TestLoggerFactory.CreateLogger<DiagnosticsCollector>();
        var collector = new DiagnosticsCollector(options, healthMonitor, _statistics, logger);

        // Act
        var status = collector.GetHealthStatus();

        // Assert
        status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public void GetRecentErrors_WithNoErrors_ReturnsEmptyList()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = _testOutputDir,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };
        var healthMonitor = new HealthMonitor(options);
        var logger = TestLoggerFactory.CreateLogger<DiagnosticsCollector>();
        var collector = new DiagnosticsCollector(options, healthMonitor, _statistics, logger);

        // Act
        var errors = collector.GetRecentErrors();

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public void GetRecentErrors_WithErrors_ReturnsErrorList()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = _testOutputDir,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };
        var healthMonitor = new HealthMonitor(options);

        healthMonitor.RecordError("Error 1");
        healthMonitor.RecordError("Error 2");

        var logger = TestLoggerFactory.CreateLogger<DiagnosticsCollector>();
        var collector = new DiagnosticsCollector(options, healthMonitor, _statistics, logger);

        // Act
        var errors = collector.GetRecentErrors();

        // Assert
        errors.Should().HaveCount(2);
        errors.Should().Contain("Error 1");
        errors.Should().Contain("Error 2");
    }

    [Fact]
    public void GetConsecutiveErrorCount_WithNoErrors_ReturnsZero()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = _testOutputDir,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };
        var healthMonitor = new HealthMonitor(options);
        var logger = TestLoggerFactory.CreateLogger<DiagnosticsCollector>();
        var collector = new DiagnosticsCollector(options, healthMonitor, _statistics, logger);

        // Act
        var count = collector.GetConsecutiveErrorCount();

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public void GetConsecutiveErrorCount_WithErrors_ReturnsCount()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = _testOutputDir,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };
        var healthMonitor = new HealthMonitor(options);

        healthMonitor.RecordError("Error 1");
        healthMonitor.RecordError("Error 2");
        healthMonitor.RecordError("Error 3");

        var logger = TestLoggerFactory.CreateLogger<DiagnosticsCollector>();
        var collector = new DiagnosticsCollector(options, healthMonitor, _statistics, logger);

        // Act
        var count = collector.GetConsecutiveErrorCount();

        // Assert
        count.Should().Be(3);
    }

    [Fact]
    public void GetOutputDirectory_ReturnsConfiguredDirectory()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = _testOutputDir,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };
        var healthMonitor = new HealthMonitor(options);
        var logger = TestLoggerFactory.CreateLogger<DiagnosticsCollector>();
        var collector = new DiagnosticsCollector(options, healthMonitor, _statistics, logger);

        // Act
        var outputDir = collector.GetOutputDirectory();

        // Assert
        outputDir.Should().Be(_testOutputDir);
    }

    [Fact]
    public void GetFileInfo_WithNoFiles_ReturnsEmptyList()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = _testOutputDir,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };
        var healthMonitor = new HealthMonitor(options);
        var logger = TestLoggerFactory.CreateLogger<DiagnosticsCollector>();
        var collector = new DiagnosticsCollector(options, healthMonitor, _statistics, logger);

        // Act
        var fileInfos = collector.GetFileInfo();

        // Assert
        fileInfos.Should().BeEmpty();
    }

    [Fact]
    public void GetFileInfo_WithFiles_ReturnsFileInformation()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = _testOutputDir,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };
        var healthMonitor = new HealthMonitor(options);
        var logger = TestLoggerFactory.CreateLogger<DiagnosticsCollector>();
        var collector = new DiagnosticsCollector(options, healthMonitor, _statistics, logger);

        // Create test files
        var tracesFile = Path.Combine(_testOutputDir, "traces.20251108_120000.ndjson");
        var logsFile = Path.Combine(_testOutputDir, "logs.20251108_120000.ndjson");
        File.WriteAllText(tracesFile, "{\"test\":\"data\"}\n");
        File.WriteAllText(logsFile, "{\"test\":\"data\"}\n");

        // Act
        var fileInfos = collector.GetFileInfo();

        // Assert
        fileInfos.Should().HaveCount(2);
        fileInfos.Should().Contain(fi => fi.Path == tracesFile);
        fileInfos.Should().Contain(fi => fi.Path == logsFile);
    }

    [Fact]
    public void GetFileInfo_WithSignalFilter_ReturnsOnlyMatchingFiles()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = _testOutputDir,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };
        var healthMonitor = new HealthMonitor(options);
        var logger = TestLoggerFactory.CreateLogger<DiagnosticsCollector>();
        var collector = new DiagnosticsCollector(options, healthMonitor, _statistics, logger);

        // Create test files
        var tracesFile = Path.Combine(_testOutputDir, "traces.20251108_120000.ndjson");
        var logsFile = Path.Combine(_testOutputDir, "logs.20251108_120000.ndjson");
        File.WriteAllText(tracesFile, "{\"test\":\"data\"}\n");
        File.WriteAllText(logsFile, "{\"test\":\"data\"}\n");

        // Act
        var fileInfos = collector.GetFileInfo("traces");

        // Assert
        fileInfos.Should().HaveCount(1);
        fileInfos.Should().Contain(fi => fi.Path == tracesFile);
    }

    [Fact]
    public void GetFileInfo_IncludesSizeAndLastModified()
    {
        // Arrange
        var options = new OpenTelWatcherOptions
        {
            OutputDirectory = _testOutputDir,
            MaxConsecutiveFileErrors = 10,
            MaxErrorHistorySize = 50
        };
        var healthMonitor = new HealthMonitor(options);
        var logger = TestLoggerFactory.CreateLogger<DiagnosticsCollector>();
        var collector = new DiagnosticsCollector(options, healthMonitor, _statistics, logger);

        // Create test file with known content
        var tracesFile = Path.Combine(_testOutputDir, "traces.20251108_120000.ndjson");
        var content = "{\"test\":\"data\"}\n";
        File.WriteAllText(tracesFile, content);

        // Act
        var fileInfos = collector.GetFileInfo();

        // Assert
        fileInfos.Should().HaveCount(1);
        var fileInfo = fileInfos.First();
        fileInfo.SizeBytes.Should().BeGreaterThan(0);
        fileInfo.LastModified.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}

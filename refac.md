# Refactoring Plan: Pipeline Architecture for Telemetry Writing

## Overview

Refactor the existing `TelemetryFileWriter` service to use a **pipeline architecture** where telemetry data flows through a central pipeline to multiple receivers with different filters.

**Goal**: Replace direct file writing with a modular, extensible pipeline while preserving all existing functionality.

## Current vs. Target Architecture

### Current (Before)
```
OTLP Request → TelemetryFileWriter
                   ↓
   [Serialize: Protobuf → JSON → NDJSON]
   [Detect Errors]
                   ↓
   [Write to .ndjson file]
                   ↓
   [If error: Write to .errors.ndjson file]
```

### Target (After)
```
OTLP Request → TelemetryPipeline
                   ↓
   [Serialize: Protobuf → JSON → NDJSON]
   [Detect Errors: ErrorDetectionService]
                   ↓
         Create TelemetryItem
                   ↓
   For each (Receiver, Filter) pair:
       ↓
   [Filter.ShouldWrite(item)]
       ↓ (if true)
   [Receiver.WriteAsync(item)]
```

**Registered Receivers**:
1. `FileReceiver` (extension: ".ndjson") + `AllSignalsFilter` → All telemetry
2. `FileReceiver` (extension: ".errors.ndjson") + `ErrorsOnlyFilter` → Errors only

## Success Criteria

- ✅ All existing E2E tests pass without modification
- ✅ Normal files still written to `{signal}.{timestamp}.ndjson`
- ✅ Error files still written to `{signal}.{timestamp}.errors.ndjson`
- ✅ File rotation works identically
- ✅ Disk space checking works identically
- ✅ Thread-safe concurrent writes preserved
- ✅ No changes to OTLP endpoint behavior
- ✅ No changes to file format or naming

## Implementation Steps (TDD)

### Step 1: Create Core Abstractions

**Files to Create**:
- `Services/Interfaces/ITelemetryReceiver.cs`
- `Services/Interfaces/ITelemetryFilter.cs`
- `Services/Interfaces/ITelemetryPipeline.cs`
- `Models/TelemetryItem.cs`

**ITelemetryReceiver.cs**:
```csharp
namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Receives telemetry items from the pipeline and processes them.
/// </summary>
public interface ITelemetryReceiver
{
    /// <summary>
    /// Writes a telemetry item to the receiver's destination.
    /// </summary>
    Task WriteAsync(TelemetryItem item, CancellationToken cancellationToken);
}
```

**ITelemetryFilter.cs**:
```csharp
namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Determines whether a telemetry item should be processed by a receiver.
/// </summary>
public interface ITelemetryFilter
{
    /// <summary>
    /// Returns true if the item should be written by the receiver.
    /// </summary>
    bool ShouldWrite(TelemetryItem item);
}
```

**ITelemetryPipeline.cs**:
```csharp
namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Central pipeline for processing telemetry data through multiple receivers.
/// </summary>
public interface ITelemetryPipeline
{
    /// <summary>
    /// Writes a telemetry message through the pipeline.
    /// </summary>
    Task WriteAsync<T>(T message, string signal, CancellationToken cancellationToken);

    /// <summary>
    /// Registers a receiver with one or more filters to process telemetry items.
    /// All filters must return true for the item to be written.
    /// </summary>
    void RegisterReceiver(ITelemetryReceiver receiver, params ITelemetryFilter[] filters);
}
```

**Models/TelemetryItem.cs**:
```csharp
namespace OpenTelWatcher.Models;

/// <summary>
/// Represents a processed telemetry item ready for receiver consumption.
/// </summary>
public sealed record TelemetryItem(
    string Signal,              // "traces", "logs", "metrics"
    string NdjsonLine,          // Pre-serialized NDJSON string (includes \n)
    bool IsError,               // Pre-detected error status
    DateTimeOffset Timestamp    // When item was created (UTC)
);
```

**Verification**: Compile successfully, no tests yet.

---

### Step 2: Implement Filters (TDD)

#### Step 2.1: AllSignalsFilter

**Test First**: Create `unit_tests/Services/Filters/AllSignalsFilterTests.cs`

```csharp
using FluentAssertions;
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Filters;
using Xunit;

namespace OpenTelWatcher.Tests.Services.Filters;

public class AllSignalsFilterTests
{
    private readonly AllSignalsFilter _filter = new();

    [Fact]
    public void ShouldWrite_ReturnsTrue_ForNonErrorTraces()
    {
        // Arrange
        var item = new TelemetryItem(
            Signal: "traces",
            NdjsonLine: "{\"traceId\":\"123\"}\n",
            IsError: false,
            Timestamp: DateTimeOffset.UtcNow);

        // Act
        var result = _filter.ShouldWrite(item);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldWrite_ReturnsTrue_ForErrorLogs()
    {
        // Arrange
        var item = new TelemetryItem(
            Signal: "logs",
            NdjsonLine: "{\"severityNumber\":17}\n",
            IsError: true,
            Timestamp: DateTimeOffset.UtcNow);

        // Act
        var result = _filter.ShouldWrite(item);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("traces")]
    [InlineData("logs")]
    [InlineData("metrics")]
    public void ShouldWrite_ReturnsTrue_ForAllSignalTypes(string signal)
    {
        // Arrange
        var item = new TelemetryItem(signal, "{}\n", false, DateTimeOffset.UtcNow);

        // Act
        var result = _filter.ShouldWrite(item);

        // Assert
        result.Should().BeTrue();
    }
}
```

**Run Tests**: `dotnet test unit_tests/Services/Filters/AllSignalsFilterTests.cs` → ❌ Fails (class doesn't exist)

**Implement**: Create `Services/Filters/AllSignalsFilter.cs`

```csharp
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services.Filters;

/// <summary>
/// Filter that accepts all telemetry items regardless of signal type or error status.
/// </summary>
public sealed class AllSignalsFilter : ITelemetryFilter
{
    public bool ShouldWrite(TelemetryItem item) => true;
}
```

**Run Tests**: `dotnet test unit_tests/Services/Filters/AllSignalsFilterTests.cs` → ✅ Passes

---

#### Step 2.2: ErrorsOnlyFilter

**Test First**: Create `unit_tests/Services/Filters/ErrorsOnlyFilterTests.cs`

```csharp
using FluentAssertions;
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Filters;
using Xunit;

namespace OpenTelWatcher.Tests.Services.Filters;

public class ErrorsOnlyFilterTests
{
    private readonly ErrorsOnlyFilter _filter = new();

    [Fact]
    public void ShouldWrite_ReturnsTrue_WhenItemIsError()
    {
        // Arrange
        var item = new TelemetryItem(
            Signal: "traces",
            NdjsonLine: "{\"status\":{\"code\":2}}\n",
            IsError: true,
            Timestamp: DateTimeOffset.UtcNow);

        // Act
        var result = _filter.ShouldWrite(item);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldWrite_ReturnsFalse_WhenItemIsNotError()
    {
        // Arrange
        var item = new TelemetryItem(
            Signal: "traces",
            NdjsonLine: "{\"status\":{\"code\":1}}\n",
            IsError: false,
            Timestamp: DateTimeOffset.UtcNow);

        // Act
        var result = _filter.ShouldWrite(item);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("traces")]
    [InlineData("logs")]
    [InlineData("metrics")]
    public void ShouldWrite_WorksForAllSignalTypes_WhenError(string signal)
    {
        // Arrange
        var item = new TelemetryItem(signal, "{}\n", IsError: true, DateTimeOffset.UtcNow);

        // Act
        var result = _filter.ShouldWrite(item);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("traces")]
    [InlineData("logs")]
    [InlineData("metrics")]
    public void ShouldWrite_WorksForAllSignalTypes_WhenNotError(string signal)
    {
        // Arrange
        var item = new TelemetryItem(signal, "{}\n", IsError: false, DateTimeOffset.UtcNow);

        // Act
        var result = _filter.ShouldWrite(item);

        // Assert
        result.Should().BeFalse();
    }
}
```

**Run Tests**: `dotnet test unit_tests/Services/Filters/ErrorsOnlyFilterTests.cs` → ❌ Fails

**Implement**: Create `Services/Filters/ErrorsOnlyFilter.cs`

```csharp
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services.Filters;

/// <summary>
/// Filter that only accepts telemetry items marked as errors.
/// </summary>
public sealed class ErrorsOnlyFilter : ITelemetryFilter
{
    public bool ShouldWrite(TelemetryItem item) => item.IsError;
}
```

**Run Tests**: `dotnet test unit_tests/Services/Filters/ErrorsOnlyFilterTests.cs` → ✅ Passes

---

### Step 3: Implement FileReceiver (TDD)

**Test First**: Create `unit_tests/Services/Receivers/FileReceiverTests.cs`

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Interfaces;
using OpenTelWatcher.Services.Receivers;
using OpenTelWatcher.Tests.Infrastructure;
using Xunit;

namespace OpenTelWatcher.Tests.Services.Receivers;

public class FileReceiverTests : FileBasedTestBase
{
    [Fact]
    public async Task WriteAsync_WritesNdjsonToFile_WithNormalExtension()
    {
        // Arrange
        var rotationService = CreateMockRotationService(TestDirectory, "traces");
        var diskSpaceChecker = CreateMockDiskSpaceChecker(hasSufficientSpace: true);
        var receiver = new FileReceiver(
            rotationService.Object,
            diskSpaceChecker.Object,
            TestDirectory,
            ".ndjson",
            NullLogger<FileReceiver>.Instance);

        var item = new TelemetryItem(
            "traces",
            "{\"traceId\":\"123\"}\n",
            false,
            DateTimeOffset.UtcNow);

        // Act
        await receiver.WriteAsync(item, TestContext.Current.CancellationToken);

        // Assert
        var expectedFile = Path.Combine(TestDirectory, "traces.20250119_120000_000.ndjson");
        File.Exists(expectedFile).Should().BeTrue();
        var content = await File.ReadAllTextAsync(expectedFile);
        content.Should().Be("{\"traceId\":\"123\"}\n");
    }

    [Fact]
    public async Task WriteAsync_WritesNdjsonToFile_WithErrorExtension()
    {
        // Arrange
        var rotationService = CreateMockRotationService(TestDirectory, "logs");
        var diskSpaceChecker = CreateMockDiskSpaceChecker(hasSufficientSpace: true);
        var receiver = new FileReceiver(
            rotationService.Object,
            diskSpaceChecker.Object,
            TestDirectory,
            ".errors.ndjson",
            NullLogger<FileReceiver>.Instance);

        var item = new TelemetryItem(
            "logs",
            "{\"severityNumber\":17}\n",
            true,
            DateTimeOffset.UtcNow);

        // Act
        await receiver.WriteAsync(item, TestContext.Current.CancellationToken);

        // Assert
        var expectedFile = Path.Combine(TestDirectory, "logs.20250119_120000_000.errors.ndjson");
        File.Exists(expectedFile).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_ThreadSafe_ConcurrentWritesToSameSignal()
    {
        // Arrange
        var rotationService = CreateMockRotationService(TestDirectory, "traces");
        var diskSpaceChecker = CreateMockDiskSpaceChecker(hasSufficientSpace: true);
        var receiver = new FileReceiver(
            rotationService.Object,
            diskSpaceChecker.Object,
            TestDirectory,
            ".ndjson",
            NullLogger<FileReceiver>.Instance);

        var items = Enumerable.Range(0, 100).Select(i => new TelemetryItem(
            "traces",
            $"{{\"id\":{i}}}\n",
            false,
            DateTimeOffset.UtcNow)).ToList();

        // Act
        await Parallel.ForEachAsync(items, async (item, ct) =>
        {
            await receiver.WriteAsync(item, ct);
        });

        // Assert
        var expectedFile = Path.Combine(TestDirectory, "traces.20250119_120000_000.ndjson");
        var lines = await File.ReadAllLinesAsync(expectedFile);
        lines.Should().HaveCount(100);
    }

    // Helper methods
    private Mock<IFileRotationService> CreateMockRotationService(string outputDir, string signal)
    {
        var mock = new Mock<IFileRotationService>();
        var filePath = Path.Combine(outputDir, $"{signal}.20250119_120000_000.ndjson");
        mock.Setup(x => x.GetOrCreateFilePath(outputDir, signal)).Returns(filePath);
        mock.Setup(x => x.ShouldRotate(It.IsAny<string>())).Returns(false);
        return mock;
    }

    private Mock<IDiskSpaceChecker> CreateMockDiskSpaceChecker(bool hasSufficientSpace)
    {
        var mock = new Mock<IDiskSpaceChecker>();
        mock.Setup(x => x.HasSufficientSpace(It.IsAny<string>())).Returns(hasSufficientSpace);
        return mock;
    }
}
```

**Additional Tests to Add**:
- File rotation when size exceeds limit
- Disk space check prevents write
- Concurrent writes to different signals
- Proper semaphore cleanup on disposal

**Run Tests**: `dotnet test unit_tests/Services/Receivers/FileReceiverTests.cs` → ❌ Fails

**Implement**: Create `Services/Receivers/FileReceiver.cs`

```csharp
using System.Collections.Concurrent;
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services.Receivers;

/// <summary>
/// Writes telemetry items to NDJSON files with configurable file extension.
/// </summary>
public sealed class FileReceiver : ITelemetryReceiver, IDisposable
{
    private readonly IFileRotationService _rotationService;
    private readonly IDiskSpaceChecker _diskSpaceChecker;
    private readonly string _outputDirectory;
    private readonly string _fileExtension;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _signalLocks;
    private readonly ILogger<FileReceiver> _logger;

    public FileReceiver(
        IFileRotationService rotationService,
        IDiskSpaceChecker diskSpaceChecker,
        string outputDirectory,
        string fileExtension,
        ILogger<FileReceiver> logger)
    {
        _rotationService = rotationService;
        _diskSpaceChecker = diskSpaceChecker;
        _outputDirectory = outputDirectory;
        _fileExtension = fileExtension;
        _signalLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        _logger = logger;
    }

    public async Task WriteAsync(TelemetryItem item, CancellationToken cancellationToken)
    {
        var semaphore = _signalLocks.GetOrAdd(item.Signal, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            // Get base path from rotation service
            var basePath = _rotationService.GetOrCreateFilePath(_outputDirectory, item.Signal);

            // Transform to target extension
            var targetPath = basePath.Replace(".ndjson", _fileExtension);

            // Check rotation
            if (File.Exists(targetPath) && _rotationService.ShouldRotate(targetPath))
            {
                var newBasePath = _rotationService.RotateFile(_outputDirectory, item.Signal);
                targetPath = newBasePath.Replace(".ndjson", _fileExtension);
            }

            // Check disk space
            if (!_diskSpaceChecker.HasSufficientSpace(_outputDirectory))
            {
                _logger.LogWarning("Insufficient disk space in {Directory}", _outputDirectory);
                return;
            }

            // Write
            await File.AppendAllTextAsync(targetPath, item.NdjsonLine, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public void Dispose()
    {
        foreach (var semaphore in _signalLocks.Values)
        {
            semaphore.Dispose();
        }
        _signalLocks.Clear();
    }
}
```

**Run Tests**: `dotnet test unit_tests/Services/Receivers/FileReceiverTests.cs` → ✅ Passes

---

### Step 4: Implement TelemetryPipeline (TDD)

**Test First**: Create `unit_tests/Services/TelemetryPipelineTests.cs`

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelWatcher.Models;
using OpenTelWatcher.Services;
using OpenTelWatcher.Services.Interfaces;
using Xunit;

namespace OpenTelWatcher.Tests.Services;

public class TelemetryPipelineTests
{
    [Fact]
    public void RegisterReceiver_AddsReceiverToList()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var receiver = new MockReceiver();
        var filter = new MockFilter(shouldWrite: true);

        // Act
        pipeline.RegisterReceiver(receiver, filter);

        // Assert - verify via WriteAsync behavior
        // (internal list not exposed, tested indirectly)
    }

    [Fact]
    public async Task WriteAsync_CallsAllRegisteredReceivers()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var receiver1 = new MockReceiver();
        var receiver2 = new MockReceiver();
        var filter = new MockFilter(shouldWrite: true);

        pipeline.RegisterReceiver(receiver1, filter);
        pipeline.RegisterReceiver(receiver2, filter);

        var message = CreateMockTracesRequest();

        // Act
        await pipeline.WriteAsync(message, "traces", CancellationToken.None);

        // Assert
        receiver1.WriteCallCount.Should().Be(1);
        receiver2.WriteCallCount.Should().Be(1);
    }

    [Fact]
    public async Task WriteAsync_AppliesFiltersCorrectly()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var receiver1 = new MockReceiver();
        var receiver2 = new MockReceiver();
        var passFilter = new MockFilter(shouldWrite: true);
        var blockFilter = new MockFilter(shouldWrite: false);

        pipeline.RegisterReceiver(receiver1, passFilter);
        pipeline.RegisterReceiver(receiver2, blockFilter);

        var message = CreateMockTracesRequest();

        // Act
        await pipeline.WriteAsync(message, "traces", CancellationToken.None);

        // Assert
        receiver1.WriteCallCount.Should().Be(1);
        receiver2.WriteCallCount.Should().Be(0);
    }

    [Fact]
    public async Task WriteAsync_AppliesMultipleFilters_AllMustReturnTrue()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var receiver = new MockReceiver();
        var filter1 = new MockFilter(shouldWrite: true);
        var filter2 = new MockFilter(shouldWrite: true);
        var filter3 = new MockFilter(shouldWrite: false); // This blocks

        pipeline.RegisterReceiver(receiver, filter1, filter2, filter3);

        var message = CreateMockTracesRequest();

        // Act
        await pipeline.WriteAsync(message, "traces", CancellationToken.None);

        // Assert - Blocked because filter3 returned false
        receiver.WriteCallCount.Should().Be(0);
    }

    [Fact]
    public async Task WriteAsync_MultipleFilters_AllPass_WritesItem()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var receiver = new MockReceiver();
        var filter1 = new MockFilter(shouldWrite: true);
        var filter2 = new MockFilter(shouldWrite: true);
        var filter3 = new MockFilter(shouldWrite: true);

        pipeline.RegisterReceiver(receiver, filter1, filter2, filter3);

        var message = CreateMockTracesRequest();

        // Act
        await pipeline.WriteAsync(message, "traces", CancellationToken.None);

        // Assert - All filters passed
        receiver.WriteCallCount.Should().Be(1);
    }

    [Fact]
    public void RegisterReceiver_NoFilters_UsesAllSignalsFilter()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var receiver = new MockReceiver();

        // Act - Register without filters
        pipeline.RegisterReceiver(receiver);

        // Assert - Verify via WriteAsync (should write all items)
        var message = CreateMockTracesRequest();
        pipeline.WriteAsync(message, "traces", CancellationToken.None).Wait();
        receiver.WriteCallCount.Should().Be(1);
    }

    [Fact]
    public async Task WriteAsync_DetectsErrors_UsingErrorDetectionService()
    {
        // Arrange
        var errorDetection = new Mock<IErrorDetectionService>();
        errorDetection.Setup(x => x.ContainsError(It.IsAny<object>(), "traces")).Returns(true);

        var pipeline = CreatePipeline(errorDetectionService: errorDetection.Object);
        var receiver = new MockReceiver();
        var filter = new MockFilter(shouldWrite: true);

        pipeline.RegisterReceiver(receiver, filter);

        var message = CreateMockTracesRequest();

        // Act
        await pipeline.WriteAsync(message, "traces", CancellationToken.None);

        // Assert
        receiver.ReceivedItems.Should().ContainSingle()
            .Which.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_ContinuesProcessing_WhenReceiverThrows()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var throwingReceiver = new ThrowingReceiver();
        var normalReceiver = new MockReceiver();
        var filter = new MockFilter(shouldWrite: true);

        pipeline.RegisterReceiver(throwingReceiver, filter);
        pipeline.RegisterReceiver(normalReceiver, filter);

        var message = CreateMockTracesRequest();

        // Act
        await pipeline.WriteAsync(message, "traces", CancellationToken.None);

        // Assert - second receiver still called despite first throwing
        normalReceiver.WriteCallCount.Should().Be(1);
    }

    // Helper methods and mock classes
    private TelemetryPipeline CreatePipeline(
        IProtobufJsonSerializer? serializer = null,
        IErrorDetectionService? errorDetectionService = null)
    {
        return new TelemetryPipeline(
            serializer ?? CreateMockSerializer(),
            errorDetectionService ?? CreateMockErrorDetection(),
            new MockTimeProvider(),
            NullLogger<TelemetryPipeline>.Instance);
    }

    private class MockReceiver : ITelemetryReceiver
    {
        public int WriteCallCount { get; private set; }
        public List<TelemetryItem> ReceivedItems { get; } = new();

        public Task WriteAsync(TelemetryItem item, CancellationToken ct)
        {
            WriteCallCount++;
            ReceivedItems.Add(item);
            return Task.CompletedTask;
        }
    }

    private class MockFilter : ITelemetryFilter
    {
        private readonly bool _shouldWrite;
        public MockFilter(bool shouldWrite) => _shouldWrite = shouldWrite;
        public bool ShouldWrite(TelemetryItem item) => _shouldWrite;
    }

    private class ThrowingReceiver : ITelemetryReceiver
    {
        public Task WriteAsync(TelemetryItem item, CancellationToken ct)
        {
            throw new InvalidOperationException("Receiver failed");
        }
    }
}
```

**Run Tests**: `dotnet test unit_tests/Services/TelemetryPipelineTests.cs` → ❌ Fails

**Implement**: Create `Services/TelemetryPipeline.cs`

```csharp
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services;

/// <summary>
/// Central pipeline for processing telemetry through multiple receivers with filters.
/// </summary>
public sealed class TelemetryPipeline : ITelemetryPipeline
{
    private readonly IProtobufJsonSerializer _serializer;
    private readonly IErrorDetectionService _errorDetection;
    private readonly ITimeProvider _timeProvider;
    private readonly List<(ITelemetryReceiver Receiver, ITelemetryFilter[] Filters)> _receivers;
    private readonly object _lock = new();
    private readonly ILogger<TelemetryPipeline> _logger;

    public TelemetryPipeline(
        IProtobufJsonSerializer serializer,
        IErrorDetectionService errorDetection,
        ITimeProvider timeProvider,
        ILogger<TelemetryPipeline> logger)
    {
        _serializer = serializer;
        _errorDetection = errorDetection;
        _timeProvider = timeProvider;
        _receivers = new List<(ITelemetryReceiver, ITelemetryFilter[])>();
        _logger = logger;
    }

    public void RegisterReceiver(ITelemetryReceiver receiver, params ITelemetryFilter[] filters)
    {
        ArgumentNullException.ThrowIfNull(receiver);

        // Default to AllSignalsFilter if no filters provided
        var filterArray = filters.Length > 0 ? filters : new[] { new AllSignalsFilter() };

        lock (_lock)
        {
            _receivers.Add((receiver, filterArray));
        }
    }

    public async Task WriteAsync<T>(T message, string signal, CancellationToken cancellationToken)
    {
        // Serialize
        var json = _serializer.Serialize(message);
        var ndjsonLine = json + "\n";

        // Detect errors
        var isError = _errorDetection.ContainsError(message, signal);

        // Create item
        var item = new TelemetryItem(signal, ndjsonLine, isError, _timeProvider.GetUtcNow());

        // Get receivers snapshot
        List<(ITelemetryReceiver Receiver, ITelemetryFilter[] Filters)> receivers;
        lock (_lock)
        {
            receivers = new List<(ITelemetryReceiver, ITelemetryFilter[])>(_receivers);
        }

        // Process receivers
        foreach (var (receiver, filters) in receivers)
        {
            try
            {
                // ALL filters must return true for item to be written
                var shouldWrite = filters.All(filter => filter.ShouldWrite(item));

                if (shouldWrite)
                {
                    await receiver.WriteAsync(item, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Receiver {ReceiverType} failed to process {Signal} telemetry",
                    receiver.GetType().Name, signal);
                // Continue processing other receivers
            }
        }
    }
}
```

**Run Tests**: `dotnet test unit_tests/Services/TelemetryPipelineTests.cs` → ✅ Passes

---

### Step 5: Update Dependency Injection

**Modify**: `Hosting/WebApplicationHost.cs`

**Changes**:
1. Remove `TelemetryFileWriter` registration
2. Add pipeline and receiver registrations
3. Configure pipeline with two FileReceivers

```csharp
private void ConfigureServices(WebApplicationBuilder builder, ServerOptions options)
{
    // ... existing service registrations ...

    // Remove this:
    // builder.Services.AddSingleton<TelemetryFileWriter>();

    // Add pipeline components
    builder.Services.AddSingleton<ITelemetryPipeline>(sp =>
    {
        var pipeline = new TelemetryPipeline(
            sp.GetRequiredService<IProtobufJsonSerializer>(),
            sp.GetRequiredService<IErrorDetectionService>(),
            sp.GetRequiredService<ITimeProvider>(),
            sp.GetRequiredService<ILogger<TelemetryPipeline>>());

        // Create filters
        var allSignalsFilter = new AllSignalsFilter();
        var errorsOnlyFilter = new ErrorsOnlyFilter();

        // Normal files: all signals → .ndjson
        var normalFileReceiver = new FileReceiver(
            sp.GetRequiredService<IFileRotationService>(),
            sp.GetRequiredService<IDiskSpaceChecker>(),
            options.OutputDirectory,
            ".ndjson",
            sp.GetRequiredService<ILogger<FileReceiver>>());
        pipeline.RegisterReceiver(normalFileReceiver, allSignalsFilter); // Single filter

        // Error files: errors only → .errors.ndjson
        var errorFileReceiver = new FileReceiver(
            sp.GetRequiredService<IFileRotationService>(),
            sp.GetRequiredService<IDiskSpaceChecker>(),
            options.OutputDirectory,
            ".errors.ndjson",
            sp.GetRequiredService<ILogger<FileReceiver>>());
        pipeline.RegisterReceiver(errorFileReceiver, errorsOnlyFilter); // Single filter

        // Future example: Multiple filters (errors from traces only)
        // pipeline.RegisterReceiver(someReceiver, errorsOnlyFilter, new SignalTypeFilter("traces"));

        return pipeline;
    });
}
```

---

### Step 6: Update OTLP Endpoints

**Modify**: Endpoint handlers (likely in `Program.cs` or `WebApplicationHost.cs`)

**Find current usage**:
```csharp
// Old
var telemetryFileWriter = app.Services.GetRequiredService<TelemetryFileWriter>();
app.MapPost("/v1/traces", async (HttpContext context) =>
{
    var request = await ParseRequest<ExportTraceServiceRequest>(context);
    await telemetryFileWriter.WriteAsync(request, "traces", context.RequestAborted);
});
```

**Replace with**:
```csharp
// New
var telemetryPipeline = app.Services.GetRequiredService<ITelemetryPipeline>();
app.MapPost("/v1/traces", async (HttpContext context) =>
{
    var request = await ParseRequest<ExportTraceServiceRequest>(context);
    await telemetryPipeline.WriteAsync(request, "traces", context.RequestAborted);
});
```

**Apply to all three endpoints**: `/v1/traces`, `/v1/logs`, `/v1/metrics`

---

### Step 7: Update Existing Tests

**Identify tests that reference `TelemetryFileWriter`**:
```bash
grep -r "TelemetryFileWriter" unit_tests/ --include="*.cs"
grep -r "TelemetryFileWriter" e2e_tests/ --include="*.cs"
```

**Expected findings**:
- Unit tests that directly test `TelemetryFileWriter` functionality
- Unit tests that mock `TelemetryFileWriter` as a dependency
- Possibly service integration tests

**Update Strategy**:

1. **Tests that directly test TelemetryFileWriter**:
   - **DELETE** these tests (functionality now covered by `TelemetryPipelineTests` + `FileReceiverTests`)
   - Example: `unit_tests/Services/TelemetryFileWriterTests.cs` → DELETE

2. **Tests that mock TelemetryFileWriter**:
   - **UPDATE** to mock `ITelemetryPipeline` instead
   - Replace `Mock<TelemetryFileWriter>` with `Mock<ITelemetryPipeline>`
   - Update method calls from `WriteAsync(message, signal, ct)` to same signature (no changes needed)
   - Example:
     ```csharp
     // Before
     var mockWriter = new Mock<TelemetryFileWriter>();
     mockWriter.Setup(x => x.WriteAsync(It.IsAny<object>(), "traces", It.IsAny<CancellationToken>()));

     // After
     var mockPipeline = new Mock<ITelemetryPipeline>();
     mockPipeline.Setup(x => x.WriteAsync(It.IsAny<object>(), "traces", It.IsAny<CancellationToken>()));
     ```

3. **Integration tests**:
   - **VERIFY** they still pass (should work as-is since interface is compatible)
   - Update any direct service instantiation to use pipeline

**Specific Files to Update**:

Run grep to identify, then update each file:
```bash
# Find all test files referencing TelemetryFileWriter
grep -l "TelemetryFileWriter" unit_tests/**/*.cs e2e_tests/**/*.cs

# Expected files (examples):
# - unit_tests/Services/TelemetryFileWriterTests.cs (DELETE)
# - unit_tests/Endpoints/OtlpEndpointTests.cs (UPDATE mocks)
# - Other files as discovered
```

**Verification**:
```bash
# After updates, verify no references remain
grep -r "TelemetryFileWriter" unit_tests/ e2e_tests/ --include="*.cs"
# Should return: no matches
```

---

### Step 8: Delete Old Service

**Delete**: `Services/TelemetryFileWriter.cs`

**Verify no references remain in production code**:
```bash
grep -r "TelemetryFileWriter" opentelwatcher/ --include="*.cs"
# Should return: no matches
```

---

### Step 9: Run Full Test Suite

**Unit Tests**:
```bash
dotnet test unit_tests --verbosity normal
```

**Expected**:
- ✅ All new tests pass (filters, receivers, pipeline)
- ✅ All existing tests pass (updated mocks work correctly)
- ✅ No test failures or regressions
- ✅ Code coverage maintained or improved

**E2E Tests**:
```bash
dotnet test e2e_tests --verbosity normal
```

**Expected**:
- ✅ All existing E2E tests pass (behavior unchanged despite refactoring)
- ✅ Files still written to correct locations
- ✅ Error files still created
- ✅ No behavioral changes detected

**If tests fail**:
1. Check for missed references to `TelemetryFileWriter`
2. Verify DI configuration is correct
3. Ensure endpoint handlers updated to use pipeline
4. Review test mocks for correct interface usage

**Manual Verification**:
```bash
# Start server
dotnet run --project opentelwatcher -- start --port 4318

# Send test telemetry (use existing test helpers)
# Verify files created:
# - traces.{timestamp}.ndjson
# - logs.{timestamp}.ndjson
# - metrics.{timestamp}.ndjson
# - traces.{timestamp}.errors.ndjson (for error traces)
# - logs.{timestamp}.errors.ndjson (for error logs)

# Verify file contents identical to before refactoring
```

---

## Verification Checklist

Before considering refactoring complete:

**Code Changes**:
- [ ] All new files created (interfaces, models, filters, receivers, pipeline)
- [ ] `TelemetryFileWriter.cs` deleted
- [ ] No references to `TelemetryFileWriter` in production code
- [ ] DI configuration updated
- [ ] OTLP endpoints updated to use pipeline

**Test Changes**:
- [ ] Existing tests referencing `TelemetryFileWriter` identified
- [ ] Tests mocking `TelemetryFileWriter` updated to mock `ITelemetryPipeline`
- [ ] Tests directly testing `TelemetryFileWriter` deleted (replaced by new tests)
- [ ] No references to `TelemetryFileWriter` in test code
- [ ] All new unit tests written and passing
- [ ] All existing unit tests passing (updated mocks work)
- [ ] All E2E tests passing (no behavioral changes)

**Functionality**:
- [ ] No changes to file naming or format
- [ ] Normal files still created for all telemetry
- [ ] Error files still created for errors
- [ ] File rotation works correctly
- [ ] Disk space checking prevents writes when low
- [ ] Thread-safe under concurrent load
- [ ] Code coverage maintained or improved

**Acceptance Criteria** (MUST ALL PASS):
- [ ] `dotnet test unit_tests` - 100% pass rate
- [ ] `dotnet test e2e_tests` - 100% pass rate
- [ ] Manual smoke test confirms files created correctly

---

## Benefits of Pipeline Architecture

1. **Single Responsibility**: Each receiver does one thing
2. **Open/Closed**: Add new receivers without modifying existing code
3. **DRY Principle**: FileReceiver reused for normal and error files
4. **Testability**: Each component tested independently
5. **Extensibility**: Future receivers (webhooks, Kafka, sampling) trivial to add
6. **Composable Filters**: Multiple filters can be combined per receiver (AND logic)
7. **Maintainability**: Clear separation of concerns

### Filter Composition Examples

The design supports multiple filters per receiver using AND logic (all must pass):

```csharp
// Example 1: Errors only
pipeline.RegisterReceiver(receiver, new ErrorsOnlyFilter());

// Example 2: Multiple filters - errors from traces signal only
pipeline.RegisterReceiver(receiver,
    new ErrorsOnlyFilter(),
    new SignalTypeFilter("traces"));

// Example 3: Sampling + errors (future)
pipeline.RegisterReceiver(receiver,
    new ErrorsOnlyFilter(),
    new SamplingFilter(0.1)); // 10% sample rate

// Example 4: No filters = all signals (default)
pipeline.RegisterReceiver(receiver);
```

---

## Summary

**New Files (8)**:
- `Services/Interfaces/ITelemetryReceiver.cs`
- `Services/Interfaces/ITelemetryFilter.cs`
- `Services/Interfaces/ITelemetryPipeline.cs`
- `Models/TelemetryItem.cs`
- `Services/Filters/AllSignalsFilter.cs`
- `Services/Filters/ErrorsOnlyFilter.cs`
- `Services/Receivers/FileReceiver.cs`
- `Services/TelemetryPipeline.cs`

**Test Files (4)**:
- `unit_tests/Services/Filters/AllSignalsFilterTests.cs`
- `unit_tests/Services/Filters/ErrorsOnlyFilterTests.cs`
- `unit_tests/Services/Receivers/FileReceiverTests.cs`
- `unit_tests/Services/TelemetryPipelineTests.cs`

**Modified Files (2-4)**:
- `Hosting/WebApplicationHost.cs` (DI configuration)
- OTLP endpoint handlers (use pipeline instead of writer)
- Any test files that mock `TelemetryFileWriter` (update to mock `ITelemetryPipeline`)

**Deleted Files (1+)**:
- `Services/TelemetryFileWriter.cs`
- `unit_tests/Services/TelemetryFileWriterTests.cs` (if exists, replaced by new tests)

**Estimated Effort**: ~500 lines production code, ~450 lines test code

**Risk**: Low - Existing E2E tests provide safety net for regression detection

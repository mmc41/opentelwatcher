# OpenTelWatcher Tail Command - Implementation Plan

**Last Updated:** 2025-11-17 (Updated with latest testability improvements from commit a889ec8)

## Executive Summary

The `tail` command provides live, real-time visibility into telemetry data as it's captured by OpenTelWatcher. Similar to Unix `tail -f`, this command monitors the output directory and displays telemetry in human-readable format as files are written, enabling developers to see what their applications are sending without manually inspecting NDJSON files.

**Status:** Deferred from Quick Wins (simplyplan.md) - Advanced feature for future release

**Effort Estimate:** 6-8 hours (Moderate complexity)

**Priority:** Low (Nice-to-have for development workflows)

---

## Motivation

### Problem Statement

**Current workflow for real-time telemetry visibility:**
```bash
# Manual, tedious approach
while true; do
  clear
  ls -lh ./telemetry-data/*.ndjson
  tail -1 ./telemetry-data/traces.*.ndjson | jq .
  sleep 1
done
```

**Pain points:**
1. No built-in way to see telemetry arriving in real-time
2. Developers must repeatedly check files manually
3. Raw NDJSON is hard to read without processing
4. "Is my app sending telemetry?" requires guesswork and file checking
5. Debugging telemetry issues is slow (check files, parse JSON, repeat)

### Use Cases

**1. Development Workflow**
```bash
# Terminal 1: Run application
npm run dev

# Terminal 2: Watch telemetry live
opentelwatcher tail --signal traces
# See spans appear as application executes requests
```

**2. Debugging Telemetry Configuration**
```bash
# Quick verification: Is telemetry being sent?
opentelwatcher tail
# If nothing appears after triggering app operations → telemetry misconfigured
```

**3. Error Monitoring During Testing**
```bash
# Watch for errors in real-time
opentelwatcher tail --errors-only
# Immediately see any ERROR spans or logs as they occur
```

**4. Live Metric Observation**
```bash
# Monitor specific signal type
opentelwatcher tail --signal metrics --format compact
# See metrics being recorded in real-time
```

---

## Requirements

### Functional Requirements

**FR1: File Watching**
- Monitor output directory for new and modified NDJSON files
- Detect file creation, append operations, and file rotation
- Support filtering by signal type (traces, logs, metrics)
- Support error-only mode (*.errors.ndjson files)

**FR2: Display Formats**
- **Compact** (default): One-line summary per telemetry item
- **Verbose**: Multi-line detailed view
- **JSON**: Raw NDJSON output (for piping to other tools)

**FR3: Signal-Specific Formatting**
- **Traces**: Show span name, duration, status, trace ID
- **Logs**: Show severity, message, timestamp
- **Metrics**: Show metric name, value, unit

**FR4: Options and Flags**
- `--signal <type>` - Filter by signal (traces, logs, metrics)
- `--errors-only` - Show only error telemetry
- `--format <type>` - Output format (compact, verbose, json)
- `--output-dir, -o <path>` - Directory to monitor (default: ./telemetry-data)
- `--lines <n>` - Show last N entries from existing files then continue (like `tail -n`)
- `--no-follow` - Display existing entries and exit (no live watching)

**FR5: User Experience**
- Clear indication that monitoring has started
- Graceful shutdown on Ctrl+C
- Colorized output for readability (errors in red, warnings in yellow)
- Timestamp prefix for each entry
- File rotation indicators ("==> New file: traces.*.ndjson <==")

### Non-Functional Requirements

**NFR1: Performance**
- Minimal CPU usage while idle (FileSystemWatcher event-driven)
- Handle high-throughput scenarios (100+ telemetry items/second)
- No memory leaks during extended monitoring sessions

**NFR2: Reliability**
- Handle file locking scenarios (concurrent writes)
- Gracefully handle file deletion during monitoring
- No crashes on malformed NDJSON entries (skip and warn)

**NFR3: Consistency**
- Follow existing CLI patterns (System.CommandLine 2.0)
- Reuse existing models and services where possible
- Support `--json` output for programmatic usage

---

## Architecture and Design

### Component Overview

```text
┌─────────────────────────────────────────────────────────────┐
│ TailCommand (CLI/Commands/TailCommand.cs)                   │
│ - Parse options                                             │
│ - Initialize TailService                                    │
│ - Handle Ctrl+C gracefully                                  │
└────────────────┬────────────────────────────────────────────┘
                 │
                 v
┌─────────────────────────────────────────────────────────────┐
│ ITailService (Services/Interfaces/ITailService.cs)          │
│ - FileSystemWatcher orchestration                           │
│ - File state tracking (byte offsets for each file)          │
│ - Event dispatching (OnNewEntry)                            │
└────────────────┬────────────────────────────────────────────┘
                 │
                 v
┌─────────────────────────────────────────────────────────────┐
│ TelemetryParser (Utilities/TelemetryParser.cs)              │
│ - Parse NDJSON to TelemetryEntry models                     │
│ - Extract signal type from filename                         │
│ - Handle malformed JSON gracefully                          │
└────────────────┬────────────────────────────────────────────┘
                 │
                 v
┌─────────────────────────────────────────────────────────────┐
│ TelemetryFormatter (Utilities/TelemetryFormatter.cs)        │
│ - Format traces (span name, duration, status)               │
│ - Format logs (severity, message)                           │
│ - Format metrics (name, value, unit)                        │
│ - Apply colorization and timestamps                         │
└─────────────────────────────────────────────────────────────┘
```

### Data Flow

```text
1. File Change Event
   ↓
2. TailService reads new bytes (from last offset)
   ↓
3. TelemetryParser parses NDJSON lines
   ↓
4. TelemetryFormatter formats for display
   ↓
5. Console.WriteLine (colorized output)
```

### File State Tracking

Each monitored file requires state tracking:

```csharp
// Services/TailService.cs
private class FileState
{
    public string FilePath { get; set; }
    public long LastReadPosition { get; set; }
    public DateTime LastModified { get; set; }
}

private readonly Dictionary<string, FileState> _fileStates = new();
```

### Handling File Rotation

OpenTelWatcher creates new files when rotation threshold is met:

```text
traces.20251116_143022_456.ndjson  ← Currently writing
traces.20251116_150015_789.ndjson  ← New file created (rotation)
```

TailService must:
1. Detect new file creation via FileSystemWatcher.Created event
2. Add new file to tracking state
3. Display file rotation indicator
4. Continue monitoring both files

---

## Implementation Plan (TDD Approach)

### TDD Process Overview

Follow the **Red-Green-Refactor** cycle for each component:
1. **🔴 RED**: Write a failing test first
2. **🟢 GREEN**: Write minimal code to pass the test
3. **🔵 REFACTOR**: Improve code while keeping tests green
4. **↻ REPEAT**: Continue with next test

**Development Workflow:**
```bash
# For each feature:
# 1. Write failing test
dotnet test unit_tests/<TestFile>.cs

# 2. Implement minimum code to pass
# 3. Verify test passes
dotnet test unit_tests/<TestFile>.cs

# 4. Refactor and verify still green
# 5. Move to next test
```

---

### Phase 1: TelemetryEntry Model & Parser (TDD) (2-3 hours)

**Goal:** Parse NDJSON telemetry files into structured entries

#### 🔴 RED - Step 1.1: Write Failing Test for TelemetryEntry Model

Create test **before** the model exists:

**File:** `unit_tests/Utilities/TelemetryParserTests.cs`
```csharp
public class TelemetryParserTests
{
    [Fact]
    public void ParseLine_ValidTraceJson_ReturnsTelemetryEntry()
    {
        // Arrange - Use TestBuilders for protobuf test data
        var traceRequest = TestBuilders.CreateExportTraceServiceRequest(
            spanName: "GET /api/users",
            durationNanos: 124_000_000);
        var json = TestBuilders.ToJson(traceRequest);
        var fileName = $"{TestConstants.Signals.Traces}.20251116_143022_456.ndjson";

        // Act
        var entry = TelemetryParser.ParseLine(json, fileName);

        // Assert
        entry.Should().NotBeNull();
        entry!.SignalType.Should().Be(SignalType.Traces);
        entry.SpanName.Should().Be("GET /api/users");
        entry.DurationMs.Should().Be(124);
    }
}
```

**Run test:** `dotnet test unit_tests/Utilities/TelemetryParserTests.cs` → **FAILS** (TelemetryParser doesn't exist)

#### 🟢 GREEN - Step 1.2: Create Minimal TelemetryEntry Model

Write minimum code to compile:

**File:** `opentelwatcher/Models/TelemetryEntry.cs`
```csharp
public enum SignalType { Traces, Logs, Metrics, Unknown }

public record TelemetryEntry
{
    public SignalType SignalType { get; init; }
    public DateTime Timestamp { get; init; }
    public bool IsError { get; init; }
    public string? SpanName { get; init; }
    public long? DurationMs { get; init; }
    public string? TraceId { get; init; }
    public string? SpanStatus { get; init; }
    public string? LogMessage { get; init; }
    public string? LogSeverity { get; init; }
    public string? MetricName { get; init; }
    public double? MetricValue { get; init; }
    public string? MetricUnit { get; init; }
}
```

#### 🟢 GREEN - Step 1.3: Create Minimal TelemetryParser

**File:** `opentelwatcher/Utilities/TelemetryParser.cs`
```csharp
public static class TelemetryParser
{
    public static TelemetryEntry? ParseLine(string json, string fileName)
    {
        try
        {
            var signalType = ExtractSignalType(fileName);

            return signalType switch
            {
                SignalType.Traces => ParseTraceEntry(json),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SignalType ExtractSignalType(string fileName)
    {
        if (fileName.StartsWith(TestConstants.Signals.Traces))
            return SignalType.Traces;
        return SignalType.Unknown;
    }

    private static TelemetryEntry ParseTraceEntry(string json)
    {
        var request = JsonSerializer.Deserialize<ExportTraceServiceRequest>(json);
        var span = request?.ResourceSpans[0]?.ScopeSpans[0]?.Spans[0];

        return new TelemetryEntry
        {
            SignalType = SignalType.Traces,
            SpanName = span?.Name,
            DurationMs = (long?)(span?.EndTimeUnixNano - span?.StartTimeUnixNano) / 1_000_000,
            Timestamp = DateTime.UtcNow
        };
    }
}
```

**Run test:** `dotnet test unit_tests/Utilities/TelemetryParserTests.cs` → **PASSES**

#### 🔴 RED - Step 1.4: Add Test for Malformed JSON

```csharp
[Fact]
public void ParseLine_MalformedJson_ReturnsNull()
{
    // Arrange
    var json = "{invalid json}";
    var fileName = $"{TestConstants.Signals.Traces}.20251116_143022_456.ndjson";

    // Act
    var entry = TelemetryParser.ParseLine(json, fileName);

    // Assert
    entry.Should().BeNull();
}
```

**Run test:** → **PASSES** (already handles JsonException)

#### 🔴 RED - Step 1.5: Add Test for Log Parsing

```csharp
[Fact]
public void ParseLine_ValidLogJson_ReturnsTelemetryEntry()
{
    // Arrange
    var logRequest = TestBuilders.CreateExportLogsServiceRequest(
        message: "User logged in",
        severity: "INFO");
    var json = TestBuilders.ToJson(logRequest);
    var fileName = $"{TestConstants.Signals.Logs}.20251116_143022_456.ndjson";

    // Act
    var entry = TelemetryParser.ParseLine(json, fileName);

    // Assert
    entry.Should().NotBeNull();
    entry!.SignalType.Should().Be(SignalType.Logs);
    entry.LogMessage.Should().Be("User logged in");
    entry.LogSeverity.Should().Be("INFO");
}
```

**Run test:** → **FAILS** (ParseLogEntry not implemented)

#### 🟢 GREEN - Step 1.6: Implement Log Parsing

Add to `TelemetryParser.cs`:
```csharp
private static SignalType ExtractSignalType(string fileName)
{
    if (fileName.StartsWith(TestConstants.Signals.Traces))
        return SignalType.Traces;
    if (fileName.StartsWith(TestConstants.Signals.Logs))
        return SignalType.Logs;
    return SignalType.Unknown;
}

private static TelemetryEntry ParseLogEntry(string json)
{
    var request = JsonSerializer.Deserialize<ExportLogsServiceRequest>(json);
    var log = request?.ResourceLogs[0]?.ScopeLogs[0]?.LogRecords[0];

    return new TelemetryEntry
    {
        SignalType = SignalType.Logs,
        LogMessage = log?.Body?.StringValue,
        LogSeverity = log?.SeverityText,
        Timestamp = DateTime.UtcNow
    };
}
```

**Run test:** → **PASSES**

#### 🔵 REFACTOR - Step 1.7: Extract Common Code

Refactor parser to reduce duplication while keeping tests green.

#### ↻ REPEAT - Step 1.8-1.10: Add Metrics Support

Follow same Red-Green-Refactor cycle for metrics parsing.

### Phase 2: TelemetryFormatter (TDD) (1-2 hours)

**Goal:** Format telemetry entries for console display

#### 🔴 RED - Step 2.1: Write Failing Test for Compact Format

**File:** `unit_tests/Utilities/TelemetryFormatterTests.cs`
```csharp
public class TelemetryFormatterTests
{
    [Fact]
    public void Format_Compact_TraceEntry_ReturnsExpectedFormat()
    {
        // Arrange
        var testTime = new DateTime(2025, 11, 16, 14, 30, 22, 456, DateTimeKind.Utc);
        var entry = new TelemetryEntry
        {
            SignalType = SignalType.Traces,
            Timestamp = testTime,
            SpanName = "GET /api/users",
            DurationMs = 124,
            SpanStatus = "OK",
            IsError = false
        };

        // Act
        var result = TelemetryFormatter.Format(entry, TailFormat.Compact);

        // Assert
        result.Should().Contain("14:30:22.456");
        result.Should().Contain("TRACE");
        result.Should().Contain("GET /api/users");
        result.Should().Contain("124ms");
        result.Should().Contain("OK");
    }
}
```

**Run test:** → **FAILS** (TelemetryFormatter doesn't exist)

#### 🟢 GREEN - Step 2.2: Create Minimal TelemetryFormatter

```csharp
public static class TelemetryFormatter
{
    public static string Format(TelemetryEntry entry, TailFormat format)
    {
        return format switch
        {
            TailFormat.Compact => FormatCompact(entry),
            _ => throw new NotImplementedException()
        };
    }

    private static string FormatCompact(TelemetryEntry entry)
    {
        var timestamp = entry.Timestamp.ToString("HH:mm:ss.fff");
        var prefix = entry.SignalType.ToString().ToUpper();

        return entry.SignalType switch
        {
            SignalType.Traces => $"{timestamp} - {prefix}: {entry.SpanName} ({entry.DurationMs}ms, {entry.SpanStatus})",
            _ => $"{timestamp} - UNKNOWN"
        };
    }
}
```

**Run test:** → **PASSES**

#### 🔴 RED - Step 2.3: Add Test for Error Colorization

```csharp
[Fact]
public void Format_Compact_ErrorEntry_UsesRedColor()
{
    // Arrange
    var entry = new TelemetryEntry
    {
        SignalType = SignalType.Traces,
        Timestamp = DateTime.UtcNow,
        SpanName = "GET /api/error",
        IsError = true
    };

    // Act
    var result = TelemetryFormatter.Format(entry, TailFormat.Compact);

    // Assert
    result.Should().Contain("\u001b[31m"); // ANSI red color
    result.Should().Contain("ERROR");
}
```

**Run test:** → **FAILS**

#### 🟢 GREEN - Step 2.4: Add Error Colorization

Update `FormatCompact`:
```csharp
private static string FormatCompact(TelemetryEntry entry)
{
    var timestamp = entry.Timestamp.ToString("HH:mm:ss.fff");
    var prefix = entry.IsError ? "ERROR" : entry.SignalType.ToString().ToUpper();
    var color = entry.IsError ? "\u001b[31m" : "\u001b[0m";
    var reset = "\u001b[0m";

    return entry.SignalType switch
    {
        SignalType.Traces => $"{timestamp} - {color}{prefix}{reset}: {entry.SpanName} ({entry.DurationMs}ms, {entry.SpanStatus})",
        _ => $"{timestamp} - UNKNOWN"
    };
}
```

**Run test:** → **PASSES**

#### ↻ REPEAT - Step 2.5-2.8: Add Verbose and JSON Formats

Follow same Red-Green-Refactor cycle for verbose and JSON formats.

### Phase 3: TailService & CLI Integration (TDD) (2-3 hours)

**Goal:** Wire up file watching and CLI command

#### 🔴 RED - Step 3.1: Write Failing Test for TailCommand

**File:** `unit_tests/CLI/Commands/TailCommandTests.cs`
```csharp
public class TailCommandTests : FileBasedTestBase
{
    [Fact]
    public async Task ExecuteAsync_CallsTailServiceWithCorrectOptions()
    {
        using var _ = new SlowTestDetector();

        // Arrange
        var mockTailService = new Mock<ITailService>();
        var command = new TailCommand(mockTailService.Object);
        var options = new TailOptions
        {
            Signal = TestConstants.Signals.Traces,
            ErrorsOnly = true,
            Format = TailFormat.Compact,
            OutputDir = TestOutputDir
        };

        // Act
        var result = await command.ExecuteAsync(options);

        // Assert
        mockTailService.Verify(s => s.MonitorAsync(
            It.Is<TailOptions>(o =>
                o.Signal == TestConstants.Signals.Traces &&
                o.ErrorsOnly),
            It.IsAny<Action<TelemetryEntry>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

**Run test:** → **FAILS** (TailCommand doesn't exist)

#### 🟢 GREEN - Step 3.2: Create Minimal TailCommand

```csharp
public class TailCommand
{
    private readonly ITailService _tailService;

    public TailCommand(ITailService tailService)
    {
        _tailService = tailService;
    }

    public async Task<CommandResult> ExecuteAsync(TailOptions options)
    {
        await _tailService.MonitorAsync(
            options,
            entry => { /* Callback - implemented later */ },
            CancellationToken.None);

        return new CommandResult(0, "Monitoring stopped");
    }
}
```

**Run test:** → **PASSES**

#### 🔴 RED - Step 3.3: Write Test for TailService Interface

**File:** `unit_tests/Services/TailServiceTests.cs`
```csharp
public class TailServiceTests : FileBasedTestBase
{
    [Fact]
    public async Task MonitorAsync_WhenFileCreated_InvokesCallback()
    {
        using var _ = new SlowTestDetector();

        // Arrange
        var mockTimeProvider = new MockTimeProvider();
        var logger = TestLoggerFactory.CreateLogger<TailService>();
        var service = new TailService(mockTimeProvider, logger);

        var callbackInvoked = false;
        var options = new TailOptions { OutputDir = TestOutputDir };

        // Act
        var monitorTask = service.MonitorAsync(
            options,
            entry => { callbackInvoked = true; },
            CancellationToken.None);

        // Create test file
        var testFile = Path.Combine(TestOutputDir, $"{TestConstants.Signals.Traces}.ndjson");
        await File.WriteAllTextAsync(testFile, "test content");

        await Task.Delay(500); // Allow file watcher to detect

        // Assert
        callbackInvoked.Should().BeTrue();
    }
}
```

**Run test:** → **FAILS** (TailService doesn't exist)

#### 🟢 GREEN - Step 3.4: Create TailService Interface and Stub

**File:** `opentelwatcher/Services/Interfaces/ITailService.cs`
```csharp
public interface ITailService
{
    Task MonitorAsync(
        TailOptions options,
        Action<TelemetryEntry> onEntry,
        CancellationToken cancellationToken);
}
```

**File:** `opentelwatcher/Services/TailService.cs`
```csharp
public class TailService : ITailService
{
    private readonly ITimeProvider _timeProvider;
    private readonly ILogger<TailService> _logger;

    public TailService(ITimeProvider timeProvider, ILogger<TailService> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task MonitorAsync(
        TailOptions options,
        Action<TelemetryEntry> onEntry,
        CancellationToken cancellationToken)
    {
        // Minimal implementation - use FileSystemWatcher
        var watcher = new FileSystemWatcher(options.OutputDir);
        watcher.Created += (s, e) => onEntry(new TelemetryEntry());
        watcher.EnableRaisingEvents = true;

        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
```

**Run test:** → **PASSES** (basic implementation works)

#### 🔵 REFACTOR - Step 3.5-3.8: Enhance TailService

- Add file state tracking
- Implement file reading from last position
- Add NDJSON parsing integration
- Handle file rotation

#### ↻ REPEAT - Step 3.9: Register CLI Command

Add command registration to CliApplication (no test needed for System.CommandLine wiring)

### Phase 4: E2E Testing & Polish (1-2 hours)

**Goal:** Validate end-to-end behavior with real file system operations

#### Step 4.1: Write E2E Test for File Watching

**File:** `e2e_tests/CLI/TailCommandTests.cs`
```csharp
public class TailCommandTests : FileBasedTestBase
{
    [Fact]
    public async Task TailCommand_WatchesNewFiles_DisplaysEntries()
    {
        using var _ = new SlowTestDetector();

        // Arrange: Start tail in background using TestHelpers
        var arguments = $"tail --output-dir {TestOutputDir} --format compact";
        var commandResult = TestHelpers.StartCommandInBackground(
            "opentelwatcher",
            arguments);

        // Wait for monitoring to start using PollingHelpers
        await PollingHelpers.WaitForConditionAsync(
            () => commandResult.StandardOutput.Length > 0,
            timeout: E2EConstants.Timeouts.CommandStart,
            interval: E2EConstants.PollingIntervals.Fast);

        // Act: Create telemetry file with realistic OTLP data
        var traceRequest = ProtobufBuilders.CreateTraceRequest(
            spanName: "GET /api/users",
            durationNanos: 124_000_000);
        var json = ProtobufBuilders.ToJson(traceRequest);

        var traceFile = Path.Combine(TestOutputDir, "traces.20251116_143022_456.ndjson");
        await File.WriteAllTextAsync(traceFile, json + "\n");

        // Wait for file to be processed using PollingHelpers
        await PollingHelpers.WaitForConditionAsync(
            () => commandResult.StandardOutput.ToString().Contains("TRACE"),
            timeout: E2EConstants.Timeouts.FileProcessing,
            interval: E2EConstants.PollingIntervals.Medium);

        // Assert: Check output contains formatted entry
        var output = commandResult.StandardOutput.ToString();
        output.Should().Contain("TRACE:");
        output.Should().Contain("GET /api/users");
        output.Should().Contain("124ms");

        // Cleanup
        commandResult.Process.Kill();
        await commandResult.Process.WaitForExitAsync();
    }

    [Fact]
    public async Task TailCommand_WithErrorsOnly_FiltersCorrectly()
    {
        // Test --errors-only flag filters out non-error entries
    }

    [Fact]
    public async Task TailCommand_WithSignalFilter_ShowsOnlyMatchingSignal()
    {
        // Test --signal flag filters by signal type
    }
}
```

#### Step 4.2: Manual Testing & Polish

- Test with real OpenTelemetry applications
- Verify colorization in terminal
- Test Ctrl+C graceful shutdown
- Test file rotation scenarios
- Performance test with high throughput

#### Step 4.3: CLI Registration

**File:** `opentelwatcher/CLI/CliApplication.cs`
```csharp
var tailCommand = new Command("tail", "Monitor telemetry files in real-time");

var signalOption = new Option<string?>(
    "--signal",
    "Filter by signal type (traces, logs, metrics)");
tailCommand.AddOption(signalOption);

var errorsOnlyOption = new Option<bool>(
    "--errors-only",
    "Show only error telemetry");
tailCommand.AddOption(errorsOnlyOption);

var formatOption = new Option<TailFormat>(
    "--format",
    getDefaultValue: () => TailFormat.Compact,
    "Output format (compact, verbose, json)");
tailCommand.AddOption(formatOption);

var outputDirOption = new Option<string>(
    aliases: new[] { "--output-dir", "-o" },
    getDefaultValue: () => "./telemetry-data",
    "Directory to monitor");
tailCommand.AddOption(outputDirOption);

var linesOption = new Option<int?>(
    "--lines",
    "Show last N entries from existing files before monitoring");
tailCommand.AddOption(linesOption);

var noFollowOption = new Option<bool>(
    "--no-follow",
    "Display existing entries and exit (do not monitor)");
tailCommand.AddOption(noFollowOption);

tailCommand.SetHandler(async (signal, errorsOnly, format, outputDir, lines, noFollow) =>
{
    var options = new TailOptions
    {
        Signal = signal,
        ErrorsOnly = errorsOnly,
        Format = format,
        OutputDir = outputDir,
        Lines = lines,
        NoFollow = noFollow
    };

    var tailService = serviceProvider.GetRequiredService<ITailService>();
    var command = new TailCommand(tailService);
    var result = await command.ExecuteAsync(options);
    Environment.ExitCode = result.ExitCode;
},
signalOption, errorsOnlyOption, formatOption, outputDirOption, linesOption, noFollowOption);

rootCommand.AddCommand(tailCommand);
```

---

## Testing Strategy

### Testability Improvements (Latest Commit)

The tail command implementation will leverage the latest testability enhancements:

**Test Infrastructure:**
- **FileBasedTestBase** - Automatic temp directory creation and cleanup
- **TestBuilders** - Factory methods for Options, Protobuf messages, and test data
- **TestConstants** - Centralized constants (ports, PIDs, file sizes, signals, timeouts)
- **SlowTestDetector** - Automatic detection of slow-running tests (>2s warning, >5s error)

**Service Abstractions:**
- **ITimeProvider** - Testable timestamps (use instead of DateTime.UtcNow)
- **IProcessProvider** - Testable process operations (if needed)
- **IEnvironment** - Testable environment variables (if needed)

**E2E Helpers:**
- **E2EConstants** - Standardized timeouts, polling intervals, retry counts
- **PollingHelpers** - Polling utilities with configurable timeouts
- **ProtobufBuilders** - Realistic OTLP telemetry data generation
- **PortAllocator** - Automatic port allocation for concurrent tests

### Unit Tests (Fast, Isolated)

1. **TelemetryParser**: Parse valid/invalid NDJSON for all signal types (use ProtobufBuilders)
2. **TelemetryFormatter**: Verify all format modes (compact, verbose, json)
3. **TailCommand**: Verify options passed to TailService correctly (use TestConstants)
4. **TailService (mocked dependencies)**: Verify file state tracking logic (use ITimeProvider mock)

### E2E Tests (Real Scenarios)

1. **File Watching**: Create new file with ProtobufBuilders, verify displayed (use PollingHelpers)
2. **File Append**: Append to existing file, verify new entries displayed (use PollingHelpers)
3. **File Rotation**: Create new file during monitoring, verify both monitored
4. **Error Filtering**: Create error file with ProtobufBuilders, verify --errors-only filters correctly
5. **Signal Filtering**: Create mixed signal files with ProtobufBuilders, verify --signal filters correctly
6. **Graceful Shutdown**: Send Ctrl+C using TestHelpers, verify clean exit
7. **Performance**: High throughput test (100+ items/second) with SlowTestDetector

### Manual Testing Checklist

- [ ] Start watcher, trigger app operations, verify telemetry appears
- [ ] Test with high throughput (100+ items/second)
- [ ] Test with all format modes (compact, verbose, json)
- [ ] Test with file rotation scenarios
- [ ] Test --lines option shows historical entries
- [ ] Test --no-follow exits after displaying existing entries
- [ ] Test error colorization in terminal
- [ ] Test Ctrl+C graceful shutdown

---

## Documentation Updates

### README.md

Add tail command to CLI examples section:

```markdown
### Real-Time Monitoring

Watch telemetry as it arrives:
```bash
# Watch all telemetry
opentelwatcher tail

# Watch only traces
opentelwatcher tail --signal traces

# Watch for errors only
opentelwatcher tail --errors-only

# Verbose output
opentelwatcher tail --format verbose

# Show last 10 entries then continue monitoring
opentelwatcher tail --lines 10
```

### CLAUDE.md

Update CLI commands section with tail command:

```markdown
# Monitor telemetry files in real-time
dotnet run --project opentelwatcher -- tail

# Filter by signal type
dotnet run --project opentelwatcher -- tail --signal traces

# Show only errors
dotnet run --project opentelwatcher -- tail --errors-only

# JSON output for piping
dotnet run --project opentelwatcher -- tail --format json | jq .
```

### watcheruse.md

Add new usage scenario:

```markdown
## Scenario 7: Real-Time Telemetry Monitoring During Development

**Goal:** Watch telemetry appear live as application executes.

**Workflow:**
1. Start watcher in daemon mode
2. In separate terminal, start tail monitoring
3. Run application and trigger operations
4. See spans/logs/metrics appear immediately

**Commands:**
```bash
# Terminal 1: Start watcher
opentelwatcher start --daemon

# Terminal 2: Watch live telemetry
opentelwatcher tail --signal traces

# Terminal 3: Run application
npm run dev
```

---

## Success Metrics

### Before Tail Command
```bash
# Manual, tedious
while true; do
  ls -lh ./telemetry-data/*.ndjson
  tail -1 ./telemetry-data/traces.*.ndjson | jq .resourceSpans[0].scopeSpans[0].spans[0].name
  sleep 1
done
```

### After Tail Command
```bash
# Clean, built-in
opentelwatcher tail --signal traces
# Output:
# 14:30:22.456 - TRACE: GET /api/users (124ms, OK)
# 14:30:22.789 - TRACE: POST /api/orders (56ms, OK)
# 14:30:23.123 - ERROR: Query failed (status=ERROR, message="Connection timeout")
```

### Impact Measurements
- **Developer workflow:** Manual file checking → Real-time visibility
- **Debugging speed:** Minutes → Seconds (immediate feedback)
- **Error detection:** Post-mortem file inspection → Live error stream
- **User experience:** External tools (jq, tail -f) → Native command

---

## Future Enhancements (Post-MVP)

### 1. Advanced Filtering
```bash
# Filter by attribute
opentelwatcher tail --filter "http.status_code >= 500"

# Filter by trace ID
opentelwatcher tail --trace-id 5b8efff798038103d269b633813fc60c
```

### 2. Performance Sampling
```bash
# Only show slow traces (>1s)
opentelwatcher tail --signal traces --min-duration 1000
```

### 3. Export to File
```bash
# Tail to stdout AND save to file
opentelwatcher tail --tee captured.ndjson
```

### 4. Remote Monitoring
```bash
# Monitor remote instance (via API)
opentelwatcher tail --remote http://remote-host:4318
```

---

## Dependencies and Risks

### Dependencies
- **FileSystemWatcher**: .NET built-in (reliable, well-tested)
- **Existing parsers**: Reuse OTLP deserialization from TelemetryCollectionService
- **System.CommandLine**: Already used in CLI

### Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| High CPU usage during idle | Low | Medium | Event-driven FileSystemWatcher (not polling) |
| File locking conflicts | Medium | Low | Retry logic with backoff, read-only file access |
| Memory leak during long sessions | Low | High | Dispose FileSystemWatcher properly, limit buffering |
| Malformed JSON crashes tool | Medium | Medium | Try/catch with skip and warn strategy |
| File rotation detection failure | Low | Medium | Test with actual rotation scenarios |

---

## Conclusion

The `tail` command fills a significant gap in the developer experience by providing **real-time visibility** into telemetry capture. While deferred as a "nice-to-have" in the Quick Wins roadmap, it represents a high-value feature for development and debugging workflows.

**Recommended Timeline:**
- Phase 1 (Core Infrastructure): 3-4 hours
- Phase 2 (Formatting and Display): 2-3 hours
- Phase 3 (Testing): 1-2 hours
- **Total: 6-9 hours**

**Implementation Priority:** After completion of Quick Wins #1-8 (simplyplan.md), implement as first "enhancement" feature based on user demand.

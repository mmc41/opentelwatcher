# Tails Feature Implementation Plan

## Overview

Add `--tails` and `--tails-filter-errors-only` options to the `start` command to output live telemetry to stdout with colorized, timestamped formatting.

**Prerequisites**: Refactoring plan (`refac.md`) must be completed and all tests passing.

**Note on SignalType**: The codebase uses `SignalType` enum (from `Configuration/SignalType.cs`) instead of strings:
- `SignalType.Traces`, `SignalType.Logs`, `SignalType.Metrics`, `SignalType.Unspecified`
- Extension methods: `ToLowerString()` converts to lowercase string ("traces", "logs", "metrics")
- `TelemetryItem.Signal` is `SignalType` enum, not string

## Feature Requirements

### Command-Line Options

1. **`--tails`**: Enable live telemetry output to stdout
   - Outputs NDJSON format (always)
   - Includes timestamp prefix: `[2025-01-19T12:00:00.123] [traces] {...}`
   - Color-coded by SignalType and error status
   - Writes to both stdout AND files (dual output)
   - NOT compatible with `--daemon` mode

2. **`--tails-filter-errors-only`**: Reduce output to errors only
   - Requires `--tails` to be enabled
   - Uses same `ErrorsOnlyFilter` as error files
   - Only outputs items where `IsError == true`

### UX Requirements

- **Startup Message**: Clear indication monitoring has started
- **Graceful Shutdown**: Ctrl+C stops both tailing and server
- **Colorization**:
  - Errors: Red (`\x1b[31m`)
  - SignalType.Traces (non-error): Cyan (`\x1b[36m`)
  - SignalType.Logs (non-error): White (`\x1b[37m`)
  - SignalType.Metrics (non-error): Green (`\x1b[32m`)
- **Timestamp**: ISO 8601 format with milliseconds (`yyyy-MM-ddTHH:mm:ss.fff`)
- **Format**: `[timestamp] [signal_lowercase] {ndjson}` (e.g., `[2025-01-19T12:00:00.123] [traces] {...}`)

## Architecture

Uses the pipeline architecture from refactoring:

```
OTLP Request → TelemetryPipeline
                   ↓
         Create TelemetryItem
                   ↓
   Registered Receivers:
   1. FileReceiver (.ndjson) + AllSignalsFilter
   2. FileReceiver (.errors.ndjson) + ErrorsOnlyFilter
   3. StdoutReceiver + (AllSignalsFilter OR ErrorsOnlyFilter)  ← NEW
                   ↓
   [Colorized stdout output]
```

## Implementation Steps (TDD)

### Step 1: Implement StdoutReceiver (TDD)

#### Step 1.1: Write Tests First

**Create**: `unit_tests/Services/Receivers/StdoutReceiverTests.cs`

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Receivers;
using Xunit;

namespace OpenTelWatcher.Tests.Services.Receivers;

public class StdoutReceiverTests
{
    [Fact]
    public async Task WriteAsync_FormatsOutput_WithTimestampAndSignal()
    {
        // Arrange
        var receiver = new StdoutReceiver(NullLogger<StdoutReceiver>.Instance);
        var timestamp = new DateTimeOffset(2025, 1, 19, 12, 30, 45, 123, TimeSpan.Zero);
        var item = new TelemetryItem(
            SignalType.Traces,
            "{\"traceId\":\"abc123\"}\n",
            false,
            timestamp);

        var output = CaptureConsoleOutput(() =>
        {
            receiver.WriteAsync(item, CancellationToken.None).Wait();
        });

        // Assert
        output.Should().Contain("[2025-01-19T12:30:45.123]");
        output.Should().Contain("[traces]");
        output.Should().Contain("{\"traceId\":\"abc123\"}");
    }

    [Theory]
    [InlineData(SignalType.Traces, false, "\x1b[36m")] // Cyan
    [InlineData(SignalType.Logs, false, "\x1b[37m")]   // White
    [InlineData(SignalType.Metrics, false, "\x1b[32m")] // Green
    public async Task WriteAsync_ColorizesOutput_BySignalType(
        SignalType signal,
        bool isError,
        string expectedColor)
    {
        // Arrange
        var receiver = new StdoutReceiver(NullLogger<StdoutReceiver>.Instance);
        var item = new TelemetryItem(signal, "{}\n", isError, DateTimeOffset.UtcNow);

        var output = CaptureConsoleOutput(() =>
        {
            receiver.WriteAsync(item, CancellationToken.None).Wait();
        });

        // Assert
        output.Should().StartWith(expectedColor);
        output.Should().EndWith("\x1b[0m\n"); // Reset color
    }

    [Theory]
    [InlineData(SignalType.Traces)]
    [InlineData(SignalType.Logs)]
    [InlineData(SignalType.Metrics)]
    public async Task WriteAsync_ColorizesErrors_InRed(SignalType signal)
    {
        // Arrange
        var receiver = new StdoutReceiver(NullLogger<StdoutReceiver>.Instance);
        var item = new TelemetryItem(signal, "{}\n", IsError: true, DateTimeOffset.UtcNow);

        var output = CaptureConsoleOutput(() =>
        {
            receiver.WriteAsync(item, CancellationToken.None).Wait();
        });

        // Assert
        output.Should().StartWith("\x1b[31m"); // Red
    }

    [Fact]
    public async Task WriteAsync_PreservesRawNdjson_NoFormatting()
    {
        // Arrange
        var receiver = new StdoutReceiver(NullLogger<StdoutReceiver>.Instance);
        var complexJson = "{\"nested\":{\"array\":[1,2,3]},\"value\":\"test\"}\n";
        var item = new TelemetryItem(SignalType.Traces, complexJson, false, DateTimeOffset.UtcNow);

        var output = CaptureConsoleOutput(() =>
        {
            receiver.WriteAsync(item, CancellationToken.None).Wait();
        });

        // Assert - JSON should be preserved exactly (not prettified)
        output.Should().Contain("{\"nested\":{\"array\":[1,2,3]},\"value\":\"test\"}");
    }

    [Fact]
    public async Task WriteAsync_ThreadSafe_ConcurrentWrites()
    {
        // Arrange
        var receiver = new StdoutReceiver(NullLogger<StdoutReceiver>.Instance);
        var items = Enumerable.Range(0, 100).Select(i => new TelemetryItem(
            SignalType.Traces,
            $"{{\"id\":{i}}}\n",
            false,
            DateTimeOffset.UtcNow)).ToList();

        // Act
        var outputs = new ConcurrentBag<string>();
        await Parallel.ForEachAsync(items, async (item, ct) =>
        {
            var output = CaptureConsoleOutput(() =>
            {
                receiver.WriteAsync(item, ct).Wait();
            });
            outputs.Add(output);
        });

        // Assert - all writes completed without garbled output
        outputs.Should().HaveCount(100);
        outputs.Should().OnlyContain(o => o.Contains("[traces]"));
    }

    [Fact]
    public async Task WriteAsync_TrimsTrailingNewline_FromNdjson()
    {
        // Arrange
        var receiver = new StdoutReceiver(NullLogger<StdoutReceiver>.Instance);
        var item = new TelemetryItem(SignalType.Traces, "{\"id\":1}\n", false, DateTimeOffset.UtcNow);

        var output = CaptureConsoleOutput(() =>
        {
            receiver.WriteAsync(item, CancellationToken.None).Wait();
        });

        // Assert - should not have double newline
        output.Should().NotContain("}\n\n");
    }

    // Helper method
    private string CaptureConsoleOutput(Action action)
    {
        var originalOut = Console.Out;
        try
        {
            using var writer = new StringWriter();
            Console.SetOut(writer);
            action();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
```

**Run Tests**: `dotnet test unit_tests/Services/Receivers/StdoutReceiverTests.cs` → ❌ Fails (class doesn't exist)

---

#### Step 1.2: Implement StdoutReceiver

**Create**: `Services/Receivers/StdoutReceiver.cs`

```csharp
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Models;
using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services.Receivers;

/// <summary>
/// Writes telemetry items to stdout with colorized, timestamped formatting.
/// </summary>
public sealed class StdoutReceiver : ITelemetryReceiver, IDisposable
{
    private readonly SemaphoreSlim _consoleLock = new(1, 1);
    private readonly ILogger<StdoutReceiver> _logger;

    public StdoutReceiver(ILogger<StdoutReceiver> logger)
    {
        _logger = logger;
    }

    public async Task WriteAsync(TelemetryItem item, CancellationToken cancellationToken)
    {
        await _consoleLock.WaitAsync(cancellationToken);
        try
        {
            var color = GetColor(item);
            var timestamp = item.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fff");
            var ndjson = item.NdjsonLine.TrimEnd('\n');
            var signalName = item.Signal.ToLowerString(); // Convert enum to lowercase string
            var output = $"{color}[{timestamp}] [{signalName}] {ndjson}\x1b[0m";

            Console.WriteLine(output);
        }
        finally
        {
            _consoleLock.Release();
        }
    }

    private static string GetColor(TelemetryItem item)
    {
        // Errors always red, regardless of signal type
        if (item.IsError)
        {
            return "\x1b[31m"; // Red
        }

        // Color by signal type (using enum)
        return item.Signal switch
        {
            SignalType.Traces => "\x1b[36m",  // Cyan
            SignalType.Logs => "\x1b[37m",    // White
            SignalType.Metrics => "\x1b[32m", // Green
            _ => "\x1b[37m"                   // Default: White (for Unspecified)
        };
    }

    public void Dispose()
    {
        _consoleLock.Dispose();
    }
}
```

**Run Tests**: `dotnet test unit_tests/Services/Receivers/StdoutReceiverTests.cs` → ✅ Passes

---

### Step 2: Update CLI Options (TDD)

#### Step 2.1: Add Options to CommandOptions Model

**Modify**: `CLI/Models/CommandModels.cs`

```csharp
public sealed record CommandOptions
{
    public int? Port { get; init; } = DefaultPorts.Otlp;
    public string OutputDirectory { get; init; } = "./telemetry-data";
    public LogLevel LogLevel { get; init; } = LogLevel.Information;
    public bool Daemon { get; init; } = false;
    public bool Silent { get; init; } = false;
    public bool Verbose { get; init; } = false;

    // NEW: Tails options
    public bool Tails { get; init; } = false;
    public bool TailsFilterErrorsOnly { get; init; } = false;
}
```

---

#### Step 2.2: Write Tests for CLI Validation

**Create**: `unit_tests/CLI/TailsOptionValidationTests.cs`

```csharp
using FluentAssertions;
using OpenTelWatcher.CLI;
using Xunit;

namespace OpenTelWatcher.Tests.CLI;

public class TailsOptionValidationTests
{
    [Fact]
    public void StartCommand_RejectsInvalidCombination_TailsWithDaemon()
    {
        // Arrange
        var cliApp = new CliApplication();
        var args = new[] { "start", "--tails", "--daemon" };

        // Act
        var result = cliApp.ParseArguments(args);

        // Assert
        result.Should().NotBe(0); // Error exit code
        // Verify error message contains appropriate text
    }

    [Fact]
    public void StartCommand_RejectsInvalidCombination_TailsFilterWithoutTails()
    {
        // Arrange
        var cliApp = new CliApplication();
        var args = new[] { "start", "--tails-filter-errors-only" };

        // Act
        var result = cliApp.ParseArguments(args);

        // Assert
        result.Should().NotBe(0); // Error exit code
    }

    [Fact]
    public void StartCommand_AcceptsValidCombination_TailsWithErrorsOnly()
    {
        // Arrange
        var cliApp = new CliApplication();
        var args = new[] { "start", "--tails", "--tails-filter-errors-only" };

        // Act
        var result = cliApp.ParseArguments(args);

        // Assert
        result.Should().Be(0); // Success (validation passes)
    }

    [Fact]
    public void StartCommand_AcceptsValidCombination_TailsAlone()
    {
        // Arrange
        var cliApp = new CliApplication();
        var args = new[] { "start", "--tails" };

        // Act
        var result = cliApp.ParseArguments(args);

        // Assert
        result.Should().Be(0); // Success
    }
}
```

**Run Tests**: `dotnet test unit_tests/CLI/TailsOptionValidationTests.cs` → ❌ Fails

---

#### Step 2.3: Implement CLI Options and Validators

**Modify**: `CLI/CliApplication.cs` - `BuildStartCommand()`

Add option definitions:

```csharp
var tailsOption = new Option<bool>("--tails")
{
    Description = "Output live telemetry to stdout in addition to files",
    DefaultValueFactory = _ => false
};

var tailsFilterErrorsOnlyOption = new Option<bool>("--tails-filter-errors-only")
{
    Description = "Only output errors when using --tails (requires --tails)",
    DefaultValueFactory = _ => false
};

startCommand.Add(tailsOption);
startCommand.Add(tailsFilterErrorsOnlyOption);
```

Add validators to existing validation block:

```csharp
startCommand.Validators.Add(result =>
{
    var daemon = result.GetValue(daemonOption);
    var tails = result.GetValue(tailsOption);
    var tailsFilterErrorsOnly = result.GetValue(tailsFilterErrorsOnlyOption);

    // Validate --tails not used with --daemon
    if (tails && daemon)
    {
        result.AddError("Cannot use --tails with --daemon. Tails mode requires foreground operation.");
    }

    // Validate --tails-filter-errors-only requires --tails
    if (tailsFilterErrorsOnly && !tails)
    {
        result.AddError("Cannot use --tails-filter-errors-only without --tails.");
    }
});
```

Update command handler to extract values:

```csharp
startCommand.SetAction(parseResult =>
{
    var options = new CommandOptions
    {
        Port = parseResult.GetValue(portOption),
        OutputDirectory = parseResult.GetValue(outputDirOption),
        LogLevel = parseResult.GetValue(logLevelOption),
        Daemon = parseResult.GetValue(daemonOption),
        Silent = parseResult.GetValue(silentOption),
        Verbose = parseResult.GetValue(verboseOption),
        Tails = parseResult.GetValue(tailsOption),
        TailsFilterErrorsOnly = parseResult.GetValue(tailsFilterErrorsOnlyOption)
    };

    // ... execute StartCommand
});
```

**Run Tests**: `dotnet test unit_tests/CLI/TailsOptionValidationTests.cs` → ✅ Passes

---

### Step 3: Update ServerOptions

**Modify**: `Hosting/ServerOptions.cs`

```csharp
public sealed record ServerOptions
{
    public int Port { get; init; }
    public string OutputDirectory { get; init; } = "./telemetry-data";
    public LogLevel LogLevel { get; init; } = LogLevel.Information;

    // NEW: Tails options
    public bool EnableTails { get; init; } = false;
    public bool TailsFilterErrorsOnly { get; init; } = false;
}
```

**Modify**: `CLI/Commands/StartCommand.cs` - Pass options to ServerOptions

```csharp
var serverOptions = new ServerOptions
{
    Port = options.Port ?? DefaultPorts.Otlp,
    OutputDirectory = options.OutputDirectory,
    LogLevel = options.LogLevel,
    EnableTails = options.Tails,
    TailsFilterErrorsOnly = options.TailsFilterErrorsOnly
};
```

---

### Step 4: Register StdoutReceiver Conditionally

**Modify**: `Hosting/WebApplicationHost.cs` - `ConfigureServices()`

Add to pipeline configuration:

```csharp
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
    pipeline.RegisterReceiver(normalFileReceiver, allSignalsFilter);

    // Error files: errors only → .errors.ndjson
    var errorFileReceiver = new FileReceiver(
        sp.GetRequiredService<IFileRotationService>(),
        sp.GetRequiredService<IDiskSpaceChecker>(),
        options.OutputDirectory,
        ".errors.ndjson",
        sp.GetRequiredService<ILogger<FileReceiver>>());
    pipeline.RegisterReceiver(errorFileReceiver, errorsOnlyFilter);

    // Stdout receiver: conditional based on options (NEW)
    if (options.EnableTails)
    {
        var stdoutReceiver = new StdoutReceiver(
            sp.GetRequiredService<ILogger<StdoutReceiver>>());

        // Register with single filter (multiple filters supported via params)
        if (options.TailsFilterErrorsOnly)
        {
            pipeline.RegisterReceiver(stdoutReceiver, errorsOnlyFilter);
        }
        else
        {
            pipeline.RegisterReceiver(stdoutReceiver, allSignalsFilter);
        }

        // Future example: Multiple filters (errors from traces only)
        // pipeline.RegisterReceiver(stdoutReceiver, errorsOnlyFilter, new SignalTypeFilter("traces"));
    }

    return pipeline;
});
```

---

### Step 5: Add UX Messages

**Modify**: `CLI/Commands/StartCommand.cs`

Update `StartServerNormalModeAsync()`:

```csharp
private async Task<CommandResult> StartServerNormalModeAsync(
    CommandOptions options,
    ServerOptions serverOptions,
    CancellationToken cancellationToken)
{
    // Display startup banner if tails enabled
    if (options.Tails)
    {
        var filterMode = options.TailsFilterErrorsOnly ? "(errors only)" : "(all signals)";
        Console.WriteLine($"🔍 Monitoring telemetry output {filterMode}");
        Console.WriteLine("   Press Ctrl+C to stop...\n");
    }

    // Start server (blocks until shutdown)
    var exitCode = await _webHost.RunAsync(serverOptions, cancellationToken);

    // Display shutdown message if tails enabled
    if (options.Tails)
    {
        Console.WriteLine("\n✓ Monitoring stopped");
    }

    return exitCode == 0
        ? CommandResult.Success("Server stopped gracefully")
        : CommandResult.SystemError($"Server exited with code {exitCode}");
}
```

---

### Step 6: Update Existing Tests (if needed)

**Check for existing tests that might be affected**:
```bash
grep -r "StartCommand\|start.*--" e2e_tests/ unit_tests/ --include="*.cs"
```

**Potential updates**:
- Tests that validate `StartCommand` CLI options may need to verify new `--tails` options exist
- Tests that check for invalid option combinations should include new validation rules

**Example updates**:

If `unit_tests/CLI/StartCommandValidationTests.cs` exists:
- Add test: `StartCommand_RejectsTailsWithDaemon()`
- Add test: `StartCommand_RejectsTailsFilterWithoutTails()`

If `e2e_tests/CLI/StartCommandTests.cs` exists:
- Verify existing tests still pass (should not be affected)

**Verification**:
```bash
# Run existing tests to ensure no regressions
dotnet test unit_tests/CLI/ --verbosity normal
dotnet test e2e_tests/CLI/ --verbosity normal
```

---

### Step 7: Write New E2E Tests

**Create**: `e2e_tests/CLI/TailsModeTests.cs`

```csharp
using FluentAssertions;
using OpenTelWatcher.Tests.Infrastructure;
using System.Diagnostics;
using Xunit;

namespace OpenTelWatcher.E2ETests.CLI;

public class TailsModeTests : FileBasedTestBase
{
    [Fact]
    public async Task StartWithTails_OutputsNdjsonToStdout()
    {
        // Arrange
        var port = TestPorts.GetNext();
        var process = StartOpenTelWatcherProcess(
            args: $"start --port {port} --output-dir {TestDirectory} --tails",
            captureOutput: true);

        await WaitForServerReady(port);

        // Act - Send telemetry
        await SendTracesRequest(port, CreateMockTracesData());
        await Task.Delay(500); // Allow time for processing

        // Assert - Check stdout contains NDJSON
        var output = process.StandardOutput.ReadToEnd();
        output.Should().Contain("[traces]");
        output.Should().Contain("{\""); // JSON content
        output.Should().Contain("traceId");

        // Cleanup
        process.Kill();
        process.WaitForExit();
    }

    [Fact]
    public async Task StartWithTailsErrorsOnly_OutputsOnlyErrors()
    {
        // Arrange
        var port = TestPorts.GetNext();
        var process = StartOpenTelWatcherProcess(
            args: $"start --port {port} --output-dir {TestDirectory} --tails --tails-filter-errors-only",
            captureOutput: true);

        await WaitForServerReady(port);

        // Act - Send normal trace and error trace
        await SendTracesRequest(port, CreateNormalTrace());
        await SendTracesRequest(port, CreateErrorTrace());
        await Task.Delay(500);

        // Assert - Only error trace in stdout
        var output = process.StandardOutput.ReadToEnd();
        output.Should().NotContain("normalTraceId");
        output.Should().Contain("errorTraceId");

        // Cleanup
        process.Kill();
        process.WaitForExit();
    }

    [Fact]
    public async Task StartWithTails_StillWritesFilesToDisk()
    {
        // Arrange
        var port = TestPorts.GetNext();
        var process = StartOpenTelWatcherProcess(
            args: $"start --port {port} --output-dir {TestDirectory} --tails",
            captureOutput: true);

        await WaitForServerReady(port);

        // Act
        await SendTracesRequest(port, CreateMockTracesData());
        await Task.Delay(500);

        // Assert - Files created
        var files = Directory.GetFiles(TestDirectory, "traces.*.ndjson");
        files.Should().NotBeEmpty();

        // Cleanup
        process.Kill();
        process.WaitForExit();
    }

    [Fact]
    public async Task StartWithTails_OutputContainsTimestamp()
    {
        // Arrange
        var port = TestPorts.GetNext();
        var process = StartOpenTelWatcherProcess(
            args: $"start --port {port} --output-dir {TestDirectory} --tails",
            captureOutput: true);

        await WaitForServerReady(port);

        // Act
        await SendTracesRequest(port, CreateMockTracesData());
        await Task.Delay(500);

        // Assert - Timestamp format: [2025-01-19T12:00:00.123]
        var output = process.StandardOutput.ReadToEnd();
        output.Should().MatchRegex(@"\[\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}\]");

        // Cleanup
        process.Kill();
        process.WaitForExit();
    }

    [Fact]
    public async Task StartWithTailsAndDaemon_ReturnsError()
    {
        // Arrange
        var port = TestPorts.GetNext();

        // Act
        var result = await RunCliCommand($"start --port {port} --tails --daemon");

        // Assert
        result.ExitCode.Should().Be(1); // User error
        result.StandardError.Should().Contain("Cannot use --tails with --daemon");
    }

    [Fact]
    public async Task StartWithTailsFilterErrorsOnly_WithoutTails_ReturnsError()
    {
        // Arrange
        var port = TestPorts.GetNext();

        // Act
        var result = await RunCliCommand($"start --port {port} --tails-filter-errors-only");

        // Assert
        result.ExitCode.Should().Be(1); // User error
        result.StandardError.Should().Contain("Cannot use --tails-filter-errors-only without --tails");
    }

    [Fact]
    public async Task StartWithTails_CtrlC_GracefullyShutdown()
    {
        // Arrange
        var port = TestPorts.GetNext();
        var process = StartOpenTelWatcherProcess(
            args: $"start --port {port} --output-dir {TestDirectory} --tails",
            captureOutput: true);

        await WaitForServerReady(port);

        // Act - Send Ctrl+C signal
        process.StandardInput.Write('\x03'); // Ctrl+C
        var exited = process.WaitForExit(5000);

        // Assert
        exited.Should().BeTrue("Process should exit gracefully");
        process.ExitCode.Should().Be(0, "Should exit with success code");

        var output = process.StandardOutput.ReadToEnd();
        output.Should().Contain("✓ Monitoring stopped");
    }

    // Helper methods
    private Process StartOpenTelWatcherProcess(string args, bool captureOutput)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project opentelwatcher -- {args}",
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi);
        return process;
    }

    private async Task WaitForServerReady(int port, int timeoutMs = 10000)
    {
        var client = new HttpClient();
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                var response = await client.GetAsync($"http://127.0.0.1:{port}/api/version");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // Server not ready yet
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Server did not become ready in time");
    }
}
```

**Run Tests**: `dotnet test e2e_tests/CLI/TailsModeTests.cs` → ✅ Passes

---

### Step 8: Run All Existing Tests (Acceptance Criteria)

**Full Unit Test Suite**:
```bash
dotnet test unit_tests --verbosity normal
```

**Expected**:
- ✅ All existing unit tests pass (no regressions)
- ✅ New StdoutReceiver tests pass
- ✅ New CLI validation tests pass
- ✅ Code coverage maintained or improved

**Full E2E Test Suite**:
```bash
dotnet test e2e_tests --verbosity normal
```

**Expected**:
- ✅ All existing E2E tests pass (no behavioral changes)
- ✅ New TailsModeTests pass
- ✅ No test timeouts or instability

**If tests fail**:
1. Verify `StdoutReceiver` is only registered when `options.EnableTails == true`
2. Check that normal file writing still works (not affected by tails mode)
3. Ensure CLI validation doesn't break existing option combinations
4. Review DI configuration for conditional registration logic

---

### Step 9: Update Documentation

**Modify**: `CLAUDE.md` - CLI Commands section

Add new examples:

```markdown
### Tails Mode (Live Telemetry Output)

```bash
# Start with live telemetry output (all signals)
dotnet run --project opentelwatcher -- start --tails

# Start with live error output only
dotnet run --project opentelwatcher -- start --tails --tails-filter-errors-only

# Example output format:
# [2025-01-19T12:30:45.123] [traces] {"traceId":"abc123","spans":[...]}
# [2025-01-19T12:30:45.456] [logs] {"severityNumber":17,"body":"Error occurred"}
```

**Notes**:
- Tails mode writes to both stdout and files simultaneously
- Cannot be used with `--daemon` mode (requires foreground operation)
- Output is colorized (errors=red, traces=cyan, logs=white, metrics=green)
- Press Ctrl+C to stop monitoring and server gracefully
- NDJSON format is always preserved (no prettification)
```

---

### Step 8: Run Full Test Suite

**Unit Tests**:
```bash
dotnet test unit_tests --verbosity normal
```

**Expected**: All tests pass including new StdoutReceiver tests.

**E2E Tests**:
```bash
dotnet test e2e_tests --verbosity normal
```

**Expected**: All tests pass including new tails mode tests.

**Manual Testing**:

```bash
# Test 1: Normal tails
dotnet run --project opentelwatcher -- start --tails
# Send telemetry, verify colorized stdout output
# Verify files also created
# Ctrl+C to stop

# Test 2: Errors-only tails
dotnet run --project opentelwatcher -- start --tails --tails-filter-errors-only
# Send mix of normal and error telemetry
# Verify only errors in stdout
# Verify all telemetry in files

# Test 3: Validation errors
dotnet run --project opentelwatcher -- start --tails --daemon
# Should fail with error message

dotnet run --project opentelwatcher -- start --tails-filter-errors-only
# Should fail with error message
```

---

## Verification Checklist

Before considering feature complete:

**Code Changes**:
- [ ] `StdoutReceiver` implemented
- [ ] CLI options added (`--tails`, `--tails-filter-errors-only`)
- [ ] ServerOptions updated
- [ ] DI configuration conditionally registers StdoutReceiver
- [ ] UX messages added to StartCommand

**Test Changes**:
- [ ] New unit tests for StdoutReceiver written and passing
- [ ] New unit tests for CLI validation written and passing
- [ ] New E2E tests for tails mode written and passing
- [ ] Existing tests updated if needed (validation tests, etc.)

**Functionality**:
- [ ] Manual testing confirms colorized output
- [ ] Manual testing confirms timestamp format (`yyyy-MM-ddTHH:mm:ss.fff`)
- [ ] Manual testing confirms dual output (stdout + files)
- [ ] Manual testing confirms Ctrl+C graceful shutdown
- [ ] Validation prevents `--tails` + `--daemon`
- [ ] Validation prevents `--tails-filter-errors-only` without `--tails`

**Acceptance Criteria** (MUST ALL PASS):
- [ ] `dotnet test unit_tests` - 100% pass rate (all existing + new tests)
- [ ] `dotnet test e2e_tests` - 100% pass rate (all existing + new tests)
- [ ] No regressions in existing tests
- [ ] Code coverage maintained or improved
- [ ] Documentation updated in CLAUDE.md

---

## Summary

**New Files (4)**:
- `Services/Receivers/StdoutReceiver.cs`
- `unit_tests/Services/Receivers/StdoutReceiverTests.cs`
- `unit_tests/CLI/TailsOptionValidationTests.cs`
- `e2e_tests/CLI/TailsModeTests.cs`

**Modified Files (6+)**:
- `CLI/Models/CommandModels.cs` (add Tails, TailsFilterErrorsOnly properties)
- `CLI/CliApplication.cs` (add options and validators)
- `CLI/Commands/StartCommand.cs` (add UX messages, pass options)
- `Hosting/ServerOptions.cs` (add EnableTails, TailsFilterErrorsOnly)
- `Hosting/WebApplicationHost.cs` (conditionally register StdoutReceiver)
- `CLAUDE.md` (documentation)
- Any existing test files that test StartCommand validation (update as needed)

**Estimated Effort**: ~150 lines production code, ~250 lines test code

**Dependencies**: Requires completed refactoring from `refac.md`

## Future Extensibility with Multiple Filters

The pipeline supports multiple filters per receiver. Future options could include:

```bash
# Example: Tails for traces only
opentelwatcher start --tails --tails-signal traces

# Example: Tails for errors from logs only
opentelwatcher start --tails --tails-filter-errors-only --tails-signal logs

# Example: Sampled tails (future feature)
opentelwatcher start --tails --tails-sample-rate 0.1
```

Implementation would add new filters and combine them:

```csharp
var filters = new List<ITelemetryFilter>();
if (options.TailsFilterErrorsOnly)
    filters.Add(errorsOnlyFilter);
if (options.TailsSignal != null)
    filters.Add(new SignalTypeFilter(options.TailsSignal));
if (options.TailsSampleRate.HasValue)
    filters.Add(new SamplingFilter(options.TailsSampleRate.Value));

pipeline.RegisterReceiver(stdoutReceiver, filters.ToArray());
```

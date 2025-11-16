# OpenTelWatcher Quick Wins

This document identifies simple, high-impact improvements to OpenTelWatcher that make the usage scenarios described in `watcheruse.md` easier to understand and execute.

## Executive Summary

Based on analysis of the OpenTelWatcher project and the usage scenarios in `watcheruse.md`, **9 quick wins** have been identified. All are implementable in 1-4 hours each, with total effort of 1-2 days for the complete set.

### Priority Rankings

**Must-Have (Critical for testing workflows):**
1. **Check command** - CI/CD error detection
2. **--json flag** - Programmatic usage
3. **List command** - File discovery in tests

**High Value (Significant workflow improvement):**
4. **Stats command** - Telemetry capture visibility
5. **Status command** - Quick health checks
6. **Standard wait-for-startup** - Daemon mode waits for health by default

**Medium Value (Nice to have):**
7. **--errors-only flag** - Quick error visibility
8. **File pattern help** - Discoverability

**Low Priority (Advanced use case):**
9. **Tail command** - Live monitoring

---

## Quick Win #1: Add `status` CLI Command

### What
Create a new `opentelwatcher status` command that provides a quick one-line summary without the verbosity of `info`.

### Why It Helps

**Testing Workflow (watcheruse.md:1008-1012):** Users need to quickly check for errors during test runs with manual commands like `ls *.errors.ndjson`.

**Current pain point:** The `info` command is verbose - sometimes users just want: "Are there errors? How many files?"

**Common pattern in watcheruse.md:** Repeatedly checking error file existence.

### Complexity
**Trivial** (1-2 hours)

### Implementation

**Files to change:**
- `opentelwatcher/CLI/Commands/StatusCommand.cs` (new)
- `opentelwatcher/CLI/CliApplication.cs` (register command)

**Output format:**
```bash
# No errors
$ opentelwatcher status
✓ Healthy | 5 files (2.3 MB) | No errors

# Errors detected
$ opentelwatcher status
✗ Unhealthy | 12 files (5.1 MB) | 3 ERROR FILES DETECTED
```

**Exit codes:**
- 0: Healthy (no errors)
- 1: Unhealthy (errors detected)
- 2: Instance not running

### Usage Examples

**Quick health check:**
```bash
opentelwatcher status
```

**CI/CD integration:**
```bash
opentelwatcher status || exit 1
```

**Watch mode during testing:**
```bash
watch -n 1 'opentelwatcher status'
```

---

## Quick Win #2: Add `--errors-only` Flag to `info` Command

### What
Add a flag to the `info` command that filters output to show only error-related information.

### Why It Helps

**Common Analysis Patterns (watcheruse.md:1319-1327):** Finding errors is pattern #1 in the analysis guide.

**Testing Workflow (watcheruse.md:1106-1109):** Tests frequently check for error files.

**Current pain point:** Users doing rapid test iterations need quick error visibility but must scan through 30+ lines of `info` output.

### Complexity
**Easy** (2-3 hours)

### Implementation

**Files to change:**
- `opentelwatcher/CLI/Commands/InfoCommand.cs` (add flag parameter)
- `opentelwatcher/Utilities/ApplicationInfoDisplay.cs` (add error-only display mode)

**Code example:**
```csharp
// InfoCommand.cs
var errorsOnlyOption = new Option<bool>(
    "--errors-only",
    "Show only error-related information");

command.AddOption(errorsOnlyOption);
```

```csharp
// ApplicationInfoDisplay.cs
public static void DisplayErrorsOnly(InfoResponse info)
{
    var errorFiles = info.Files.Items
        .Where(f => f.Name.Contains(".errors.ndjson"))
        .ToList();

    if (errorFiles.Count == 0)
    {
        Console.WriteLine("No errors detected.");
        return;
    }

    Console.WriteLine("⚠️ ERRORS DETECTED\n");
    // Display error files and degraded health status
}
```

### Output Examples

**No errors:**
```bash
$ opentelwatcher info --errors-only
No errors detected.
```

**Errors found:**
```bash
$ opentelwatcher info --errors-only
⚠️ ERRORS DETECTED

Error Files (3 files, 450 KB):
  traces.20251116_143022_456.errors.ndjson (300 KB)
  traces.20251116_144530_123.errors.ndjson (100 KB)
  logs.20251116_143022_456.errors.ndjson (50 KB)

Health Status: Degraded (3 write failures in last hour)
```

### Usage Examples

**Testing:**
```bash
opentelwatcher info --errors-only > /dev/null || exit 1
```

**Split-screen development:**
```bash
# Left terminal: Run tests
npm test

# Right terminal: Watch for errors
watch -n 1 'opentelwatcher info --errors-only'
```

**With JSON output (combines with Quick Win #5):**
```bash
opentelwatcher info --errors-only --json | jq -e '.errorFiles | length == 0'
```

---

## Quick Win #3: Add `list` CLI Command for File Enumeration

### What
Create `opentelwatcher list` command that lists telemetry files with filtering options.

### Why It Helps

**Testing Workflow (watcheruse.md:948):** Users run `ls -lh ./telemetry-data/*.ndjson` to analyze results after tests.

**Common Analysis Patterns:** Multiple examples show manual file discovery with glob patterns.

**Current pain point:** No built-in way to list files with signal-specific filtering or error-only listing.

### Complexity
**Easy** (3-4 hours)

### Implementation

**Files to change:**
- `opentelwatcher/CLI/Commands/ListCommand.cs` (new)
- `opentelwatcher/CLI/CliApplication.cs` (register command)
- `opentelwatcher/Hosting/WebApplicationHost.cs` (add `/api/files` endpoint - optional)

**Command options:**
```bash
opentelwatcher list                    # All files
opentelwatcher list --signal traces    # Trace files only
opentelwatcher list --signal logs      # Log files only
opentelwatcher list --errors-only      # Error files only
opentelwatcher list --verbose          # Include full paths, timestamps
opentelwatcher list --json             # Machine-readable output
```

### Output Examples

**Default output:**
```bash
$ opentelwatcher list
traces.20251116_143022_456.ndjson        (1.1 MB)
traces.20251116_143022_456.errors.ndjson (300 KB)
traces.20251116_144530_123.ndjson        (500 KB)
logs.20251116_143022_456.ndjson          (400 KB)
logs.20251116_143022_456.errors.ndjson   (50 KB)
metrics.20251116_143022_456.ndjson       (100 KB)

Total: 6 files (2.45 MB)
```

**Filtered by signal:**
```bash
$ opentelwatcher list --signal traces
traces.20251116_143022_456.ndjson        (1.1 MB)
traces.20251116_143022_456.errors.ndjson (300 KB)
traces.20251116_144530_123.ndjson        (500 KB)

Total: 3 files (1.9 MB)
```

**Error files only:**
```bash
$ opentelwatcher list --errors-only
traces.20251116_143022_456.errors.ndjson (300 KB)
logs.20251116_143022_456.errors.ndjson   (50 KB)

Total: 2 error files (350 KB)
```

### Usage Examples

**Find latest trace file:**
```bash
LATEST_TRACE=$(opentelwatcher list --signal traces --json | jq -r '.files[-1].name')
cat "$LATEST_TRACE" | jq '.resourceSpans[].scopeSpans[].spans[]'
```

**Count error files in CI:**
```bash
ERROR_COUNT=$(opentelwatcher list --errors-only --json | jq '.files | length')
if [ "$ERROR_COUNT" -gt 0 ]; then
    echo "Found $ERROR_COUNT error files"
    exit 1
fi
```

---

## Quick Win #4: Add `stats` CLI Command for Quick Statistics

### What
Create `opentelwatcher stats` command showing telemetry statistics without file details.

### Why It Helps

**Common Analysis Patterns (watcheruse.md:1399-1407):** Users count spans by kind, calculate percentiles, but have no built-in way to get telemetry counts.

**Current pain point:** The `info` command shows file counts but not telemetry counts (traces received, spans, logs, etc.). Users need quick answers like "How many traces were captured?" without parsing NDJSON.

### Complexity
**Easy** (3-4 hours)

### Implementation

**Files to change:**
- `opentelwatcher/CLI/Commands/StatsCommand.cs` (new)
- `opentelwatcher/CLI/CliApplication.cs` (register command)
- `opentelwatcher/Hosting/WebApplicationHost.cs` (add `/api/stats` endpoint)

**Leverage existing:**
- `ITelemetryStatistics` service (already tracks counts)

### Output Examples

**Default output:**
```bash
$ opentelwatcher stats
Telemetry Statistics:
  Traces received:  42 requests (156 spans)
  Logs received:    156 requests (1,243 log records)
  Metrics received: 23 requests (89 data points)

Files: 5 (2.3 MB)
  traces:  2 files (1.1 MB)
  logs:    2 files (900 KB)
  metrics: 1 file (300 KB)

Uptime: 2h 34m 12s
```

**JSON output:**
```bash
$ opentelwatcher stats --json
{
  "telemetry": {
    "traces": {
      "requests": 42,
      "spans": 156
    },
    "logs": {
      "requests": 156,
      "logRecords": 1243
    },
    "metrics": {
      "requests": 23,
      "dataPoints": 89
    }
  },
  "files": {
    "traces": {"count": 2, "sizeBytes": 1153433},
    "logs": {"count": 2, "sizeBytes": 921600},
    "metrics": {"count": 1, "sizeBytes": 102400}
  },
  "uptimeSeconds": 9252
}
```

### Usage Examples

**Quick telemetry capture verification:**
```bash
opentelwatcher stats | grep "Traces received"
```

**Test assertion:**
```bash
TRACE_COUNT=$(opentelwatcher stats --json | jq '.telemetry.traces.requests')
if [ "$TRACE_COUNT" -lt 10 ]; then
    echo "Expected at least 10 trace requests, got $TRACE_COUNT"
    exit 1
fi
```

---

## Quick Win #5: Add `--json` Flag to All CLI Commands

### What
Add `--json` output format flag to all CLI commands for machine-readable output.

### Why It Helps

**Testing Workflow (watcheruse.md:969-979):** Tests parse command output programmatically.

**CI/CD Integration (watcheruse.md:1291-1302):** Automated workflows need structured data.

**Common Analysis Patterns:** All examples show programmatic parsing needs.

**Current pain point:** Commands output human-readable text only, requiring brittle text parsing in scripts.

### Complexity
**Easy** (4-6 hours for all commands)

### Implementation

**Files to change:**
- `opentelwatcher/CLI/Models/CommandModels.cs` (add `CommandOptions.JsonOutput` property)
- Update ALL command classes to check flag and output JSON:
  - `StartCommand.cs`
  - `StopCommand.cs`
  - `InfoCommand.cs`
  - `ClearCommand.cs`
  - `StatusCommand.cs` (new)
  - `ListCommand.cs` (new)
  - `StatsCommand.cs` (new)
  - `CheckCommand.cs` (new)

**Code pattern:**
```csharp
// In each command handler
if (jsonOutput)
{
    var jsonResponse = new { /* response model */ };
    Console.WriteLine(JsonSerializer.Serialize(jsonResponse,
        new JsonSerializerOptions { WriteIndented = false }));
}
else
{
    // Human-readable output
}
```

**Reuse existing API response models:**
- `InfoResponse`
- `ClearResponse`
- Create new models for other commands

### Usage Examples

**Extract specific values:**
```bash
opentelwatcher info --json | jq '.files.count'
opentelwatcher status --json | jq -r '.health.status'
opentelwatcher stats --json | jq '.telemetry.traces.requests'
```

**CI/CD assertions:**
```bash
# Check health status
STATUS=$(opentelwatcher status --json | jq -r '.health.status')
[ "$STATUS" = "Healthy" ] || exit 1

# Verify no errors
ERROR_COUNT=$(opentelwatcher list --errors-only --json | jq '.files | length')
[ "$ERROR_COUNT" -eq 0 ] || exit 1
```

**Programmatic test verification:**
```python
import subprocess
import json

result = subprocess.run(
    ["opentelwatcher", "stats", "--json"],
    capture_output=True,
    text=True
)
stats = json.loads(result.stdout)

assert stats["telemetry"]["traces"]["requests"] > 0, "No traces captured"
assert stats["files"]["traces"]["count"] > 0, "No trace files created"
```

---

## Quick Win #6: Add `check` Command for Error Detection

### What
Create `opentelwatcher check` command that exits with non-zero code if errors detected.

### Why It Helps

**Testing Workflow (watcheruse.md:1018-1030):** Tests need to assert no errors occurred.

**CI/CD Integration (watcheruse.md:1291-1302):** Pipelines check for errors and fail builds.

**Current pain point:** Common pattern `if ls *.errors.ndjson; then exit 1; fi` is verbose and error-prone.

### Complexity
**Trivial** (1-2 hours)

### Implementation

**Files to change:**
- `opentelwatcher/CLI/Commands/CheckCommand.cs` (new)
- `opentelwatcher/CLI/CliApplication.cs` (register command)

**Logic:**
1. Check for `*.errors.ndjson` files in output directory
2. Return exit code 0 (no errors) or 1 (errors found)
3. Optional `--verbose` shows error file details

**Code example:**
```csharp
public class CheckCommand
{
    public async Task<CommandResult> ExecuteAsync(CheckOptions options)
    {
        var errorFiles = Directory.GetFiles(
            options.OutputDir,
            "*.errors.ndjson");

        if (errorFiles.Length == 0)
        {
            if (options.Verbose)
                Console.WriteLine("✓ No errors detected");
            return new CommandResult(0, "No errors");
        }

        if (options.Verbose)
        {
            Console.WriteLine($"✗ {errorFiles.Length} error file(s) detected:");
            foreach (var file in errorFiles)
                Console.WriteLine($"  - {Path.GetFileName(file)}");
        }

        return new CommandResult(1, "Errors detected");
    }
}
```

### Usage Examples

**Simple CI check:**
```bash
opentelwatcher check || exit 1
```

**Verbose test assertion:**
```bash
opentelwatcher check --verbose
# Output: ✓ No errors detected
# Exit code: 0
```

**GitHub Actions:**
```yaml
- name: Verify no telemetry errors
  run: opentelwatcher check --verbose
```

**Test script:**
```bash
#!/bin/bash
set -e

# Run tests
npm test

# Verify no errors in telemetry
opentelwatcher check || {
    echo "Telemetry errors detected!"
    opentelwatcher list --errors-only
    exit 1
}
```

---

## Quick Win #7: Add File Name Patterns to Documentation Command

### What
Add `opentelwatcher help files` or enhance default help to show file naming patterns.

### Why It Helps

**File Naming Patterns (watcheruse.md:44-86):** Complex timestamp format `YYYYMMDD_HHMMSS_mmm` is not obvious to new users.

**Current pain point:** Information about `.errors.ndjson` suffix and naming conventions is only in `watcheruse.md`, not discoverable in CLI.

### Complexity
**Trivial** (1 hour)

### Implementation

**Option 1: Enhance default help**
```csharp
// CliApplication.cs
rootCommand.Description = @"
OpenTelWatcher - OpenTelemetry collector that persists to NDJSON files

File Naming Patterns:
  Normal files: {signal}.{timestamp}.ndjson
  Error files:  {signal}.{timestamp}.errors.ndjson

  Where {signal} = traces, logs, or metrics
        {timestamp} = YYYYMMDD_HHMMSS_mmm (UTC)

Examples:
  opentelwatcher start --port 4318
  opentelwatcher stop
  opentelwatcher info
  opentelwatcher clear
";
```

**Option 2: Add dedicated help command**
```csharp
// HelpCommand.cs
public class HelpCommand
{
    public void Execute(string topic)
    {
        switch (topic)
        {
            case "files":
                ShowFileNamingHelp();
                break;
            case "errors":
                ShowErrorFileHelp();
                break;
            default:
                ShowGeneralHelp();
                break;
        }
    }
}
```

### Output Example

```bash
$ opentelwatcher help files
File Naming Patterns:

Normal files:  {signal}.{timestamp}.ndjson
Error files:   {signal}.{timestamp}.errors.ndjson

Where:
  {signal}    = traces, logs, or metrics
  {timestamp} = YYYYMMDD_HHMMSS_mmm (UTC, 24-hour format)

Examples:
  traces.20251116_143022_456.ndjson
  traces.20251116_143022_456.errors.ndjson
  logs.20251116_143022_456.ndjson
  metrics.20251116_143100_789.ndjson

Error Files:
  Error files contain only telemetry with detected errors:
  - Traces: status.code = 2 (ERROR) or exception events
  - Logs: severityNumber >= 17 (ERROR/FATAL) or exception attributes

  Error files share timestamps with their normal file counterparts.
```

---

## Quick Win #8: Standard Wait-for-Startup Behavior in Daemon Mode

### What
Make `start --daemon` wait for successful startup by default before returning. This eliminates the need for manual delays in tests.

### Why It Helps

**Testing Workflow (watcheruse.md:1095-1096):** Tests currently use brittle `time.sleep(2)` hacks after daemon start - unreliable and slow.

**CI/CD patterns:** Automated workflows need reliable startup confirmation, not guesswork about timing.

**Current pain point:** Daemon mode exits immediately with uncertain startup state, forcing users to add arbitrary delays.

### Complexity
**Easy** (2-3 hours)

### Implementation

**Files to change:**
- `opentelwatcher/CLI/Commands/StartCommand.cs` (already has health check for daemon mode - make it standard behavior)

**Current behavior (problematic):**
```csharp
// StartCommand.cs - daemon mode
if (options.Daemon)
{
    // Spawn child process
    // Exit immediately - NO WAITING!
}
```

**New standard behavior:**
```csharp
// StartCommand.cs - daemon mode waits by default
if (options.Daemon)
{
    // Spawn child process

    if (!options.NoWait)  // Wait by default
    {
        // Existing health check logic (10 second timeout)
        await PerformHealthCheck(options.Port);
        Console.WriteLine("✓ Instance started successfully");
    }
    // else: Exit immediately for advanced async use cases
}
```

**Add optional --no-wait flag:**
```csharp
var noWaitOption = new Option<bool>(
    "--no-wait",
    getDefaultValue: () => false,  // Wait is the DEFAULT
    "Exit immediately without waiting for startup (advanced use case)");
```

### Usage Examples

**Default behavior (waits - RECOMMENDED):**
```bash
opentelwatcher start --daemon
# Blocks until health check passes (10 second timeout)
# Output: ✓ Instance started successfully
# Exit code: 0 (guaranteed healthy)
```

**Advanced async mode (opt-in with --no-wait):**
```bash
opentelwatcher start --daemon --no-wait
# Returns immediately without health check
# Use for scripting scenarios where you'll check health manually
```

**Test code - BEFORE this change:**
```python
# Brittle timing - BAD!
subprocess.run(["opentelwatcher", "start", "--daemon"])
time.sleep(2)  # Hope it's ready? What if it takes 3 seconds?
```

**Test code - AFTER this change:**
```python
# Reliable - GOOD!
subprocess.run(["opentelwatcher", "start", "--daemon"])
# Instance is GUARANTEED healthy at this point
# NO sleep needed!
```

**CI/CD - clean and reliable:**
```yaml
- name: Start OpenTelWatcher
  run: opentelwatcher start --daemon --port 4318
  # Command blocks until instance is healthy
  # No time.sleep(), no manual health checks needed!

- name: Run tests
  run: npm test
  # Telemetry collector is guaranteed to be ready
```

### Benefits Over Optional Flag Approach

**Why standard behavior beats optional flag:**
1. **Safer default:** New users get reliable behavior without knowing about `--wait-for-startup`
2. **Fewer surprises:** Tests "just work" without mysterious timing issues
3. **Cleaner syntax:** `start --daemon` instead of `start --daemon --wait-for-startup`
4. **Opt-out vs opt-in:** Advanced users can use `--no-wait` if they need async behavior

---

## Quick Win #9: Add `tail` Command for Live Log Viewing

### What
Create `opentelwatcher tail` command that shows real-time telemetry as it arrives (like `tail -f`).

### Why It Helps

**Common Analysis Patterns:** All examples show post-mortem file parsing - no real-time visibility.

**Developer workflow:** During development, users want to SEE telemetry arriving in real-time.

**Debugging:** "Is my app sending telemetry?" becomes immediately visible instead of checking files repeatedly.

### Complexity
**Moderate** (6-8 hours)

### Implementation

**Files to change:**
- `opentelwatcher/CLI/Commands/TailCommand.cs` (new)
- `opentelwatcher/CLI/CliApplication.cs` (register command)

**Technology:**
- Use `FileSystemWatcher` to monitor output directory
- Parse NDJSON as files are written
- Display human-readable summary (not raw JSON)

**Options:**
```bash
opentelwatcher tail                   # All signals
opentelwatcher tail --signal traces   # Traces only
opentelwatcher tail --errors-only     # Errors only
opentelwatcher tail --format json     # JSON output per entry
opentelwatcher tail --follow          # Keep watching (default)
opentelwatcher tail --lines 10        # Show last 10 entries then exit
```

**Code example:**
```csharp
public class TailCommand
{
    public async Task ExecuteAsync(TailOptions options)
    {
        using var watcher = new FileSystemWatcher(options.OutputDir);
        watcher.Filter = options.Signal != null
            ? $"{options.Signal}.*.ndjson"
            : "*.ndjson";

        watcher.Changed += (s, e) => ProcessNewData(e.FullPath, options);
        watcher.Created += (s, e) => ProcessNewData(e.FullPath, options);

        watcher.EnableRaisingEvents = true;

        Console.WriteLine($"Watching for {options.Signal ?? "telemetry"} in {options.OutputDir}...");
        Console.WriteLine("Press Ctrl+C to stop\n");

        await Task.Delay(Timeout.Infinite);
    }

    private void ProcessNewData(string filePath, TailOptions options)
    {
        // Read new lines from file
        // Parse NDJSON
        // Display summary
    }
}
```

### Output Examples

**Watching traces:**
```bash
$ opentelwatcher tail --signal traces
Watching for traces in ./telemetry-data...
Press Ctrl+C to stop

14:30:22.456 - TRACE: GET /api/users (124ms, OK)
14:30:22.789 - TRACE: POST /api/orders (56ms, OK)
14:30:23.123 - ERROR: Query failed (status=ERROR, message="Connection timeout")
14:30:24.456 - TRACE: GET /health (2ms, OK)
^C
```

**Errors only:**
```bash
$ opentelwatcher tail --errors-only
Watching for errors in ./telemetry-data...

14:30:23.123 - ERROR TRACE: Query failed
  TraceID: 5b8efff798038103d269b633813fc60c
  Message: Connection timeout

14:32:15.789 - ERROR LOG: Database connection lost
  Severity: ERROR
  Exception: System.Data.SqlException
^C
```

**JSON format:**
```bash
$ opentelwatcher tail --format json
{"timestamp":"2025-11-16T14:30:22.456Z","type":"trace","name":"GET /api/users","duration_ms":124,"status":"OK"}
{"timestamp":"2025-11-16T14:30:23.123Z","type":"trace","name":"Query failed","duration_ms":1502,"status":"ERROR","message":"Connection timeout"}
```

### Usage Examples

**Development workflow:**
```bash
# Terminal split-screen
# Left: Run application
npm run dev

# Right: Watch telemetry
opentelwatcher tail --signal traces
```

**Debugging:**
```bash
# Is my app sending telemetry?
opentelwatcher tail
# If nothing appears after triggering operations, telemetry isn't configured correctly
```

**Monitoring errors during testing:**
```bash
opentelwatcher tail --errors-only
# Any errors appear immediately as red text
```

---

## Implementation Roadmap

### Phase 1: Critical Testing Infrastructure (1 day)
**Priority: MUST-HAVE**

1. **Quick Win #6: `check` command** (1-2 hours)
   - Enables CI/CD error detection
   - Simplest implementation

2. **Quick Win #5: `--json` flag** (4-6 hours)
   - Foundation for all programmatic usage
   - Apply to existing commands first (info, clear)

3. **Quick Win #3: `list` command** (3-4 hours)
   - Essential for file discovery in tests
   - Builds on --json infrastructure

**Deliverable:** Tests and CI/CD can reliably detect errors, enumerate files, and parse output programmatically.

### Phase 2: High-Value Workflow Improvements (1 day)
**Priority: HIGH VALUE**

4. **Quick Win #1: `status` command** (1-2 hours)
   - Quick health visibility
   - Leverages existing health monitoring

5. **Quick Win #4: `stats` command** (3-4 hours)
   - Telemetry capture visibility
   - Uses existing ITelemetryStatistics

6. **Quick Win #8: Standard wait-for-startup** (2-3 hours)
   - Eliminates sleep hacks
   - Makes daemon mode reliable by default

**Deliverable:** Rapid iteration workflow with quick status checks and reliable startup.

### Phase 3: Polish and Convenience (0.5 days)
**Priority: MEDIUM VALUE**

7. **Quick Win #2: `--errors-only` flag** (2-3 hours)
   - Quick error visibility
   - Complements `status` and `check`

8. **Quick Win #7: File pattern help** (1 hour)
   - Documentation discoverability
   - Trivial enhancement to help text

**Deliverable:** Refined user experience with better discoverability and control.

### Phase 4: Advanced Features (Optional)
**Priority: LOW**

9. **Quick Win #9: `tail` command** (6-8 hours)
   - Live monitoring capability
   - Advanced use case, moderate effort

**Deliverable:** Real-time telemetry visibility for development workflows.

---

## Estimated Total Effort

| Phase | Quick Wins | Hours | Priority |
|-------|-----------|-------|----------|
| Phase 1 | #6, #5, #3 | 8-12 | Critical |
| Phase 2 | #1, #4, #8 | 6-9 | High |
| Phase 3 | #2, #7 | 3-4 | Medium |
| Phase 4 | #9 | 6-8 | Low |
| **Total (all)** | **9 quick wins** | **23-33 hours** | **1-2 days per developer** |

**Recommended approach:** Implement Phases 1-3 (Quick Wins #1-8) for complete coverage of common usage scenarios. Phase 4 can be deferred or implemented based on user demand.

---

## Success Metrics

After implementation, usage scenarios from `watcheruse.md` will be measurably improved:

### Before Quick Wins
```bash
# Testing workflow - brittle and verbose
opentelwatcher clear --output-dir ./telemetry-data --silent
opentelwatcher start --daemon --port 4318
sleep 2  # Hope it started?
run_tests
if ls ./telemetry-data/*.errors.ndjson 1>/dev/null 2>&1; then
    echo "Errors found!"
    cat ./telemetry-data/*.errors.ndjson | jq .
    exit 1
fi
```

### After Quick Wins
```bash
# Testing workflow - reliable and concise
opentelwatcher clear
opentelwatcher start --daemon  # Waits until healthy
run_tests
opentelwatcher check || exit 1  # Simple error detection
```

### Impact Measurements
- **Lines of test code:** 10 lines → 4 lines (60% reduction)
- **Sleep hacks eliminated:** 100% (--wait-for-startup)
- **Error detection:** Manual file checking → Single command
- **JSON parsing:** Custom scripts → Built-in flag
- **File discovery:** External commands → Native `list` command

---

## Conclusion

These 10 quick wins represent **high-impact, low-effort** improvements that directly address pain points identified in `watcheruse.md`. All are implementable in 1-2 days of focused development and will significantly enhance the developer experience for testing, debugging, and CI/CD integration workflows.

**Recommended action:** Implement Phases 1-3 (Quick Wins #1-9) to cover all critical and high-value scenarios, with Phase 4 (#10 - tail command) as an optional enhancement based on user feedback.

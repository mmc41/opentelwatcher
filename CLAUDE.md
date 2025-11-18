# Opentelwatcher Development Guidelines

## Active Technologies
- C# 14 with .NET 10 
- File-based NDJSON
- Microsoft.AspNetCore.OpenApi 10.0.0 - OpenAPI 3.0 document generation
- Swashbuckle.AspNetCore.SwaggerUI 10.0.1 - Interactive API documentation
- C# 14 with .NET 10 + System.CommandLine, Microsoft.Extensions.Http (HttpClient), System.Text.Json
- N/A (stateless CLI, no persistence required)

- C# 14 with .NET 10
- ASP.NET Core Minimal APIs (001-otlp-file-storage)
- ASP.NET Core Razor Pages (002-status-page)
- Terminal.css CSS framework (002-status-page)
- XUnit 3 testing framework (001-otlp-file-storage)
- FluentAssertions (001-otlp-file-storage)
- dotnet-coverage code coverage (003-test-reporting)
- dotnet-trx test reporter (003-test-reporting)

## Project Structure

```text
opentelwatcher/                     # Main production project
├── Configuration/
├── Serialization/
├── Services/
│   ├── Interfaces/
│   │   ├── IPidFileService.cs           # PID file management interface
│   │   └── IErrorDetectionService.cs     # Error detection interface
│   ├── PidFileService.cs                 # PID file implementation
│   └── ErrorDetectionService.cs          # Error/exception detection for traces and logs
├── Hosting/                 # WebApplication hosting abstractions
│   ├── IWebApplicationHost.cs   # Abstraction for testability
│   ├── ServerOptions.cs         # Server configuration model
│   ├── ValidationResult.cs      # Validation result model
│   └── WebApplicationHost.cs    # Production implementation
├── CLI/                     # CLI interface (005-cli-interface)
│   ├── Commands/           # Command implementations
│   │   ├── DefaultCommand.cs
│   │   ├── StartCommand.cs      # Actually starts server via IWebApplicationHost
│   │   ├── ShutdownCommand.cs
│   │   ├── InfoCommand.cs       # Display application information
│   │   └── ClearCommand.cs      # Clear telemetry data files (dual-mode)
│   ├── Models/             # CLI data models
│   │   ├── ApiModels.cs
│   │   └── CommandModels.cs
│   ├── Services/           # CLI services
│   │   ├── IOpenTelWatcherApiClient.cs
│   │   ├── OpenTelWatcherApiClient.cs
│   │   ├── IPortResolver.cs         # Port auto-detection interface
│   │   └── PortResolver.cs          # Port resolution from PID file
│   └── CliApplication.cs   # CLI orchestrator (System.CommandLine 2.0)
├── web/                     # Web content (002-status-page)
│   ├── Index.cshtml        # Status page Razor view
│   ├── Index.cshtml.cs     # Status page model
│   └── terminal.css        # Terminal.css framework
├── Utilities/               # Helper classes (002-status-page)
│   ├── ApplicationInfoDisplay.cs  # Banner and info display
│   ├── NumberFormatter.cs         # Formatting utilities
│   ├── UptimeFormatter.cs         # Uptime formatting
│   └── TelemetryCleaner.cs        # Shared file clearing logic (used by API and CLI)
├── Program.cs               # Simplified entry point (~40 lines)
├── appsettings.json
└── opentelopentelwatcher.pid              # Process ID file (created in daemon mode, auto-removed on shutdown)

unit_tests/                  # Unit test project
├── xunit.runner.json        # xUnit configuration (reduced diagnostic messages)
└── [Test files mirror opentelopentelwatcher/ structure]

e2e_tests/                   # E2E test project
├── xunit.runner.json        # xUnit configuration (sequential execution for reliability)
├── CLI/                     # CLI command E2E tests
└── [E2E test scenarios]

artifacts/                   # All build outputs and reports (gitignored)
├── bin/                    # Build outputs (organized by project)
│   ├── opentelopentelwatcher/Debug/
│   ├── unit_tests/Debug/
│   └── e2e_tests/Debug/
├── obj/                    # Intermediate build outputs (organized by project)
│   ├── opentelopentelwatcher/Debug/
│   ├── unit_tests/Debug/
│   └── e2e_tests/Debug/
├── test-results/           # TRX test result files and coverage data
├── coverage-report/        # HTML coverage reports
└── logs/                   # NLog application and test logs

.config/                    # Tooling configuration
└── dotnet-tools.json       # Local tool manifest

.runsettings                # Test configuration (coverage enabled by default)
Directory.Build.props       # MSBuild properties (centralizes build outputs, sets test verbosity)
Directory.Build.targets     # MSBuild targets (auto-generates coverage summary, cleans artifacts)
NLog.config                 # NLog logging configuration (shared across all projects)
project.root                # Solution root marker for E2E test discovery
```

## Testing

For detailed testing guidelines, best practices, and patterns, see **[TESTING.md](TESTING.md)**.

**Quick Reference:**
- Extend `FileBasedTestBase` for tests needing temp directories
- Use `TestBuilders` for creating test data
- Use `TestConstants` instead of magic numbers
- Always use `TestContext.Current.CancellationToken` for async operations
- Avoid inline try/finally cleanup blocks

## Commands

### Build and Test

```bash
# Build entire solution
dotnet build

# Run all tests (minimal verbosity by default)
dotnet test

# Run all tests with verbose output (shows individual test results)
dotnet test --verbosity normal

# Run unit tests only
dotnet test unit_tests

# Run E2E tests only
dotnet test e2e_tests

# Run application
dotnet run --project opentelwatcher
```

When running tests watch out for console warning messages and attempt fixes. In particular,
the warning "Ungraceful shutdown(s) detected" is likely due to a bug in E2E process cleanup
and the warning "opentelopentelwatcher.pid file still exists" indicated a bug related to cleanup of pid not
done OR a bug in E2E process cleanup. Always investigate such warnings and look for shutdown bugs.

**Log File Inspection After Test Runs:**

Always inspect `artifacts/logs/opentelwatcher-all-{date}.log` after running tests in the following cases:

1. **Test run takes unexpectedly long time** - Check logs for bugs such as hanging threads, deadlocks, or infinite loops
2. **After test suite completes** - Check for potential errors not detected by xUnit tests (e.g., unhandled exceptions in background threads, resource leaks, warnings)
3. **When xUnit test fails** - Use logs for debugging and locating the root cause of failures by examining the complete execution flow

The `-all-` log file contains complete diagnostic history including Microsoft framework logs, making it the most comprehensive source for troubleshooting test issues.

### CLI Interface (005-cli-interface)

```bash
# Show help and available commands
dotnet run --project opentelwatcher

# Start the opentelwatcher service with options
dotnet run --project opentelwatcher -- start --port 4318 --output-dir ./data

# Start in background mode (non-blocking)
dotnet run --project opentelwatcher -- start --daemon --port 4318

# Stop the running instance (auto-detects port from PID file)
dotnet run --project opentelwatcher -- stop

# Stop instance on specific port
dotnet run --project opentelwatcher -- stop --port 4318

# View instance status (health, version, config, stats)
dotnet run --project opentelwatcher -- status

# View status for specific port
dotnet run --project opentelwatcher -- status --port 4318

# View only error information
dotnet run --project opentelwatcher -- status --errors-only

# View only statistics (telemetry stats and file counts)
dotnet run --project opentelwatcher -- status --stats-only

# Suppress output, only exit code (for scripting)
dotnet run --project opentelwatcher -- status --quiet

# JSON output (all modes support this)
dotnet run --project opentelwatcher -- status --json

# Standalone mode: check for errors without running instance
dotnet run --project opentelwatcher -- status --output-dir ./telemetry-data --quiet

# List telemetry files
dotnet run --project opentelwatcher -- list

# List only error files
dotnet run --project opentelwatcher -- list --errors-only

# Clear telemetry data files (auto-detects port from PID file)
dotnet run --project opentelwatcher -- clear

# Clear from specific port instance
dotnet run --project opentelwatcher -- clear --port 4318

# Clear with options (standalone mode)
dotnet run --project opentelwatcher -- clear --output-dir ./telemetry-data --verbose

# Show help (same as no args)
dotnet run --project opentelwatcher -- --help
```

**CLI Commands (5 core):**
- `opentelwatcher` (no args) - Display help and available commands
- `opentelwatcher start` - Start the watcher service with optional configuration
  - `--port <number>` - Port number (default: 4318)
  - `--output-dir, -o <path>` - Output directory (default: ./telemetry-data)
  - `--log-level <level>` - Log level (default: Information)
  - `--daemon` - Run in background (non-blocking mode)
- `opentelwatcher stop` - Stop the running instance
  - `--port <number>` - Port number (auto-detected from PID file if omitted)
  - `--silent` - Suppress all output except errors
  - `--json` - Output in JSON format
- `opentelwatcher status` - Unified status/info/check command (dual-mode: API + filesystem)
  - `--port <number>` - Port number (auto-detected from PID file if omitted)
  - `--errors-only` - Show only error information
  - `--stats-only` - Show only telemetry statistics
  - `--verbose` - Show detailed diagnostic information
  - `--quiet` - Suppress output, only exit code (for scripting)
  - `--json` - Output in JSON format
  - `--output-dir, -o <path>` - Standalone filesystem mode (scan directory for errors)
  - Exit codes: 0 (healthy/no errors), 1 (unhealthy/errors detected), 2 (system error)
- `opentelwatcher list` - List telemetry files
  - `--signal <type>` - Filter by signal type (traces, logs, metrics)
  - `--errors-only` - List only error files
  - `--json` - Output in JSON format
- `opentelwatcher clear` - Clear telemetry data files
  - `--port <number>` - Port number (auto-detected from PID file if omitted)
  - `--output-dir, -o <path>` - Directory to clear (validated against instance when running)
  - `--verbose` - Show detailed operation information
  - `--silent` - Suppress all output except errors
  - `--json` - Output in JSON format
- `opentelwatcher --help` / `opentelwatcher -h` - Show help message

**CLI-to-API Mapping (1-1):**
- `start` → (no API - bootstraps server)
- `stop` → POST `/api/stop`
- `status` → GET `/api/status` (or filesystem scan in standalone mode)
- `list` → GET `/api/list`
- `clear` → POST `/api/clear`

**CLI Architecture (System.CommandLine 2.0):**
- Declarative command/option definitions via `RootCommand` and `Command`
- Automatic help generation from `Description` properties
- Built-in validators for type checking and range validation
- Automatic alias support (e.g., `-o` for `--output-dir`)
- Version compatibility checking (major version must match)
- HTTP client communication with running instance via `/api/status`, `/api/stop`, `/api/list`, `/api/clear`
- Exit codes: 0 (success), 1 (user error), 2 (system error)
- Command pattern with dependency injection

**Port Auto-Resolution:**
- Commands that interact with the API (`stop`, `status`, `clear`) support automatic port detection
- When `--port` is omitted, the `PortResolver` service consults the PID file (`opentelwatcher.pid`)
- **Behavior**:
  - If exactly one instance is running: Uses that instance's port automatically
  - If multiple instances are running: Returns error "Multiple instances running on ports: X, Y, Z. Please specify --port"
  - If no instances are running: Returns error "No running instances found. Please specify --port or start an instance"
  - Stale PID entries (dead processes) are automatically filtered out
- **Benefits**:
  - Simplified workflow when running single instance (default case)
  - Explicit port required for multiple instances (prevents accidental operations)
  - Clear error messages guide users to correct usage
- **Implementation**:
  - `IPortResolver` service with dependency injection
  - Uses `IPidFileService` to read PID file entries
  - Uses `IProcessProvider` to verify process is alive
  - Structured logging for debugging port resolution

**Troubleshooting Port Resolution:**

Common errors and solutions:

1. **"No running instances found"**
   - **Cause**: No `opentelwatcher.pid` file exists OR all PIDs in file are for terminated processes
   - **Solution**:
     - Start an instance: `opentelwatcher start --port 4318`
     - Or specify port explicitly: `opentelwatcher <command> --port 4318`
   - **Check**: Verify PID file exists in working directory: `ls opentelwatcher.pid`

2. **"Multiple instances running on ports: X, Y, Z"**
   - **Cause**: Multiple instances are running simultaneously on different ports
   - **Solution**: Specify which instance to target: `opentelwatcher <command> --port X`
   - **List all instances**: Check PID file content to see all registered instances
   - **Stop all**: Run `opentelwatcher stop --port X` for each port

3. **PID file exists but command says "No running instances found"**
   - **Cause**: All processes in PID file have exited, but file wasn't cleaned up
   - **Solution**: Delete stale PID file: `rm opentelwatcher.pid`
   - **Prevention**: Use graceful shutdown (`opentelwatcher stop`) instead of force kill

4. **Port resolution works intermittently**
   - **Cause**: Race condition during instance startup/shutdown
   - **Solution**: Wait a few seconds after start/stop before running next command
   - **Workaround**: Use explicit `--port` for reliability in scripts

5. **"Permission denied" when accessing PID file**
   - **Cause**: File permissions issue or file locked by another process
   - **Solution**: Check file permissions: `ls -l opentelwatcher.pid`
   - **Fix**: Adjust permissions or run command with appropriate user

**Debugging Port Resolution:**

Enable verbose logging to see port resolution details:

```bash
# Set log level to Debug (shows port resolution logs)
export DOTNET_ENVIRONMENT=Development

# Run command - will show detailed port resolution logs
opentelwatcher stop --verbose
```

Check application logs in `artifacts/logs/opentelwatcher-all-{date}.log` for port resolution diagnostics.

**Status Command Dual-Mode Behavior:**
- **Instance Running (API Mode)**:
  - Calls `GET /api/status` to retrieve full information
  - Returns: health, version, uptime, config, file stats, telemetry stats, errors
  - Exit code: 0 (healthy), 1 (errors detected), 2 (system error)
- **Standalone (Filesystem Mode)**:
  - No running instance required
  - Scans `--output-dir` for `*.errors.ndjson` files
  - Returns: error file count, error file list
  - Limited information (no API data available)
  - Exit code: 0 (no errors), 1 (errors detected)
- **Mode Selection**:
  - Automatically attempts API connection first
  - Falls back to filesystem mode if instance not running
  - User can force filesystem mode with `--output-dir` parameter
- **Use Cases**:
  - CI/CD: Check for errors after tests (instance stopped): `watcher status --output-dir ./telemetry-data --quiet`
  - Development: Get full status while running: `watcher status`
  - Scripting: Health check with exit code: `watcher status --quiet && echo "healthy"`

**Clear Command Dual-Mode Behavior:**
- **Instance Running Mode**:
  - Queries `/api/status` to get instance's output directory
  - Validates `--output-dir` option against instance directory (if provided)
  - Calls `/api/clear` endpoint to delete files
  - Displays before/after stats, files deleted, and space freed
- **Standalone Mode** (no instance running):
  - Uses `TelemetryCleaner` utility to clear files directly
  - Clears from directory specified by `--output-dir` (default: `./telemetry-data`)
  - Displays same statistics as instance mode
- **No Code Duplication**: Both API endpoint and CLI command use shared `TelemetryCleaner` utility
- **Error File Handling**: Automatically deletes both normal and `.errors.ndjson` files (pattern: `*.ndjson`)

### Test Reporting and Coverage

**Note:** `dotnet test` is pre-configured to automatically:
- Collect code coverage (Microsoft.CodeCoverage with child process support)
- Generate binary .coverage files
- Merge coverage across all test projects
- Output all artifacts to `./artifacts/`
- Use minimal verbosity by default for clean output

**Coverage Collection:**
- Uses Microsoft.CodeCoverage (dotnet-coverage) for comprehensive coverage tracking
- Supports child process coverage (E2E tests with Process.Start)
- Coverage data stored in shared memory (survives crashes)
- Automatic solution-level merging

**Centralized Build Outputs:**
- All build outputs (bin, obj) are centralized in `./artifacts/` folder
- Coverage files: `./artifacts/test-results/**/*.coverage` (binary format)
- Merged coverage: `./artifacts/coverage-report/merged.coverage`
- Visual Studio XML: `./artifacts/coverage-report/coverage.xml`

**Test Output Verbosity:**
- Default verbosity set to `minimal` in `Directory.Build.props` (VSTestVerbosity property)
- xUnit diagnostic messages are disabled via `xunit.runner.json` in test projects
- Console logging set to Warn level in `NLog.config` to reduce noise
- Coverage summary report saved to file instead of displayed in console
- Override with `--verbosity normal` to see individual test results

```bash
# Install reporting tools (first time only)
dotnet tool restore

# Run tests (minimal output by default, auto-collects coverage)
dotnet test

# Run tests with verbose output (shows individual test results)
dotnet test --verbosity normal

# Convert coverage to Visual Studio XML format (done automatically by MSBuild)
dotnet-coverage merge ./artifacts/test-results/**/*.coverage \
  -o ./artifacts/coverage-report/coverage.xml \
  -f xml

# View coverage summary (generated automatically after tests)
cat ./artifacts/coverage-report/Summary.txt

# Generate unified HTML test report (from TRX files)
cd ./artifacts/test-results && dotnet trx

# Clean all build outputs and artifacts (via MSBuild target)
dotnet clean

# Override .runsettings if needed (run tests without coverage)
dotnet test --settings /dev/null
```

### Publishing

**Single-File Executable (Release Build Only):**

The project is configured to publish as a single, self-contained executable with no external files:

```bash
# Publish for specific platform
dotnet publish opentelwatcher -c Release -r win-x64 --self-contained
dotnet publish opentelwatcher -c Release -r linux-x64 --self-contained
dotnet publish opentelwatcher -c Release -r osx-arm64 --self-contained

# Output: artifacts/publish/opentelwatcher/release/opentelwatcher.exe (or opentelwatcher on Unix)
```

**Publish Configuration (opentelwatcher.csproj):**
- `PublishSingleFile=true` - Single executable file
- `SelfContained=true` - Includes .NET runtime
- `IncludeNativeLibrariesForSelfExtract=true` - Extracts native libraries to temp
- `EnableCompressionInSingleFile=true` - Compresses embedded files
- `PublishTrimmed=false` - Trimming disabled for reflection compatibility
- `DebugType=none` - No .pdb files in release
- `IsWebConfigTransformDisabled=true` - No web.config generation

**Excluded Files:**
- appsettings.json (use CLI arguments or environment variables instead)
- appsettings.*.json (all environment-specific configs)
- web.config (IIS-specific, not needed)
- MvcTestingAppManifest.json (testing artifact)
- *.staticwebassets.endpoints.json (embedded resources used instead)
- *.pdb (debug symbols)

**Result:** Single ~58 MB executable with everything embedded (runtime, dependencies, web resources, protobuf definitions)

### Development Workflow

```bash
# TDD workflow for new feature
# 1. Write failing test (Red)
dotnet test unit_tests/<TestFile>.cs

# 2. Implement minimum code (Green)
# 3. Refactor while keeping tests green
# 4. Repeat
```

## Code Style

C# 14 with .NET 10: Follow standard conventions

### Razor Pages (002-status-page)

- Page Models in `web/*.cshtml.cs`
- Views in `web/*.cshtml`
- Use `@page` directive at top of .cshtml files
- Inject services via constructor in Page Model
- Keep logic in Page Model, presentation in .cshtml
- Configure custom root directory in `WebApplicationHost.cs`: `options.RootDirectory = "/web"`

### Static Files (002-status-page)

- Terminal.css located at `opentelopentelwatcher/web/terminal.css` (pre-downloaded)
- Reference in views with `~/` prefix (e.g., `~/web/terminal.css`)
- Enable with `app.UseStaticFiles()` in Program.cs

### CLI Interface (005-cli-interface)

**Command Pattern:**
- Each command is a separate class implementing `ExecuteAsync` method
- Commands are resolved via dependency injection
- All commands return `CommandResult` with exit code and message
- Use `CommandOptions` record for passing configuration

**API Client:**
- `IWatcherApiClient` interface for HTTP communication
- Configured via `HttpClient` with base address
- 30-second timeout for API requests
- Graceful handling of connection failures

**Models:**
- Use C# records for immutable data models
- `ApiModels.cs` for API responses (JSON serialization with System.Text.Json)
- `CommandModels.cs` for command execution data

**System.CommandLine Architecture (Best Practices):**
- Program.cs delegates ALL execution to CliApplication (~40 lines total)
- No manual argument inspection - System.CommandLine handles all parsing and routing
- RootCommand shows help by default when no arguments provided
- Commands actually perform their stated actions (StartCommand starts the server)
- IWebApplicationHost abstraction enables testing without actual server startup
- WebApplicationHost encapsulates all ASP.NET Core configuration and middleware setup
- Commands are fully testable via dependency injection with mocked dependencies

**Entry Point Flow:**
```
args → Program.cs (try/catch/finally wrapper)
     → CliApplication.RunAsync(args)
     → System.CommandLine.Parse(args)
     → System.CommandLine.Invoke()
     → Command handler (StartCommand, StopCommand, DiagnoseCommand)
```

**StartCommand Behavior:**
- Injects IWatcherApiClient (for pre-flight checks) and IWebApplicationHost (for server startup)
- Checks if instance already running before starting
- Validates configuration using IWebApplicationHost.Validate()
- Normal mode: Calls IWebApplicationHost.RunAsync() to start server (blocks until server stops)
- Daemon mode: Forks child process and exits parent immediately

**Testing Strategy:**
- Command logic: Unit tests with MockWebApplicationHost and MockWatcherApiClient
- Argument parsing: Unit tests with System.CommandLine parser
- End-to-end behavior: E2E tests with actual subprocess (DaemonModeTests)
- CliCommandTests (E2E) replaced with faster, more focused unit tests

**Pre-flight Check:**
- Before starting server (both normal and daemon modes), checks if instance is already running on target port
- Uses HTTP client to query `/api/version` endpoint with 3-second timeout
- Displays friendly error message if instance already running (prevents socket binding exception)
- Validates version compatibility (major version must match)
- Exit code 1 if instance already running, exit code 2 if incompatible version detected

**Daemon Mode:**
- `--daemon` flag spawns background process and exits parent immediately
- Uses `dotnet` executable to launch .dll files in child process
- Sets `UseShellExecute = false` and `CreateNoWindow = true` for proper background execution
- Performs health check (10 second timeout) to verify child process started successfully
- Creates `opentelopentelwatcher.pid` file in working directory with process ID
- PID file automatically removed on graceful shutdown
- Child process inherits working directory but detaches from parent console

**Endpoint Standardization:**
- All endpoints use 127.0.0.1 instead of localhost for consistency
- OpenAPI server URL configured as 127.0.0.1:{port} for Swagger UI
- Status page forces 127.0.0.1 in all displayed endpoint URLs
- HTTP clients use 127.0.0.1 for API communication
- Avoids IPv4/IPv6 ambiguity and ensures consistent behavior

**Other:**
- Do not mock ILogger or ILoggerFactory. Do not use NullLogger. Also do not use NLog api directly except from configuration.
- When the user finds a bug or a review finds a bug (outside the normal process of adding a feature) do  first try to reproduce it with a unit or E2E test before fixing the bug.
- All code and build/test infrastructure must be cross platform so it works on windows, mac and linux (without installing special tools/shells to simulate other platforms)
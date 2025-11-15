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
- Coverlet code coverage (003-test-reporting)
- dotnet-trx test reporter (003-test-reporting)
- ReportGenerator coverage reports (003-test-reporting)

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
│   │   ├── IWatcherApiClient.cs
│   │   └── WatcherApiClient.cs
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
done OR a bug in E2E process cleanup.

### CLI Interface (005-cli-interface)

```bash
# Show help and available commands
dotnet run --project opentelwatcher

# Start the opentelwatcher service with options
dotnet run --project opentelwatcher -- start --port 4318 --output-dir ./data

# Start in background mode (non-blocking)
dotnet run --project opentelwatcher -- start --daemon --port 4318

# Stop the running instance
dotnet run --project opentelwatcher -- stop

# View diagnostic information
dotnet run --project opentelwatcher -- info

# Clear telemetry data files
dotnet run --project opentelwatcher -- clear

# Clear with options (standalone mode)
dotnet run --project opentelwatcher -- clear --output-dir ./telemetry-data --verbose

# Show help (same as no args)
dotnet run --project opentelwatcher -- --help
```

**CLI Commands:**
- `opentelwatcher` (no args) - Display help and available commands
- `opentelwatcher start` - Start the watcher service with optional configuration
  - `--port <number>` - Port number (default: 4318)
  - `--output-dir, -o <path>` - Output directory (default: ./telemetry-data)
  - `--log-level <level>` - Log level (default: Information)
  - `--daemon` - Run in background (non-blocking mode)
- `opentelwatcher stop` / `opentelwatcher shutdown` - Stop the running instance
- `opentelwatcher info` - View application information (version, health, files, config)
- `opentelwatcher clear` - Clear telemetry data files
  - `--output-dir, -o <path>` - Directory to clear (validated against instance when running)
  - `--verbose` - Show detailed operation information
  - `--silent` - Suppress all output except errors
- `opentelwatcher --help` / `opentelwatcher -h` - Show help message

**CLI Architecture (System.CommandLine 2.0):**
- Declarative command/option definitions via `RootCommand` and `Command`
- Automatic help generation from `Description` properties
- Built-in validators for type checking and range validation
- Automatic alias support (e.g., `-o` for `--output-dir`)
- Version compatibility checking (major version must match)
- HTTP client communication with running instance via `/api/info`, `/api/shutdown`, `/api/clear`
- Exit codes: 0 (success), 1 (user error), 2 (system error)
- Command pattern with dependency injection

**Clear Command Dual-Mode Behavior:**
- **Instance Running Mode**:
  - Queries `/api/info` to get instance's output directory
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
- Collect code coverage (coverlet)
- Generate TRX test reports
- Output all artifacts to `./artifacts/`
- Use minimal verbosity by default for clean output

**Centralized Build Outputs:**
- All build outputs (bin, obj) are centralized in `./artifacts/` folder
- Organized by project name: `artifacts/bin/{project}/Debug/`
- Keeps project directories clean and organized
- Single `dotnet clean` removes all build artifacts

**Test Output Verbosity:**
- Default verbosity set to `minimal` in `Directory.Build.props` (VSTestVerbosity property)
- xUnit diagnostic messages are disabled via `xunit.runner.json` in test projects
- Console logging set to Warn level in `NLog.config` to reduce noise
- Coverage summary report saved to file instead of displayed in console
- Override with `--verbosity normal` to see individual test results

```bash
# Install reporting tools (first time only)
dotnet tool restore

# Run tests (minimal output by default)
dotnet test

# Run tests with verbose output (shows individual test results)
dotnet test --verbosity normal

# Generate HTML coverage report for browsing
dotnet reportgenerator \
  "-reports:./artifacts/test-results/**/coverage.cobertura.xml" \
  "-targetdir:./artifacts/coverage-report" \
  "-reporttypes:Html"

# Open HTML coverage report
start ./artifacts/coverage-report/index.html  # Windows
open ./artifacts/coverage-report/index.html   # macOS
xdg-open ./artifacts/coverage-report/index.html  # Linux

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

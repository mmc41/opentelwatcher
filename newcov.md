# Migration Plan: Coverlet → dotnet-coverage (Microsoft.CodeCoverage)

**Document Version:** 1.0
**Date:** 2025-11-17
**Project:** OpenTelWatcher
**Status:** Planning

---

## Executive Summary

This document outlines the complete migration from Coverlet to Microsoft's `dotnet-coverage` tooling for code coverage collection across unit tests and E2E tests. The migration addresses a critical limitation: **Coverlet cannot collect coverage from child processes spawned via `Process.Start()`**, which affects 69 E2E tests (19% of total test suite).

**Key Benefits:**
- ✅ **Child process coverage**: Native support via shared memory (E2E tests coverage expected to increase from 0.09% to ~40-50%)
- ✅ **Crash resilience**: Coverage persists even if child processes crash
- ✅ **Performance**: 80% faster than Coverlet (based on Microsoft benchmarks)
- ✅ **Cross-platform**: Static instrumentation works on all platforms
- ✅ **Official support**: Part of .NET SDK, better long-term maintenance
- ✅ **Automatic merging**: Solution-level `dotnet test` auto-merges all coverage reports

**Expected Coverage Increase:**
- Current: 37.7% (unit tests only)
- Post-migration: 60-70% (unit + E2E tests)

---

## Table of Contents

1. [Current State Analysis](#1-current-state-analysis)
2. [Architecture Overview](#2-architecture-overview)
3. [Migration Strategy](#3-migration-strategy)
4. [Implementation Steps](#4-implementation-steps)
5. [Configuration Details](#5-configuration-details)
6. [Testing & Validation](#6-testing--validation)
7. [CI/CD Integration](#7-cicd-integration)
8. [Rollback Plan](#8-rollback-plan)
9. [Post-Migration Cleanup](#9-post-migration-cleanup)
10. [References](#10-references)

---

## 1. Current State Analysis

### 1.1 Current Coverage Infrastructure

**Tool Stack:**
- `coverlet.collector` (6.0.4) - VSTest integration
- ReportGenerator (global tool) - HTML/text report generation
- Custom MSBuild targets (Directory.Build.targets) - Auto-report generation

**Current Configuration:**
```xml
<!-- .runsettings -->
<DataCollector friendlyName="XPlat Code Coverage">
  <Configuration>
    <Format>cobertura</Format>
    <IncludeDirectory>./artifacts/bin/opentelwatcher/Debug</IncludeDirectory>
    <Exclude>[*tests]*</Exclude>
    <ExcludeByFile>**/Migrations/**,**/*.g.cs,**/*.Designer.cs,**/opentelemetry/Proto/**</ExcludeByFile>
  </Configuration>
</DataCollector>
```

**Test Projects:**
- `unit_tests/` - 286 tests, 37.7% coverage, ~4.5s execution
- `e2e_tests/` - 69 tests, 0.09% coverage (child processes not tracked), ~60s execution

### 1.2 Identified Limitations

**Critical Issues:**
1. **E2E Coverage Gap**: Child processes launched via `Process.Start("opentelwatcher.exe")` are not instrumented
2. **Graceful Shutdown Dependency**: Coverlet requires clean process exit to flush coverage data
3. **Platform-Specific Issues**: `IncludeDirectory` with runtime-loaded assemblies fails on macOS/Linux

**Coverage Blindspots:**
- `WebApplicationHost` (0%) - Only tested by E2E
- `CliApplication` (0%) - Entry point, tested by E2E
- `Program.cs` (0%) - Application startup
- Web UI components (0%) - StatusPage, Swagger UI

---

## 2. Architecture Overview

### 2.1 dotnet-coverage Architecture

**Collection Modes:**

```
┌─────────────────────────────────────────────────────────────┐
│                    dotnet-coverage                          │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌──────────────────┐         ┌──────────────────┐         │
│  │ Dynamic          │         │ Static           │         │
│  │ Instrumentation  │         │ Instrumentation  │         │
│  ├──────────────────┤         ├──────────────────┤         │
│  │ • CLR Profiler   │         │ • Mono.Cecil     │         │
│  │ • Runtime hooks  │         │ • Pre-instrument │         │
│  │ • Win/Linux/macOS│         │ • All platforms  │         │
│  │ • Default mode   │         │ • IIS scenarios  │         │
│  └──────────────────┘         └──────────────────┘         │
│                                                             │
│  ┌──────────────────────────────────────────────┐          │
│  │ Shared Memory Coverage Storage              │          │
│  │ • Survives crashes                          │          │
│  │ • Multi-process safe                        │          │
│  │ • Automatic flushing                        │          │
│  └──────────────────────────────────────────────┘          │
│                                                             │
│  ┌──────────────────────────────────────────────┐          │
│  │ Child Process Tracking                       │          │
│  │ • Automatic instrumentation                  │          │
│  │ • Process tree monitoring                    │          │
│  │ • Unified report generation                  │          │
│  └──────────────────────────────────────────────┘          │
└─────────────────────────────────────────────────────────────┘
```

**Coverage Flow:**

```
┌─────────────────────────────────────────────────────────────┐
│ Test Execution with dotnet-coverage                        │
└─────────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────────┐
│ VSTest starts with Microsoft.CodeCoverage collector        │
└─────────────────────────────────────────────────────────────┘
                          │
        ┌─────────────────┴──────────────────┐
        ▼                                    ▼
┌────────────────┐                  ┌────────────────┐
│ unit_tests.dll │                  │ e2e_tests.dll  │
│ (In-Process)   │                  │ (Multi-Process)│
└────────────────┘                  └────────────────┘
        │                                    │
        │ Tests services directly            │ Spawns child processes
        ▼                                    ▼
┌────────────────┐                  ┌────────────────┐
│ Instrumented   │                  │ Process.Start( │
│ DLLs in memory │                  │ "watcher.exe") │
└────────────────┘                  └────────────────┘
        │                                    │
        │                                    │ Child auto-instrumented
        │                                    ▼
        │                           ┌────────────────┐
        │                           │ watcher.exe    │
        │                           │ (Instrumented) │
        │                           └────────────────┘
        │                                    │
        └─────────┬──────────────────────────┘
                  │ Coverage written to shared memory
                  ▼
        ┌─────────────────────┐
        │ Shared Memory Store │
        └─────────────────────┘
                  │
                  │ On test completion
                  ▼
        ┌─────────────────────┐
        │ coverage.coverage   │
        │ (Binary format)     │
        └─────────────────────┘
                  │
                  │ Convert to cobertura
                  ▼
        ┌─────────────────────┐
        │ coverage.cobertura  │
        │ .xml                │
        └─────────────────────┘
                  │
                  │ ReportGenerator
                  ▼
        ┌─────────────────────┐
        │ HTML/Text Reports   │
        └─────────────────────┘
```

### 2.2 Integration Points

**Test Projects → Microsoft.CodeCoverage:**
- Replace `coverlet.collector` package with `Microsoft.CodeCoverage`
- Update `.runsettings` data collector from `XPlat Code Coverage` to `Code Coverage`
- Enable `CollectFromChildProcesses` for E2E tests

**MSBuild → dotnet-coverage CLI:**
- Replace `dotnet reportgenerator` calls with `dotnet-coverage merge` + `reportgenerator`
- Add conversion step: `.coverage` → `cobertura.xml`

**CI/CD → Unified Coverage:**
- Solution-level `dotnet test` auto-merges all coverage
- Single coverage artifact upload instead of per-project

---

## 3. Migration Strategy

### 3.1 Phased Approach

**Phase 1: Preparation (1-2 hours)**
- Install `dotnet-coverage` global tool
- Create backup branch
- Document current baseline metrics
- Update documentation

**Phase 2: Configuration Migration (2-3 hours)**
- Update NuGet packages (both test projects)
- Migrate `.runsettings` configuration
- Update MSBuild targets
- Configure child process collection

**Phase 3: Testing & Validation (2-4 hours)**
- Run unit tests, verify coverage parity
- Run E2E tests, verify child process coverage
- Generate reports, validate ReportGenerator compatibility
- Performance benchmarking

**Phase 4: Cleanup & Documentation (1 hour)**
- Remove Coverlet artifacts
- Update CLAUDE.md documentation
- Create migration summary report

**Total Estimated Time: 6-10 hours**

### 3.2 Success Criteria

**Must-Have:**
- ✅ All 355 tests pass (286 unit + 69 E2E)
- ✅ Unit test coverage ≥37.7% (no regression)
- ✅ E2E coverage >0.09% (demonstrates child process tracking)
- ✅ Combined coverage >37.7% (shows improvement)
- ✅ Coverage reports generate successfully (HTML + text)
- ✅ Test execution time not significantly degraded (±10%)

**Nice-to-Have:**
- ✅ Combined coverage ≥60% (expected with E2E coverage)
- ✅ Performance improvement in test execution
- ✅ Simplified MSBuild configuration

### 3.3 Risk Assessment

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Coverage data incompatibility | Low | High | Validate with test migration before full rollout |
| Performance degradation | Medium | Medium | Benchmark before/after, can revert via git |
| E2E tests still don't capture coverage | Low | High | Research shows this is supported, verify early |
| CI/CD pipeline breaks | Medium | High | Test locally first, update CI config in same PR |
| ReportGenerator incompatibility | Low | Medium | Cobertura format is standard, well-supported |

---

## 4. Implementation Steps

### 4.1 Prerequisites

```bash
# Install dotnet-coverage global tool (requires .NET 8+ SDK)
dotnet tool install --global dotnet-coverage

# Verify installation
dotnet-coverage --version

# Update local tools manifest (optional, for team consistency)
dotnet tool install --local dotnet-coverage
```

### 4.2 Step 1: Update NuGet Packages

**File: `unit_tests/unit_tests.csproj`**

```xml
<!-- REMOVE -->
<PackageReference Include="coverlet.collector" Version="6.0.4">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>

<!-- ADD -->
<PackageReference Include="Microsoft.CodeCoverage" Version="17.12.0" />
```

**File: `e2e_tests/e2e_tests.csproj`**

```xml
<!-- REMOVE -->
<PackageReference Include="coverlet.collector" Version="6.0.4">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>

<!-- ADD -->
<PackageReference Include="Microsoft.CodeCoverage" Version="17.12.0" />
```

### 4.3 Step 2: Update .runsettings Configuration

**File: `.runsettings`**

**Replace entire DataCollectionRunSettings section:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <!-- Configuration for test execution -->
  <RunConfiguration>
    <ResultsDirectory>./artifacts/test-results</ResultsDirectory>
    <TestSessionTimeout>600000</TestSessionTimeout>
  </RunConfiguration>

  <!-- Data collection settings for code coverage -->
  <DataCollectionRunSettings>
    <DataCollectors>
      <!-- Microsoft Code Coverage collector (replaces Coverlet) -->
      <DataCollector friendlyName="Code Coverage" uri="datacollector://Microsoft/CodeCoverage/2.0">
        <Configuration>
          <CodeCoverage>
            <!-- CRITICAL: Enable child process coverage for E2E tests -->
            <CollectFromChildProcesses>True</CollectFromChildProcesses>

            <!-- Performance optimizations -->
            <UseVerifiableInstrumentation>True</UseVerifiableInstrumentation>
            <AllowLowIntegrityProcesses>True</AllowLowIntegrityProcesses>

            <!-- Instrumentation scope -->
            <ModulePaths>
              <Include>
                <ModulePath>.*opentelwatcher\.dll$</ModulePath>
                <ModulePath>.*opentelwatcher\.exe$</ModulePath>
              </Include>
              <Exclude>
                <!-- Exclude test assemblies -->
                <ModulePath>.*tests\.dll$</ModulePath>
                <ModulePath>.*testhost.*\.dll$</ModulePath>

                <!-- Exclude Microsoft assemblies -->
                <ModulePath>.*microsoft.*\.dll$</ModulePath>
                <ModulePath>.*system.*\.dll$</ModulePath>

                <!-- Exclude third-party packages -->
                <ModulePath>.*nlog.*\.dll$</ModulePath>
                <ModulePath>.*google\.protobuf.*\.dll$</ModulePath>
                <ModulePath>.*fluentassertions.*\.dll$</ModulePath>
                <ModulePath>.*xunit.*\.dll$</ModulePath>
              </Exclude>
            </ModulePaths>

            <!-- Exclude auto-generated code by file path -->
            <Sources>
              <Exclude>
                <Source>.*\\Migrations\\.*</Source>
                <Source>.*\\.g\.cs$</Source>
                <Source>.*\.Designer\.cs$</Source>
                <Source>.*\\opentelemetry\\Proto\\.*</Source>
              </Exclude>
            </Sources>

            <!-- Exclude by attributes -->
            <Attributes>
              <Exclude>
                <Attribute>^System\.Diagnostics\.CodeAnalysis\.ExcludeFromCodeCoverageAttribute$</Attribute>
                <Attribute>^System\.CodeDom\.Compiler\.GeneratedCodeAttribute$</Attribute>
              </Exclude>
            </Attributes>

            <!-- Functions to exclude (if needed) -->
            <Functions>
              <Exclude>
                <!-- Example: Exclude property getters/setters -->
                <!-- <Function>^.*\.get_.*$</Function> -->
                <!-- <Function>^.*\.set_.*$</Function> -->
              </Exclude>
            </Functions>
          </CodeCoverage>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </DataCollectionRunSettings>

  <!-- Logger configuration -->
  <LoggerRunSettings>
    <Loggers>
      <Logger friendlyName="trx" />
      <Logger friendlyName="console">
        <Configuration>
          <Verbosity>normal</Verbosity>
        </Configuration>
      </Logger>
    </Loggers>
  </LoggerRunSettings>
</RunSettings>
```

**Key Configuration Changes:**

| Setting | Coverlet | dotnet-coverage | Notes |
|---------|----------|-----------------|-------|
| Collector name | `XPlat Code Coverage` | `Code Coverage` | ⚠️ Breaking change |
| Format | `<Format>cobertura</Format>` | Binary `.coverage` (convert to XML with `-f xml`) | ❌ Cobertura completely eliminated |
| Child processes | Not supported | `<CollectFromChildProcesses>True</CollectFromChildProcesses>` | ✅ E2E coverage fix |
| Exclusions | `<Exclude>`, `<ExcludeByFile>` | `<ModulePaths>`, `<Sources>`, `<Attributes>` | More granular control |
| Platform support | Cross-platform | Cross-platform (dynamic + static) | Static for IIS scenarios |
| Report format | Cobertura XML → ReportGenerator | Visual Studio XML → ReportGenerator | Microsoft native format |

### 4.4 Step 3: Update MSBuild Targets

**File: `Directory.Build.targets`**

**Replace `GenerateCoverageSummary` target:**

```xml
<!-- Generate coverage summary automatically after tests complete -->
<Target Name="GenerateCoverageSummary" AfterTargets="VSTest" Condition="'$(IsTestProject)' == 'true'">
  <PropertyGroup>
    <!-- dotnet-coverage produces .coverage files (binary format) -->
    <CoverageFilesPattern>$(MSBuildThisFileDirectory)artifacts\test-results\**\*.coverage</CoverageFilesPattern>
    <MergedCoverageFile>$(MSBuildThisFileDirectory)artifacts\coverage-report\merged.coverage</MergedCoverageFile>
    <XmlCoverageFile>$(MSBuildThisFileDirectory)artifacts\coverage-report\coverage.xml</XmlCoverageFile>
    <CoverageOutputPath>$(MSBuildThisFileDirectory)artifacts\coverage-report</CoverageOutputPath>
  </PropertyGroup>

  <!-- Check if coverage files exist -->
  <ItemGroup>
    <CoverageFiles Include="$(CoverageFilesPattern)" />
  </ItemGroup>

  <!-- Step 1: Merge all .coverage files into single binary -->
  <Exec Command="dotnet-coverage merge &quot;$(CoverageFilesPattern)&quot; -o &quot;$(MergedCoverageFile)&quot;"
        Condition="'@(CoverageFiles)' != ''"
        ContinueOnError="true"
        IgnoreExitCode="false"
        ConsoleToMSBuild="true"
        StandardOutputImportance="Low" />

  <!-- Step 2: Convert binary .coverage to Visual Studio XML for ReportGenerator -->
  <!-- Note: Using 'xml' format (Visual Studio XML), NOT Cobertura -->
  <Exec Command="dotnet-coverage merge &quot;$(MergedCoverageFile)&quot; -o &quot;$(XmlCoverageFile)&quot; -f xml"
        Condition="Exists('$(MergedCoverageFile)')"
        ContinueOnError="true"
        IgnoreExitCode="false"
        ConsoleToMSBuild="true"
        StandardOutputImportance="Low" />

  <!-- Step 3: Generate human-readable text summary report -->
  <Exec Command="dotnet reportgenerator &quot;-reports:$(XmlCoverageFile)&quot; &quot;-targetdir:$(CoverageOutputPath)&quot; &quot;-reporttypes:TextSummary&quot;"
        Condition="Exists('$(XmlCoverageFile)')"
        ContinueOnError="true"
        IgnoreExitCode="false"
        ConsoleToMSBuild="true"
        StandardOutputImportance="Low" />

  <Message Text="Coverage report generated at: $(CoverageOutputPath)\Summary.txt"
           Condition="Exists('$(CoverageOutputPath)\Summary.txt')"
           Importance="High" />
</Target>
```

**Key Changes:**
1. **File pattern**: Changed from `**\coverage.cobertura.xml` to `**\*.coverage`
2. **Merge step**: Added `dotnet-coverage merge` to combine binary files
3. **Conversion step**: Convert merged `.coverage` → `coverage.xml` (Visual Studio XML) for ReportGenerator
4. **Format**: Uses Visual Studio XML (`-f xml`), **NOT Cobertura** - completely eliminated
5. **Same output**: Still generates `Summary.txt` via ReportGenerator

### 4.5 Step 4: Update Commands Documentation

**File: `CLAUDE.md`**

**Find and replace coverage section:**

```markdown
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

# Generate HTML coverage report for browsing
dotnet reportgenerator \
  "-reports:./artifacts/coverage-report/coverage.xml" \
  "-targetdir:./artifacts/coverage-report" \
  "-reporttypes:Html"

# Open HTML coverage report
start ./artifacts/coverage-report/index.html  # Windows
open ./artifacts/coverage-report/index.html   # macOS
xdg-open ./artifacts/coverage-report/index.html  # Linux

# Clean all build outputs and artifacts (via MSBuild target)
dotnet clean
```
```

### 4.6 Step 5: Clean and Rebuild

```bash
# Remove all existing build artifacts and coverage files
dotnet clean
rm -rf artifacts/

# Restore packages with new Microsoft.CodeCoverage
dotnet restore

# Full rebuild
dotnet build
```

---

## 5. Configuration Details

### 5.1 Child Process Coverage Configuration

**How `CollectFromChildProcesses` Works:**

When enabled, Microsoft.CodeCoverage:
1. **Monitors process creation** - Intercepts child process spawns via profiler API
2. **Auto-instruments child DLLs** - Injects coverage hooks into child process assemblies
3. **Shared memory IPC** - Parent and child write coverage to same shared memory segment
4. **Unified reporting** - All processes contribute to single `.coverage` file
5. **Crash resilience** - Coverage flushed continuously, survives abnormal termination

**Process Tree Example:**

```
testhost.exe (PID 1234) [Instrumented]
  └─ e2e_tests.dll executing test
       └─ Process.Start("opentelwatcher.exe") (PID 5678) [Auto-instrumented by CodeCoverage]
            ├─ Loads opentelwatcher.dll [Instrumented]
            ├─ Executes CLI commands [Coverage tracked]
            ├─ Starts Kestrel server [Coverage tracked]
            └─ Writes to shared memory → Coverage persisted

Final coverage = Unit tests + E2E test harness + Child process execution
```

### 5.2 Output Formats

**Binary .coverage Format:**
- **Pros**: Fast, compact, Visual Studio compatible, incremental merging
- **Cons**: Not human-readable, requires conversion for ReportGenerator
- **Use**: Primary collection and merge format

**Visual Studio XML Format:**
- **Pros**: ReportGenerator compatible, human-readable (XML), CI/CD friendly
- **Cons**: Larger than binary, slower to generate than binary
- **Use**: Intermediate format for ReportGenerator input

**❌ Cobertura Format:**
- **ELIMINATED**: No longer used in this project
- **Reason**: Unnecessary intermediate format; Visual Studio XML is native and sufficient

**Recommended Workflow:**
1. Collect as `.coverage` (fast, native format)
2. Merge all `.coverage` files (fast binary merge)
3. Convert final merged file to Visual Studio XML with `-f xml` (one-time conversion)
4. Generate HTML reports from `coverage.xml` via ReportGenerator

### 5.3 Exclusion Patterns Reference

**Module Path Patterns (Regex):**

```xml
<ModulePath>.*opentelwatcher\.dll$</ModulePath>  <!-- Include main DLL -->
<ModulePath>.*tests\.dll$</ModulePath>            <!-- Exclude tests -->
<ModulePath>.*microsoft.*\.dll$</ModulePath>      <!-- Exclude Microsoft -->
```

**Source File Patterns (Regex):**

```xml
<Source>.*\\Migrations\\.*</Source>               <!-- Exclude migrations -->
<Source>.*\\.g\.cs$</Source>                      <!-- Exclude generated -->
<Source>.*\.Designer\.cs$</Source>                <!-- Exclude designers -->
```

**Attribute-Based Exclusion:**

```csharp
[ExcludeFromCodeCoverage]
public class GeneratedCode
{
    // This class won't be included in coverage
}
```

---

## 6. Testing & Validation

### 6.1 Pre-Migration Baseline

**Capture current metrics before migration:**

```bash
# Run tests and capture baseline
dotnet test > baseline-test-output.txt

# Save current coverage report
cp ./artifacts/coverage-report/Summary.txt baseline-coverage-summary.txt

# Document metrics
echo "=== BASELINE METRICS ===" > baseline-metrics.txt
echo "Total tests: 355 (286 unit + 69 E2E)" >> baseline-metrics.txt
echo "Unit test coverage: 37.7%" >> baseline-metrics.txt
echo "E2E coverage: 0.09%" >> baseline-metrics.txt
echo "Combined coverage: 37.7%" >> baseline-metrics.txt
echo "Test execution time: ~68 seconds" >> baseline-metrics.txt
```

### 6.2 Post-Migration Validation

**Test Plan:**

| Test Case | Command | Expected Result | Success Criteria |
|-----------|---------|-----------------|------------------|
| Unit tests pass | `dotnet test unit_tests` | All 286 tests pass | Exit code 0 |
| E2E tests pass | `dotnet test e2e_tests` | All 69 tests pass | Exit code 0 |
| Coverage files generated | `ls artifacts/test-results/**/*.coverage` | Files exist | At least 2 .coverage files |
| Coverage merge works | `dotnet-coverage merge ...` | merged.coverage created | File size >0 |
| XML conversion | `dotnet-coverage merge -f xml ...` | coverage.xml created | Valid Visual Studio XML |
| ReportGenerator works | `reportgenerator -reports:coverage.xml...` | HTML + Summary.txt | Files exist |
| Child process coverage | Check WebApplicationHost coverage | Coverage >0% | Previously 0% |
| Coverage increase | Compare Summary.txt | Line coverage >37.7% | Improvement shown |

**Validation Script:**

```bash
#!/bin/bash
# validation.sh - Run post-migration validation

echo "=== POST-MIGRATION VALIDATION ==="

# 1. Run all tests
echo "[1/8] Running all tests..."
dotnet test --no-build || { echo "FAIL: Tests failed"; exit 1; }

# 2. Check coverage files exist
echo "[2/8] Checking .coverage files..."
COVERAGE_COUNT=$(find artifacts/test-results -name "*.coverage" | wc -l)
if [ "$COVERAGE_COUNT" -lt 2 ]; then
  echo "FAIL: Expected at least 2 .coverage files, found $COVERAGE_COUNT"
  exit 1
fi

# 3. Verify merged coverage
echo "[3/8] Verifying merged.coverage..."
if [ ! -f "artifacts/coverage-report/merged.coverage" ]; then
  echo "FAIL: merged.coverage not found"
  exit 1
fi

# 4. Verify Visual Studio XML
echo "[4/8] Verifying coverage.xml..."
if [ ! -f "artifacts/coverage-report/coverage.xml" ]; then
  echo "FAIL: coverage.xml not found"
  exit 1
fi

# 5. Verify Summary.txt
echo "[5/8] Verifying Summary.txt..."
if [ ! -f "artifacts/coverage-report/Summary.txt" ]; then
  echo "FAIL: Summary.txt not found"
  exit 1
fi

# 6. Check coverage percentage
echo "[6/8] Checking coverage percentage..."
COVERAGE=$(grep "Line coverage:" artifacts/coverage-report/Summary.txt | awk '{print $3}' | tr -d '%')
if (( $(echo "$COVERAGE < 37.7" | bc -l) )); then
  echo "FAIL: Coverage regression detected: $COVERAGE% < 37.7%"
  exit 1
fi

# 7. Verify child process coverage (WebApplicationHost should have coverage now)
echo "[7/8] Checking WebApplicationHost coverage..."
if grep -q "OpenTelWatcher.Hosting.WebApplicationHost.*0%" artifacts/coverage-report/Summary.txt; then
  echo "WARNING: WebApplicationHost still shows 0% coverage (child processes may not be tracked)"
fi

# 8. Performance check
echo "[8/8] Performance check (test execution time)..."
# Note: Manual comparison recommended

echo "=== VALIDATION COMPLETE ==="
echo "Review baseline-metrics.txt vs current Summary.txt for detailed comparison"
```

### 6.3 Expected Coverage Changes

**Classes Expected to Gain Coverage (E2E tests):**

| Class | Current | Expected Post-Migration | Reason |
|-------|---------|-------------------------|--------|
| `WebApplicationHost` | 0% | 60-80% | E2E tests start server |
| `CliApplication` | 0% | 70-90% | E2E tests run CLI commands |
| `Program` | 0% | 40-60% | Entry point executed by E2E |
| `IndexModel` (StatusPage) | 0% | 30-50% | StatusPage E2E tests |
| `ApplicationInfoDisplay` | 0% | 50-70% | CLI output utilities |
| `EnvironmentAdapter` | 0% | 40-60% | Used at startup |

**Overall Coverage Projection:**

```
Current:  37.7% (1,582 / 4,186 lines)
Expected: 60-70% (2,500-2,900 / 4,186 lines)
Gain:     +900-1,300 lines covered
```

---

## 7. CI/CD Integration

### 7.1 GitHub Actions Example

**Workflow file: `.github/workflows/test.yml`**

```yaml
name: Test & Coverage

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main ]

jobs:
  test:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v3

    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '10.0.x'

    - name: Install dotnet-coverage
      run: dotnet tool install --global dotnet-coverage

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore

    - name: Test with coverage
      run: dotnet test --no-build --verbosity normal

    # Coverage files are automatically generated by MSBuild targets

    - name: Upload coverage to Codecov
      uses: codecov/codecov-action@v3
      with:
        files: ./artifacts/coverage-report/coverage.xml
        flags: unittests,e2etests
        name: opentelwatcher-coverage

    - name: Upload coverage report as artifact
      uses: actions/upload-artifact@v3
      with:
        name: coverage-report
        path: |
          ./artifacts/coverage-report/Summary.txt
          ./artifacts/coverage-report/coverage.xml

    - name: Comment coverage on PR
      if: github.event_name == 'pull_request'
      uses: actions/github-script@v6
      with:
        script: |
          const fs = require('fs');
          const summary = fs.readFileSync('./artifacts/coverage-report/Summary.txt', 'utf8');
          const coverage = summary.match(/Line coverage: ([\d.]+)%/)[1];

          await github.rest.issues.createComment({
            issue_number: context.issue.number,
            owner: context.repo.owner,
            repo: context.repo.repo,
            body: `## Code Coverage Report\n\nLine Coverage: **${coverage}%**\n\n<details><summary>Full Report</summary>\n\n\`\`\`\n${summary}\n\`\`\`\n</details>`
          });
```

### 7.2 Azure DevOps Example

**azure-pipelines.yml:**

```yaml
trigger:
  - main
  - develop

pool:
  vmImage: 'ubuntu-latest'

steps:
- task: UseDotNet@2
  inputs:
    packageType: 'sdk'
    version: '10.x'

- script: dotnet tool install --global dotnet-coverage
  displayName: 'Install dotnet-coverage'

- script: dotnet restore
  displayName: 'Restore dependencies'

- script: dotnet build --no-restore
  displayName: 'Build'

- script: dotnet test --no-build --logger trx --collect:"Code Coverage"
  displayName: 'Run tests with coverage'

# Publish test results
- task: PublishTestResults@2
  inputs:
    testResultsFormat: 'VSTest'
    testResultsFiles: '**/artifacts/test-results/**/*.trx'

# Publish code coverage (using binary .coverage or convert to XML)
- task: PublishCodeCoverageResults@2
  inputs:
    codeCoverageTool: 'coverage'  # Visual Studio coverage tool
    summaryFileLocation: '**/artifacts/coverage-report/merged.coverage'
    reportDirectory: '**/artifacts/coverage-report'

# Alternative: If using XML, configure task for generic XML parser
# - task: PublishCodeCoverageResults@2
#   inputs:
#     codeCoverageTool: 'cobertura'  # Generic XML parser (works with VS XML too)
#     summaryFileLocation: '**/artifacts/coverage-report/coverage.xml'
```

### 7.3 Local Development Workflow

**Quick commands for developers:**

```bash
# Standard test run (auto-generates coverage)
dotnet test

# View coverage summary in terminal
cat artifacts/coverage-report/Summary.txt

# Generate and open HTML report
dotnet reportgenerator \
  "-reports:artifacts/coverage-report/coverage.cobertura.xml" \
  "-targetdir:artifacts/coverage-report" \
  "-reporttypes:Html" && \
  open artifacts/coverage-report/index.html

# Run only unit tests (faster iteration)
dotnet test unit_tests

# Run only E2E tests
dotnet test e2e_tests

# Clean coverage data between runs
rm -rf artifacts/test-results artifacts/coverage-report
```

---

## 8. Rollback Plan

### 8.1 Rollback Triggers

**When to rollback:**
- ❌ Tests fail after migration (>1 failure)
- ❌ Coverage drops below baseline (37.7%)
- ❌ Test execution time increases >50% (>102 seconds)
- ❌ Child process coverage still not working (WebApplicationHost remains 0%)
- ❌ ReportGenerator fails to generate reports
- ❌ Critical bugs discovered during validation

### 8.2 Rollback Procedure

**Step 1: Revert Git Changes**

```bash
# If changes not committed
git checkout .
git clean -fd

# If changes committed
git revert <commit-hash>

# If on feature branch
git reset --hard origin/main
```

**Step 2: Restore Coverlet Packages**

```bash
# Restore original package references (git will restore .csproj files)
dotnet restore

# Rebuild
dotnet clean
dotnet build
```

**Step 3: Verify Rollback**

```bash
# Run tests
dotnet test

# Verify coverage reports
cat artifacts/coverage-report/Summary.txt

# Should show: Line coverage: 37.7%
```

**Step 4: Document Issue**

Create GitHub issue documenting:
- What failed during migration
- Error messages / logs
- System configuration (OS, .NET version)
- Suggested investigation steps

### 8.3 Backup Strategy

**Before starting migration:**

```bash
# Create backup branch
git checkout -b backup/pre-dotnet-coverage-migration

# Save current state
git add -A
git commit -m "Backup before dotnet-coverage migration"
git push -u origin backup/pre-dotnet-coverage-migration

# Return to working branch
git checkout main
git checkout -b feature/migrate-to-dotnet-coverage
```

**Artifacts to preserve:**

```bash
# Save baseline coverage report
mkdir -p migration-backup
cp artifacts/coverage-report/Summary.txt migration-backup/baseline-summary.txt
cp .runsettings migration-backup/runsettings-coverlet.xml
cp Directory.Build.targets migration-backup/Directory.Build.targets-original

# Commit backups
git add migration-backup/
git commit -m "Add migration baseline artifacts"
```

---

## 9. Post-Migration Cleanup

### 9.1 Remove Coverlet Artifacts

**After successful migration and validation:**

```bash
# Remove Coverlet-related files (if any exist)
find . -name "coverage.json" -delete
find . -name "coverage.info" -delete

# Clean old coverage reports
rm -rf artifacts/test-results/**/coverage.cobertura.xml

# Note: .coverage files are the new standard
```

### 9.2 Update Documentation

**Files to update:**

1. **README.md** - Update coverage badges, mention dotnet-coverage
2. **CLAUDE.md** - Update test coverage section (done in Step 4.5)
3. **TESTING.md** - Add dotnet-coverage workflow examples
4. **.gitignore** - Add `.coverage` pattern if needed

**Example .gitignore addition:**

```gitignore
# Code coverage (Microsoft native formats only)
artifacts/coverage-report/
artifacts/test-results/
*.coverage      # Binary coverage files
*.xml           # Visual Studio XML (in coverage-report only)

# NOTE: NO Cobertura patterns - format completely eliminated
```

### 9.3 Team Communication

**Migration announcement template:**

```markdown
## ✅ Migration Complete: Coverlet → dotnet-coverage

We've successfully migrated our code coverage infrastructure from Coverlet to
Microsoft's dotnet-coverage (Microsoft.CodeCoverage).

### What Changed
- NuGet package: `coverlet.collector` → `Microsoft.CodeCoverage`
- Coverage files: `coverage.cobertura.xml` → `*.coverage` (binary) + `coverage.xml` (Visual Studio XML)
- Format: **Cobertura completely eliminated** - using Microsoft native formats only
- Child process coverage: Now supported (E2E tests tracked)

### Benefits
- **E2E Coverage**: Child processes spawned by E2E tests now contribute to coverage
- **Overall Coverage**: Increased from 37.7% to XX% (include actual number)
- **Performance**: Test execution time improved by XX% (if applicable)
- **Reliability**: Coverage survives process crashes

### Developer Impact
- Commands remain the same: `dotnet test` still works
- Coverage reports still at `artifacts/coverage-report/Summary.txt`
- HTML reports: `artifacts/coverage-report/index.html`

### New Coverage Breakdown
- Unit Tests: 37.7% (unchanged)
- E2E Tests: XX% (was 0.09%)
- Combined: XX% (was 37.7%)

### Questions?
See [newcov.md](newcov.md) for full migration details.
```

### 9.4 Create Migration Summary Report

**Template: `migration-report.md`**

```markdown
# dotnet-coverage Migration Report

**Date:** YYYY-MM-DD
**Performed By:** [Name]
**Duration:** X hours
**Status:** ✅ Success / ⚠️ Partial / ❌ Failed

## Metrics Comparison

| Metric | Before (Coverlet) | After (dotnet-coverage) | Change |
|--------|-------------------|-------------------------|--------|
| Total Tests | 355 | 355 | - |
| Passing Tests | 355 | 355 | - |
| Unit Test Coverage | 37.7% | XX% | +/-X% |
| E2E Test Coverage | 0.09% | XX% | +XX% |
| Combined Coverage | 37.7% | XX% | +XX% |
| Test Execution Time | 68s | XXs | +/-Xs |
| Coverage File Count | 4 | 2 | -2 |
| Lines Covered | 1,582 | X,XXX | +XXX |

## Key Achievements
- ✅ [Achievement 1]
- ✅ [Achievement 2]
- ✅ [Achievement 3]

## Issues Encountered
- [Issue 1 and resolution]
- [Issue 2 and resolution]

## Lessons Learned
- [Lesson 1]
- [Lesson 2]

## Next Steps
- [ ] Monitor coverage trends over next week
- [ ] Update CI/CD coverage thresholds
- [ ] Add coverage requirements to PR template
```

### 9.5 Final Cleanup: Complete Cobertura Elimination

**⚠️ CRITICAL: Cobertura format is COMPLETELY ELIMINATED from this project.**

This is not about removing "legacy" Cobertura - Cobertura is **not used at all** in the new architecture. This section ensures zero Cobertura references remain anywhere in the codebase.

**Migration Principle:** Use only Microsoft native formats (`.coverage` binary + Visual Studio XML) throughout the entire pipeline.

#### 9.5.1 Audit Cobertura References

**Search for remaining Cobertura mentions:**

```bash
# Find all Cobertura references in codebase
grep -r "cobertura" --include="*.md" --include="*.xml" --include="*.cs" --include="*.csproj" --include="*.props" --include="*.targets" .

# Find in documentation
grep -r "coverage\.cobertura\.xml" --include="*.md" .

# Find in configuration files
grep -r "Format.*cobertura" --include="*.xml" .
```

#### 9.5.2 Remove ALL Cobertura from .gitignore

**File: `.gitignore`**

```bash
# REMOVE these Cobertura-specific patterns (delete if present):
# coverage.cobertura.xml      ← DELETE
# *.cobertura.xml              ← DELETE
# **/coverage.cobertura.xml    ← DELETE
# cobertura/                   ← DELETE

# KEEP these Microsoft native format patterns:
artifacts/coverage-report/     # Contains coverage.xml (Visual Studio XML)
artifacts/test-results/        # Contains *.coverage (binary)
*.coverage                     # Binary coverage files
```

**Critical:** There is NO `coverage.cobertura.xml` file anymore - period. The conversion uses `coverage.xml` (Visual Studio XML format).

#### 9.5.3 Update Documentation - Remove Cobertura References

**Files to audit and update:**

**1. README.md**

```bash
# Find Cobertura mentions
grep -i "cobertura" README.md

# Replace with dotnet-coverage terminology
# OLD: "Coverage reports in Cobertura format"
# NEW: "Coverage reports in binary .coverage format (auto-converted to Cobertura for ReportGenerator)"
```

**2. CLAUDE.md** (already updated in Step 4.5, but verify)

```bash
# Ensure no standalone Cobertura references remain
grep -i "cobertura" CLAUDE.md

# Should only appear in context of:
# - dotnet-coverage merge -f cobertura (conversion command)
# - reportgenerator input format (intermediate use)
```

**3. TESTING.md** (if exists)

Remove any sections like:
- ~~"Generating Cobertura Reports"~~
- ~~"Cobertura Format Configuration"~~
- ~~"coverlet.runsettings Cobertura format"~~

**4. CI/CD Configuration Files**

**GitHub Actions (`.github/workflows/*.yml`):**

```yaml
# REMOVE explicit Cobertura format specifications
# OLD:
# - dotnet test --collect:"XPlat Code Coverage;Format=Cobertura"

# NEW (handled automatically by .runsettings and MSBuild):
# - dotnet test
```

**Azure Pipelines (`azure-pipelines.yml`):**

```yaml
# REMOVE:
# - task: PublishCodeCoverageResults@2
#   inputs:
#     codeCoverageTool: 'cobertura'  # OLD

# KEEP (uses auto-generated Cobertura XML):
- task: PublishCodeCoverageResults@2
  inputs:
    codeCoverageTool: 'cobertura'
    summaryFileLocation: '**/artifacts/coverage-report/coverage.cobertura.xml'
    # Note: This is the converted intermediate file, not direct output
```

#### 9.5.4 Remove Obsolete Scripts

**Check for custom coverage scripts:**

```bash
# Find shell scripts with Cobertura references
find . -name "*.sh" -o -name "*.ps1" | xargs grep -l "cobertura"

# Common obsolete scripts to remove:
# - scripts/generate-cobertura-report.sh
# - .build/coverage-cobertura.ps1
# - tools/convert-to-cobertura.sh
```

**Example obsolete patterns to remove:**

```bash
# OBSOLETE: Manual Cobertura generation scripts
rm -f scripts/generate-cobertura-report.sh

# OBSOLETE: Coverlet-specific conversion utilities
rm -f .build/merge-cobertura.ps1
```

#### 9.5.5 Clean Migration Artifacts

**Remove migration backup files after 30 days:**

```bash
# After verifying migration stability (30+ days), remove backups
rm -rf migration-backup/

# Remove baseline metrics files
rm -f baseline-*.txt

# Remove migration report (archive to documentation folder first)
mv migration-report.md docs/archive/migration-report-dotnet-coverage-YYYY-MM-DD.md
```

#### 9.5.6 Update Code Comments

**Search for Cobertura mentions in code comments:**

```bash
# Find code comments referencing Cobertura
grep -r "// .*[Cc]obertura" --include="*.cs" .
grep -r "/// .*[Cc]obertura" --include="*.cs" .
```

**Example updates:**

```csharp
// REMOVE or UPDATE:

// OLD:
/// <summary>
/// Generates coverage report in Cobertura format for CI/CD integration
/// </summary>

// NEW:
/// <summary>
/// Generates coverage report (auto-converted to Cobertura for ReportGenerator)
/// </summary>
```

#### 9.5.7 Verify No Hard-Coded Cobertura Paths

**Check for hard-coded file paths:**

```bash
# Find hard-coded Cobertura XML paths
grep -r "coverage\.cobertura\.xml" --include="*.cs" --include="*.csproj" --include="*.props" --include="*.targets" .
```

**Common locations to check:**

- MSBuild properties: `<CoberturaReportPath>`
- Test utilities: File path constants
- CI/CD scripts: Report upload paths

**Acceptable references (keep these):**

```xml
<!-- Directory.Build.targets - This is OK (intermediate file) -->
<CoberturaCoverageFile>$(MSBuildThisFileDirectory)artifacts\coverage-report\coverage.cobertura.xml</CoberturaCoverageFile>
```

```bash
# CI/CD - This is OK (ReportGenerator input)
reportgenerator -reports:coverage.cobertura.xml
```

#### 9.5.8 Final Verification Checklist

After cleanup, verify the following:

```bash
# 1. No "XPlat Code Coverage" references (old collector)
! grep -r "XPlat Code Coverage" --include="*.xml" --include="*.md" .

# 2. No standalone Cobertura format specifications
! grep -r "Format.*cobertura" --include="*.runsettings" --include="*.xml" .

# 3. No coverlet.collector references
! grep -r "coverlet\.collector" --include="*.csproj" .

# 4. All tests still pass
dotnet test

# 5. Coverage reports still generate
ls -la artifacts/coverage-report/Summary.txt
ls -la artifacts/coverage-report/coverage.cobertura.xml  # Intermediate file, OK

# 6. Documentation updated
git diff README.md CLAUDE.md TESTING.md
```

#### 9.5.9 Cleanup Summary

**✅ What We Use (Microsoft Native Formats Only):**

- `.coverage` (binary) - Primary collection and merge format
- `coverage.xml` (Visual Studio XML) - ReportGenerator input format
- `dotnet-coverage merge -f xml` - XML conversion command

**❌ What We Completely Eliminate (Zero Cobertura):**

- ❌ All `cobertura` format references (`-f cobertura`)
- ❌ All `*.cobertura.xml` file references
- ❌ All `coverage.cobertura.xml` file paths
- ❌ Coverlet-specific `<Format>cobertura</Format>` in .runsettings
- ❌ Explicit Cobertura format specifications in test commands
- ❌ Documentation sections about "Cobertura" anything
- ❌ Custom scripts for Cobertura generation
- ❌ Hard-coded Cobertura paths (ALL of them)
- ❌ `.cobertura.xml` gitignore patterns
- ❌ CI/CD Cobertura tool specifications (use Visual Studio XML parser)
- ❌ Any code comments mentioning Cobertura

**Verification Command:**

```bash
# After cleanup, this should return ZERO results
grep -ri "cobertura" \
  --include="*.md" \
  --include="*.xml" \
  --include="*.cs" \
  --include="*.csproj" \
  --include="*.props" \
  --include="*.targets" \
  --include="*.yml" \
  --include="*.yaml" \
  --include="*.sh" \
  --include="*.ps1" \
  .

# Expected: No matches found
```

**Rationale:**

Cobertura is a third-party format created for Java projects. Microsoft provides native formats (`.coverage` and Visual Studio XML) that:
1. Work natively with Microsoft.CodeCoverage
2. Are fully supported by ReportGenerator
3. Avoid unnecessary format conversions
4. Provide better Visual Studio integration
5. Are the official Microsoft standard

**There is ZERO technical reason to use Cobertura in a .NET project with dotnet-coverage.**

---

## 10. References

### 10.1 Official Documentation

- [dotnet-coverage Documentation](https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage)
- [Microsoft.CodeCoverage NuGet Package](https://www.nuget.org/packages/Microsoft.CodeCoverage)
- [Customizing Code Coverage Analysis](https://learn.microsoft.com/en-us/visualstudio/test/customizing-code-coverage-analysis)
- [Configure Unit Tests with .runsettings](https://learn.microsoft.com/en-us/visualstudio/test/configure-unit-tests-by-using-a-dot-runsettings-file)
- [What's New in Code Coverage Tooling](https://devblogs.microsoft.com/dotnet/whats-new-in-our-code-coverage-tooling/)

### 10.2 GitHub Issues & Discussions

- [Coverlet #1260 - CollectFromChildProcesses Support Request](https://github.com/coverlet-coverage/coverlet/issues/1260)
- [Coverlet #1079 - ASP.NET Application Coverage](https://github.com/coverlet-coverage/coverlet/issues/1079)
- [Microsoft CodeCoverage Repository](https://github.com/microsoft/codecoverage)

### 10.3 Community Resources

- [A Guide to Code Coverage Tools for C# in 2025](https://blog.ndepend.com/guide-code-coverage-tools/)
- [Code Coverage in .NET :: my tech ramblings](https://www.mytechramblings.com/posts/code-coverage-in-dotnet/)
- [Computing Code Coverage for a .NET Project - Meziantou's Blog](https://www.meziantou.net/computing-code-coverage-for-a-dotnet-project.htm)

### 10.4 Related Tools

- [ReportGenerator](https://github.com/danielpalme/ReportGenerator) - Coverage report generation
- [Codecov](https://about.codecov.io/) - Coverage tracking and visualization
- [Coveralls](https://coveralls.io/) - Alternative coverage tracking

---

## Appendix A: Troubleshooting

### Common Issues and Solutions

#### Issue 1: "Child processes not showing coverage"

**Symptoms:**
- WebApplicationHost still shows 0% coverage after migration
- E2E coverage identical to Coverlet (0.09%)

**Diagnosis:**
```bash
# Check if CollectFromChildProcesses is enabled
grep -A 5 "CollectFromChildProcesses" .runsettings

# Verify coverage file contains multiple processes
dotnet-coverage merge artifacts/test-results/**/*.coverage -o test.coverage
# File should be larger than unit tests alone
```

**Solutions:**
1. Ensure `.runsettings` has `<CollectFromChildProcesses>True</CollectFromChildProcesses>`
2. Verify using `Code Coverage` collector, not `XPlat Code Coverage`
3. Check E2E tests use `/api/stop` endpoint for graceful shutdown (not `Process.Kill()`)
4. Try static instrumentation on non-Windows platforms

#### Issue 2: "dotnet-coverage command not found"

**Symptoms:**
```
'dotnet-coverage' is not recognized as an internal or external command
```

**Solutions:**
```bash
# Install globally
dotnet tool install --global dotnet-coverage

# OR add to PATH if already installed
export PATH="$PATH:$HOME/.dotnet/tools"

# Verify installation
dotnet tool list --global | grep dotnet-coverage
```

#### Issue 3: "Coverage files not being generated"

**Symptoms:**
- No `.coverage` files in `artifacts/test-results/`
- Tests pass but no coverage collected

**Diagnosis:**
```bash
# Check if collector is loaded
dotnet test --collect:"Code Coverage" --diag:diag.log

# Inspect diagnostic log
cat diag.log | grep -i "code coverage"
```

**Solutions:**
1. Verify `Microsoft.CodeCoverage` package installed in test projects
2. Check `.runsettings` file path is correct
3. Ensure `--no-build` not used without prior `dotnet build`
4. Try explicit settings: `dotnet test --settings .runsettings`

#### Issue 4: "Cobertura conversion fails"

**Symptoms:**
```
Error: Unable to convert .coverage to cobertura format
```

**Solutions:**
```bash
# Update dotnet-coverage
dotnet tool update --global dotnet-coverage

# Try XML format first
dotnet-coverage merge merged.coverage -o coverage.xml -f xml

# Convert XML to Cobertura via ReportGenerator
reportgenerator -reports:coverage.xml -targetdir:. -reporttypes:Cobertura
```

#### Issue 5: "Tests hang during coverage collection"

**Symptoms:**
- E2E tests timeout
- Process tree never terminates

**Solutions:**
1. Check for background threads not respecting cancellation tokens
2. Increase test timeout: `<TestSessionTimeout>600000</TestSessionTimeout>`
3. Disable coverage for specific tests to isolate issue
4. Review E2E test cleanup code (ensure all processes shut down)

---

## Appendix B: Performance Tuning

### Optimization Strategies

#### Strategy 1: Selective Coverage Collection

**Scenario:** Tests slow down significantly with coverage enabled

**Solution:** Create separate runsettings for unit vs E2E tests

**unit.runsettings:**
```xml
<DataCollector friendlyName="Code Coverage">
  <Configuration>
    <CodeCoverage>
      <!-- Disable child process tracking for unit tests (not needed) -->
      <CollectFromChildProcesses>False</CollectFromChildProcesses>

      <!-- Enable only for unit test project -->
      <ModulePaths>
        <Include>
          <ModulePath>.*opentelwatcher\.dll$</ModulePath>
        </Include>
      </ModulePaths>
    </CodeCoverage>
  </Configuration>
</DataCollector>
```

**Usage:**
```bash
# Fast unit test iteration (no child process overhead)
dotnet test unit_tests --settings unit.runsettings

# Full coverage including E2E
dotnet test --settings .runsettings
```

#### Strategy 2: Static Instrumentation for CI/CD

**Scenario:** CI/CD builds are slow due to dynamic instrumentation overhead

**Solution:** Pre-instrument assemblies

```bash
# Build first
dotnet build --configuration Release

# Instrument assemblies statically
dotnet-coverage instrument ./artifacts/bin/opentelwatcher/Release/opentelwatcher.dll

# Run tests against instrumented assemblies
dotnet test --no-build --configuration Release
```

#### Strategy 3: Parallel Test Execution

**Enable parallel execution in .runsettings:**

```xml
<RunConfiguration>
  <MaxCpuCount>0</MaxCpuCount> <!-- 0 = use all available cores -->
</RunConfiguration>

<MSTest>
  <Parallelize>
    <Workers>0</Workers>  <!-- 0 = use all cores -->
    <Scope>MethodLevel</Scope>
  </Parallelize>
</MSTest>
```

**Note:** E2E tests may need sequential execution due to port binding conflicts.

---

## Appendix C: Migration Checklist

### Pre-Migration

- [ ] Review migration plan (this document)
- [ ] Backup current branch (`git checkout -b backup/pre-migration`)
- [ ] Document baseline metrics (coverage %, test time)
- [ ] Install `dotnet-coverage` global tool
- [ ] Verify .NET SDK version (≥8.0)
- [ ] Communicate migration plan to team

### Migration Execution

- [ ] Create feature branch (`git checkout -b feature/migrate-to-dotnet-coverage`)
- [ ] Update `unit_tests.csproj` (remove Coverlet, add Microsoft.CodeCoverage)
- [ ] Update `e2e_tests.csproj` (remove Coverlet, add Microsoft.CodeCoverage)
- [ ] Update `.runsettings` (switch to Code Coverage collector)
- [ ] Update `Directory.Build.targets` (add dotnet-coverage merge steps)
- [ ] Update `CLAUDE.md` documentation
- [ ] Run `dotnet clean && dotnet restore && dotnet build`

### Validation

- [ ] Run unit tests: `dotnet test unit_tests --no-build`
- [ ] Verify all 286 tests pass
- [ ] Check unit test coverage (should be ≥37.7%)
- [ ] Run E2E tests: `dotnet test e2e_tests --no-build`
- [ ] Verify all 69 tests pass
- [ ] Check E2E coverage (should be >0.09%)
- [ ] Verify `.coverage` files generated
- [ ] Verify Visual Studio XML conversion works (`coverage.xml` created)
- [ ] Verify ReportGenerator produces reports (from `coverage.xml`)
- [ ] Check WebApplicationHost coverage (should be >0%)
- [ ] Run full test suite: `dotnet test`
- [ ] Compare execution time vs baseline
- [ ] Review `Summary.txt` for overall coverage increase

### Post-Migration

- [ ] Run validation script (`./validation.sh`)
- [ ] Create migration report (`migration-report.md`)
- [ ] Commit changes with descriptive message
- [ ] Push feature branch
- [ ] Create pull request
- [ ] Update CI/CD pipeline (if needed)
- [ ] Merge to main after approval
- [ ] Announce migration to team
- [ ] Monitor for issues over next 1-2 weeks
- [ ] Close this migration task

### Final Cleanup (After 1-2 Weeks Stability)

**⚠️ Execute these steps only after confirming migration is stable:**

- [ ] Audit ALL Cobertura references: `grep -ri "cobertura" --include="*.md" --include="*.xml" --include="*.yml" .`
- [ ] **VERIFY ZERO COBERTURA MATCHES** - Should return no results
- [ ] Remove ALL Cobertura .gitignore patterns (no exceptions)
- [ ] Update README.md - Remove all Cobertura mentions
- [ ] Update CLAUDE.md - Remove all Cobertura mentions
- [ ] Update TESTING.md - Remove all Cobertura mentions
- [ ] Clean CI/CD - Verify using Visual Studio XML, not Cobertura
- [ ] Remove obsolete scripts (any file with "cobertura" in name or content)
- [ ] Clean migration artifacts: `rm -rf migration-backup/`, `rm baseline-*.txt`
- [ ] Archive migration report to docs/archive/
- [ ] Update code comments - Remove ANY Cobertura references
- [ ] Verify no hard-coded paths: `coverage.cobertura.xml` should not exist ANYWHERE
- [ ] Run verification: No "XPlat Code Coverage", no Coverlet, **no Cobertura**
- [ ] Final check: `grep -ri "cobertura" . | wc -l` returns 0
- [ ] Commit cleanup: `git commit -m "Complete Cobertura elimination - use only Microsoft native formats"`
- [ ] Update document history in newcov.md

### Rollback (if needed)

- [ ] Identify rollback trigger (see Section 8.1)
- [ ] Document failure reason
- [ ] Revert Git changes
- [ ] Restore Coverlet packages
- [ ] Verify tests pass with Coverlet
- [ ] Create GitHub issue for investigation
- [ ] Schedule retry after root cause addressed

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-11-17 | AI Assistant | Initial migration plan created |
| 1.1 | 2025-11-17 | AI Assistant | Added Section 9.5: Final Cleanup - Remove All Cobertura Traces |
| 2.0 | 2025-11-17 | AI Assistant | **BREAKING**: Complete Cobertura elimination - use only Visual Studio XML format (`-f xml`), zero Cobertura anywhere |

---

**END OF MIGRATION PLAN**

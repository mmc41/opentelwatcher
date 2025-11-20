# Test Logging Guidelines

## Overview

This document establishes consistent logging practices for unit tests and E2E tests in the OpenTelWatcher project. Following these guidelines ensures:
- Consistent log verbosity across tests
- Easier debugging of test failures
- Clear audit trail of test execution
- Appropriate log levels for different scenarios

## Log Level Guidelines

### LogInformation
**Use for**: Test lifecycle events and key checkpoints

**Examples**:
```csharp
_logger.LogInformation("Testing {Endpoint} endpoint returns healthy status", endpoint);
_logger.LogInformation("Found {Count} error file(s)", errorFiles.Length);
_logger.LogInformation("Health status: {Status}", status);
```

**Guidelines**:
- Test start: Always log what the test is doing
- Test end: Log final state or result
- Key checkpoints: Major state changes or important assertions
- Results: Actual values retrieved from system under test

### LogDebug
**Use for**: Detailed step-by-step actions and internal state

**Examples**:
```csharp
_logger.LogDebug("Sending trace request with error status");
_logger.LogDebug("Reading trace file: {FilePath}", latestFile);
_logger.LogDebug("Created test directory: {TestOutputDir}", TestOutputDir);
```

**Guidelines**:
- Detailed actions: Individual API calls, file operations
- Internal state: Variable values during test execution
- Expected failures: Failures during retry loops or startup sequences

### LogWarning
**Use for**: Unexpected but handled situations

**Examples**:
```csharp
_logger.LogWarning("Health check failed during startup, will retry");
_logger.LogWarning("Failed to cleanup test directory {TestOutputDir}", TestOutputDir);
```

**Guidelines**:
- Degraded conditions: Test still passes but something unexpected happened
- Cleanup failures: Resource cleanup failed but test succeeded
- Retry scenarios: Operation failed but will be retried

### LogError
**Use for**: Test failures and critical issues

**Examples**:
```csharp
_logger.LogError(ex, "Fatal error starting server on port {Port}", port);
_logger.LogError("Test timeout exceeded: operation took {Duration}ms", elapsed);
```

**Guidelines**:
- Test failures: Log before assertion fails
- Unhandled exceptions: Exceptions that indicate test bugs
- Critical state: System in unexpected state that causes test failure

## Test Logging Patterns

### E2E Test Standard Pattern

```csharp
[Fact]
public async Task EndpointName_Scenario_ExpectedBehavior()
{
    // Arrange
    _logger.LogInformation("Testing endpoint behavior with specific scenario");

    // Arrange logging - LogDebug for setup steps
    _logger.LogDebug("Creating test data with {Count} items", count);

    // Act
    _logger.LogDebug("Sending request to {Endpoint}", endpoint);
    var response = await _client.GetAsync(endpoint);

    // Assert logging - LogInformation for results
    _logger.LogInformation("Response status: {StatusCode}", response.StatusCode);

    // Detailed verification - LogDebug
    _logger.LogDebug("Verifying response contains expected data");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
}
```

### Unit Test Standard Pattern

```csharp
[Fact]
public void MethodName_Scenario_ExpectedBehavior()
{
    // Arrange - LogDebug for setup
    _logger.LogDebug("Testing {Method} with {Scenario}", methodName, scenario);

    // Act - Usually no logging needed for unit tests (fast operations)
    var result = service.Method(input);

    // Assert - Log if helpful for debugging
    result.Should().Be(expected);
}
```

**Note**: Unit tests typically need less logging than E2E tests since they're faster and more isolated.

## Common Anti-Patterns to Avoid

### ❌ Too Verbose (Don't Do This)
```csharp
_logger.LogInformation("Entering test method");
_logger.LogInformation("Creating client");
_logger.LogInformation("Setting up test data");
_logger.LogInformation("Calling method");
_logger.LogInformation("Method returned");
_logger.LogInformation("Verifying result");
```

**Problem**: Excessive noise makes logs hard to read

### ✅ Appropriate Verbosity (Do This)
```csharp
_logger.LogInformation("Testing method behavior with scenario");
_logger.LogDebug("Calling {Method} with {Input}", method, input);
_logger.LogInformation("Result: {Output}", output);
```

### ❌ String Interpolation (Don't Do This)
```csharp
_logger.LogInformation($"Found {count} files"); // ❌ Not structured
```

**Problem**: Loses structured logging benefits

### ✅ Structured Logging (Do This)
```csharp
_logger.LogInformation("Found {Count} files", count); // ✅ Structured
```

### ❌ No Context (Don't Do This)
```csharp
_logger.LogInformation("Test passed");
```

**Problem**: Doesn't identify which test or what passed

### ✅ Clear Context (Do This)
```csharp
_logger.LogInformation("Successfully verified {Endpoint} returns {StatusCode}", endpoint, statusCode);
```

## File-Specific Guidelines

### PollingHelpers
- Always pass logger to PollingHelpers methods
- Always provide conditionDescription for meaningful timeout logs
- Log when polling starts and when condition is met

```csharp
var result = await PollingHelpers.WaitForConditionAsync(
    condition: () => checkSomething(),
    timeoutMs: 2000,
    cancellationToken: token,
    logger: _logger, // Always provide
    conditionDescription: "server to become healthy"); // Always provide
```

### FileBasedTestBase
- Constructor logs directory creation at Debug level
- Cleanup logs failures at Warning level (not Error - cleanup is optional)

### Test Fixtures
- Log fixture initialization (Information level)
- Log fixture disposal (Debug level)
- Capture subprocess output to test logs

## When to Use Which Level

| Scenario | Level | Example |
|----------|-------|---------|
| Test starting | Information | `Testing endpoint returns 200` |
| API call made | Debug | `Calling POST /api/endpoint` |
| Response received | Information | `Response: 200 OK` |
| File operation | Debug | `Reading file: path.txt` |
| Assertion setup | Debug | `Verifying count equals 5` |
| Polling started | Debug | `Waiting for condition: X` |
| Polling succeeded | Debug | `Condition met after 50ms` |
| Expected retry | Debug | `Health check failed, retrying` |
| Unexpected situation | Warning | `Cleanup failed but continuing` |
| Test failure | Error | `Fatal error: server crashed` |

## Checklist for Test Authors

Before submitting a test with logging:

- [ ] Test start logged at Information level
- [ ] Key checkpoints logged at Information level
- [ ] Detailed steps logged at Debug level
- [ ] All logging uses structured parameters (not string interpolation)
- [ ] PollingHelpers calls include logger and conditionDescription
- [ ] No excessive logging (>5 log statements in simple test)
- [ ] Log messages include relevant context (what, why, parameters)
- [ ] Exceptions logged with exception object: `LogError(ex, "message")`

## Examples from Codebase

### Good Example: ErrorFilesTests.cs
```csharp
_logger.LogInformation("Creating trace with error status");
_logger.LogDebug("Sending trace request with error status");
_logger.LogInformation("Checking for error files in {OutputDirectory}", outputDirectory);
_logger.LogInformation("Found {ErrorFileCount} error file(s)", errorFiles.Length);
```

**Why it's good**:
- Test start at Information
- Detailed action at Debug
- Results at Information with structured parameters

### Good Example: OpenTelWatcherServerFixtureBase.cs
```csharp
logger.LogInformation("Starting OpenTelWatcher server on port {Port}", port);
logger.LogDebug("Waiting for health check...");
logger.LogInformation("Server healthy and ready on port {Port}", port);
```

**Why it's good**:
- Lifecycle events at Information
- Waiting/polling at Debug
- Success confirmation at Information

## Related Documentation

- See `NLog.config` for global logging configuration
- See `CLAUDE.md` for general logging standards (production code)
- See `TESTING.md` for general testing guidelines

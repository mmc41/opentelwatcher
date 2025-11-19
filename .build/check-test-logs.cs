#!/usr/bin/dotnet run

// Workaround for getting dotnet publish to work without missing linker complaints.
#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PublishSingleFile=true

#:sdk Microsoft.NET.Sdk

// Check test logs for warnings and errors
// This script is called automatically after test execution by MSBuild
// Usage: dotnet run check-test-logs.cs [LogDirectory]

using System.Text.RegularExpressions;

// Parse command-line arguments
string logDirectory = args.Length > 0 ? args[0] : "artifacts/logs";

// Get today's date in the format used by log files
string today = DateTime.Now.ToString("yyyy-MM-dd");

// Look for log files from today
string logPattern = $"opentelwatcher-all-{today}*.log";

if (!Directory.Exists(logDirectory))
{
    Console.WriteLine($"{Colors.Yellow}No log files found for today ({today}) - this may be expected if no E2E tests ran.{Colors.Reset}");
    return 0;
}

var logFiles = Directory.GetFiles(logDirectory, logPattern);

if (logFiles.Length == 0)
{
    Console.WriteLine($"{Colors.Yellow}No log files found for today ({today}) - this may be expected if no E2E tests ran.{Colors.Reset}");
    return 0;
}

Console.WriteLine($"{Colors.Cyan}Checking {logFiles.Length} log file(s) for warnings...{Colors.Reset}");

bool foundWarnings = false;
var warningMessages = new List<string>();

foreach (var logFile in logFiles)
{
    try
    {
        string content = File.ReadAllText(logFile);

        if (string.IsNullOrEmpty(content))
        {
            continue;
        }

        string fileName = Path.GetFileName(logFile);

        // Check for ungraceful shutdown warnings
        if (content.Contains("Ungraceful shutdown"))
        {
            warningMessages.Add($"  - Found 'Ungraceful shutdown' in {fileName}");
            foundWarnings = true;
        }

        // Check for PID file warnings
        if (Regex.IsMatch(content, @"opentelwatcher\.pid file still exists"))
        {
            warningMessages.Add($"  - Found 'pid file still exists' warning in {fileName}");
            foundWarnings = true;
        }

        // Check for unhandled exceptions
        if (content.Contains("Unhandled exception"))
        {
            warningMessages.Add($"  - Found 'Unhandled exception' in {fileName}");
            foundWarnings = true;
        }

        // Check for error-level log messages (but not test-related expected errors)
        var errorMatches = Regex.Matches(content, @"\|ERROR\|(?!.*test)", RegexOptions.IgnoreCase);
        if (errorMatches.Count > 0)
        {
            warningMessages.Add($"  - Found {errorMatches.Count} ERROR log entries in {fileName}");
            foundWarnings = true;
        }
    }
    catch
    {
        // Skip files that can't be read
        continue;
    }
}

if (foundWarnings)
{
    Console.WriteLine();
    Console.WriteLine($"{Colors.Red}========================================{Colors.Reset}");
    Console.WriteLine($"{Colors.Red}TEST LOG WARNINGS DETECTED{Colors.Reset}");
    Console.WriteLine($"{Colors.Red}========================================{Colors.Reset}");
    Console.WriteLine();
    Console.WriteLine($"{Colors.Yellow}The following issues were found in test logs:{Colors.Reset}");
    foreach (var msg in warningMessages)
    {
        Console.WriteLine($"{Colors.Yellow}{msg}{Colors.Reset}");
    }
    Console.WriteLine();
    Console.WriteLine($"{Colors.Yellow}Please inspect the log files in: {logDirectory}{Colors.Reset}");
    Console.WriteLine($"{Colors.Cyan}Look for:{Colors.Reset}");
    Console.WriteLine($"{Colors.Cyan}  - Ungraceful shutdowns (process cleanup issues){Colors.Reset}");
    Console.WriteLine($"{Colors.Cyan}  - PID file warnings (file not cleaned up){Colors.Reset}");
    Console.WriteLine($"{Colors.Cyan}  - Unhandled exceptions{Colors.Reset}");
    Console.WriteLine($"{Colors.Cyan}  - Unexpected ERROR level log entries{Colors.Reset}");
    Console.WriteLine();
    Console.WriteLine($"{Colors.Yellow}Note: This is a WARNING, not a failure. Tests may have passed but issues were detected.{Colors.Reset}");
    Console.WriteLine($"{Colors.Red}========================================{Colors.Reset}");
    Console.WriteLine();

    // Exit with 0 to not fail the build, but the warnings are visible
    return 0;
}
else
{
    Console.WriteLine($"{Colors.Green}No warnings found in test logs. All clear!{Colors.Reset}");
    return 0;
}

// ANSI color codes for cross-platform colored console output
static class Colors
{
    public const string Reset = "\x1b[0m";
    public const string Red = "\x1b[31m";
    public const string Green = "\x1b[32m";
    public const string Yellow = "\x1b[33m";
    public const string Cyan = "\x1b[36m";
}

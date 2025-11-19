#!/usr/bin/dotnet run

// Workaround for getting dotnet publish to work without missing linker complaints.
#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PublishSingleFile=true

#:sdk Microsoft.NET.Sdk


// Analyze test execution times from TRX files
// This script is called automatically after test execution by MSBuild
// Usage: dotnet run analyze-test-times.cs [TrxDirectory] [WarningThresholdMs] [ErrorThresholdMs]

using System.Xml.Linq;

// Parse command-line arguments
string trxDirectory = args.Length > 0 ? args[0] : "artifacts/test-results";
int warningThresholdMs = args.Length > 1 ? int.Parse(args[1]) : 2000;  // 2 seconds
int errorThresholdMs = args.Length > 2 ? int.Parse(args[2]) : 5000;    // 5 seconds

// Find all TRX files
if (!Directory.Exists(trxDirectory))
{
    Console.WriteLine($"{Colors.Yellow}No TRX files found to analyze.{Colors.Reset}");
    return 0;
}

var trxFiles = Directory.GetFiles(trxDirectory, "*.trx", SearchOption.AllDirectories);

if (trxFiles.Length == 0)
{
    Console.WriteLine($"{Colors.Yellow}No TRX files found to analyze.{Colors.Reset}");
    return 0;
}

Console.WriteLine($"{Colors.Cyan}Analyzing test execution times from {trxFiles.Length} TRX file(s)...{Colors.Reset}");

var slowTests = new List<SlowTest>();
int totalTests = 0;
TimeSpan totalDuration = TimeSpan.Zero;

foreach (var trxFile in trxFiles)
{
    try
    {
        XDocument trx = XDocument.Load(trxFile);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

        var tests = trx.Descendants(ns + "UnitTestResult");

        foreach (var test in tests)
        {
            totalTests++;

            // Parse duration (format: HH:MM:SS.mmmmmmm)
            try
            {
                string? durationStr = test.Attribute("duration")?.Value;
                if (string.IsNullOrEmpty(durationStr))
                {
                    continue;
                }

                TimeSpan duration = TimeSpan.Parse(durationStr);
                totalDuration += duration;
                double durationMs = duration.TotalMilliseconds;

                string testName = test.Attribute("testName")?.Value ?? "Unknown";
                string outcome = test.Attribute("outcome")?.Value ?? "Unknown";
                string fileName = Path.GetFileName(trxFile);

                if (durationMs > errorThresholdMs)
                {
                    slowTests.Add(new SlowTest(testName, duration, durationMs, "ERROR", fileName, outcome));
                }
                else if (durationMs > warningThresholdMs)
                {
                    slowTests.Add(new SlowTest(testName, duration, durationMs, "WARNING", fileName, outcome));
                }
            }
            catch
            {
                // Skip tests with invalid duration format
                continue;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{Colors.Yellow}Warning: Failed to parse {Path.GetFileName(trxFile)}: {ex.Message}{Colors.Reset}");
        continue;
    }
}

// Calculate average test time
double avgDuration = totalTests > 0 ? totalDuration.TotalMilliseconds / totalTests : 0;

// Display results
Console.WriteLine();
Console.WriteLine($"{Colors.Cyan}Test Execution Summary:{Colors.Reset}");
Console.WriteLine($"{Colors.White}  Total tests: {totalTests}{Colors.Reset}");
Console.WriteLine($"{Colors.White}  Total duration: {totalDuration.TotalSeconds:F2}s{Colors.Reset}");
Console.WriteLine($"{Colors.White}  Average per test: {avgDuration:F0}ms{Colors.Reset}");
Console.WriteLine();

if (slowTests.Count > 0)
{
    Console.WriteLine($"{Colors.Yellow}========================================{Colors.Reset}");
    Console.WriteLine($"{Colors.Yellow}SLOW TESTS DETECTED ({slowTests.Count} test(s)){Colors.Reset}");
    Console.WriteLine($"{Colors.Yellow}========================================{Colors.Reset}");
    Console.WriteLine();

    // Group by severity
    var errors = slowTests.Where(t => t.Severity == "ERROR").OrderByDescending(t => t.DurationMs).ToList();
    var warnings = slowTests.Where(t => t.Severity == "WARNING").OrderByDescending(t => t.DurationMs).ToList();

    if (errors.Count > 0)
    {
        Console.WriteLine($"{Colors.Red}VERY SLOW TESTS (>{errorThresholdMs / 1000}s):{Colors.Reset}");
        foreach (var test in errors)
        {
            Console.WriteLine($"{Colors.Red}  - {test.TestName}{Colors.Reset}");
            Console.WriteLine($"{Colors.Red}    Duration: {test.Duration.TotalSeconds:F2}s{Colors.Reset}");
        }
        Console.WriteLine();
    }

    if (warnings.Count > 0)
    {
        Console.WriteLine($"{Colors.Yellow}SLOW TESTS (>{warningThresholdMs / 1000}s):{Colors.Reset}");
        foreach (var test in warnings)
        {
            Console.WriteLine($"{Colors.Yellow}  - {test.TestName}{Colors.Reset}");
            Console.WriteLine($"{Colors.Yellow}    Duration: {test.Duration.TotalSeconds:F2}s{Colors.Reset}");
        }
        Console.WriteLine();
    }

    Console.WriteLine($"{Colors.Cyan}Recommendation: Investigate slow tests for:{Colors.Reset}");
    Console.WriteLine($"{Colors.Cyan}  - Unnecessary delays or Thread.Sleep calls{Colors.Reset}");
    Console.WriteLine($"{Colors.Cyan}  - Large file operations that could be optimized{Colors.Reset}");
    Console.WriteLine($"{Colors.Cyan}  - Network calls that should be mocked{Colors.Reset}");
    Console.WriteLine($"{Colors.Cyan}  - Inefficient algorithms or data structures{Colors.Reset}");
    Console.WriteLine($"{Colors.Yellow}========================================{Colors.Reset}");
    Console.WriteLine();

    // Exit with 0 to not fail the build (warnings only)
    return 0;
}
else
{
    Console.WriteLine($"{Colors.Green}All tests completed within expected time ranges.{Colors.Reset}");
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
    public const string White = "\x1b[37m";
}

record SlowTest(string TestName, TimeSpan Duration, double DurationMs, string Severity, string File, string Outcome);

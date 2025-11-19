#!/usr/bin/dotnet run

// Workaround for getting dotnet publish to work without missing linker complaints.
#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PublishSingleFile=true

#:sdk Microsoft.NET.Sdk

// Display code coverage summary - extracts line coverage percentage from Summary.txt
// Usage: dotnet run display-coverage-summary.cs <summary-file-path>

if (args.Length == 0)
{
    Console.WriteLine("Usage: DisplayCoverageSummary <summary-file-path>");
    return 1;
}

var summaryFile = args[0];

if (!File.Exists(summaryFile))
{
    // Silently exit if no coverage file (coverage might be disabled)
    return 0;
}

try
{
    var lines = File.ReadAllLines(summaryFile);
    var lineCoverageLine = lines.FirstOrDefault(l => l.Trim().StartsWith("Line coverage:"));

    if (lineCoverageLine != null)
    {
        // Extract percentage (e.g., "  Line coverage: 76.1%" -> "76.1%")
        var percentage = lineCoverageLine.Split(':')[1].Trim();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Code Coverage: {percentage} line coverage");
        Console.ResetColor();
    }

    return 0;
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Error reading coverage summary: {ex.Message}");
    Console.ResetColor();
    return 1;
}

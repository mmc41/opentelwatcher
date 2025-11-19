#!/usr/bin/dotnet run

#:sdk Microsoft.NET.Sdk

// Extract coverage summary from Visual Studio XML coverage file
// Usage: dotnet run extract-coverage-summary.cs <coverage.xml> <Summary.txt>

using System.Xml.Linq;

if (args.Length < 2)
{
    Console.WriteLine("Usage: ExtractCoverageSummary <coverage.xml> <Summary.txt>");
    return 1;
}

var coverageXmlPath = args[0];
var summaryOutputPath = args[1];

if (!File.Exists(coverageXmlPath))
{
    // Silently exit if no coverage file
    return 0;
}

try
{
    var xml = XDocument.Load(coverageXmlPath);
    var modules = xml.Descendants("module").ToList();

    if (modules.Count == 0)
    {
        // No coverage data
        File.WriteAllText(summaryOutputPath, "No coverage data available.\n");
        return 0;
    }

    // Calculate total lines across all modules
    long totalCoveredLines = 0;
    long totalCoverableLines = 0;

    foreach (var module in modules)
    {
        var linesCovered = long.Parse(module.Attribute("lines_covered")?.Value ?? "0");
        var linesPartiallyCovered = long.Parse(module.Attribute("lines_partially_covered")?.Value ?? "0");
        var linesNotCovered = long.Parse(module.Attribute("lines_not_covered")?.Value ?? "0");

        totalCoveredLines += linesCovered + linesPartiallyCovered;
        totalCoverableLines += linesCovered + linesPartiallyCovered + linesNotCovered;
    }

    // Calculate percentage
    double coveragePercentage = totalCoverableLines > 0
        ? (double)totalCoveredLines / totalCoverableLines * 100
        : 0;

    // Generate summary file (minimal format for DisplayCoverageSummary)
    var summary = $"""
        Summary
          Generated on: {DateTime.Now:dd/MM/yyyy - HH.mm.ss}
          Parser: DynamicCodeCoverage (dotnet-coverage)
          Line coverage: {coveragePercentage:F1}%
          Covered lines: {totalCoveredLines}
          Uncovered lines: {totalCoverableLines - totalCoveredLines}
          Coverable lines: {totalCoverableLines}
        """;

    File.WriteAllText(summaryOutputPath, summary + "\n");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error parsing coverage XML: {ex.Message}");
    return 1;
}

using OpenTelWatcher.Services.Interfaces;

namespace OpenTelWatcher.Services;

/// <summary>
/// Scans filesystem for error telemetry files (*.errors.ndjson).
/// Provides centralized logic for error file detection used by CLI commands and API endpoints.
/// </summary>
public sealed class ErrorFileScanner : IErrorFileScanner
{
    private const string ErrorFilePattern = "*.errors.ndjson";

    /// <inheritdoc/>
    public int CountErrorFiles(string outputDirectory)
    {
        try
        {
            if (!Directory.Exists(outputDirectory))
            {
                return 0;
            }

            return Directory.GetFiles(outputDirectory, ErrorFilePattern, SearchOption.TopDirectoryOnly).Length;
        }
        catch
        {
            // Return 0 on any error (permissions, I/O issues, etc.)
            return 0;
        }
    }

    /// <inheritdoc/>
    public List<string> GetErrorFiles(string outputDirectory)
    {
        try
        {
            if (!Directory.Exists(outputDirectory))
            {
                return new List<string>();
            }

            return Directory.GetFiles(outputDirectory, ErrorFilePattern, SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => name != null)
                .Cast<string>()
                .OrderBy(name => name)
                .ToList();
        }
        catch
        {
            // Return empty list on any error (permissions, I/O issues, etc.)
            return new List<string>();
        }
    }
}

namespace OpenTelWatcher.Services.Interfaces;

/// <summary>
/// Service for scanning filesystem for error telemetry files (*.errors.ndjson).
/// Provides file counting and listing operations for error detection.
/// </summary>
public interface IErrorFileScanner
{
    /// <summary>
    /// Counts the number of error files in the specified directory.
    /// </summary>
    /// <param name="outputDirectory">Directory to scan for error files</param>
    /// <returns>Number of *.errors.ndjson files found, or 0 if directory doesn't exist or on error</returns>
    int CountErrorFiles(string outputDirectory);

    /// <summary>
    /// Gets a sorted list of error file names in the specified directory.
    /// </summary>
    /// <param name="outputDirectory">Directory to scan for error files</param>
    /// <returns>Sorted list of error file names (filenames only, not full paths), or empty list if directory doesn't exist or on error</returns>
    List<string> GetErrorFiles(string outputDirectory);
}

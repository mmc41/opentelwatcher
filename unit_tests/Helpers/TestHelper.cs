namespace UnitTests.Helpers;

/// <summary>
/// Helper utilities for unit tests.
/// Provides centralized test artifact path management and solution root discovery.
/// </summary>
public static class TestHelper
{
    /// <summary>
    /// Finds the solution root directory by looking for the project.root marker file.
    /// </summary>
    private static string FindSolutionRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "project.root")))
            {
                return directory;
            }
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new InvalidOperationException(
            "Could not find solution root. Ensure project.root file exists at solution root.");
    }

    /// <summary>
    /// Solution root directory (absolute path).
    /// </summary>
    public static readonly string SolutionRoot = FindSolutionRoot();

    /// <summary>
    /// Base directory for all unit test artifacts (absolute path).
    /// Located in artifacts/test-results/unit/ per project structure.
    /// </summary>
    public static readonly string BaseTestOutputDir = Path.Combine(
        SolutionRoot, "artifacts", "test-results", "unit");

    /// <summary>
    /// Gets a test-specific output directory within the unit test artifacts folder.
    /// Creates the directory if it doesn't exist.
    /// </summary>
    /// <param name="testName">Name of the test or test suite (e.g., "check-command")</param>
    /// <returns>Full absolute path to the test output directory</returns>
    public static string GetTestOutputDir(string testName)
    {
        var testDir = Path.Combine(BaseTestOutputDir, testName);
        Directory.CreateDirectory(testDir); // Ensure directory exists
        return testDir;
    }
}

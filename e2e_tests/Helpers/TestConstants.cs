namespace OpenTelWatcher.E2ETests.Helpers;

/// <summary>
/// Constants used across E2E tests.
/// Centralizes test artifact paths per project guidelines.
/// </summary>
public static class TestConstants
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
    /// Base directory for all E2E test artifacts (absolute path).
    /// Located in artifacts/test-results/e2e/ per project structure.
    /// </summary>
    public static readonly string BaseTestOutputDir = Path.Combine(
        SolutionRoot, "artifacts", "test-results", "e2e");

    /// <summary>
    /// Gets a test-specific output directory within the E2E test artifacts folder.
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

    /// <summary>
    /// Default test port for E2E tests to avoid conflicts with production instances.
    /// </summary>
    public const int DefaultTestPort = 5318;
}

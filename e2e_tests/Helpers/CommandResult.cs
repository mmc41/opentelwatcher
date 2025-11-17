namespace OpenTelWatcher.Tests.E2E;

/// <summary>
/// Result of running a CLI command with captured output.
/// </summary>
public record CommandResult
{
    public int ExitCode { get; init; }
    public string Output { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}

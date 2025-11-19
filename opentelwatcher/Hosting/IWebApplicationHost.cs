namespace OpenTelWatcher.Hosting;

/// <summary>
/// Abstraction for WebApplication host lifecycle management.
/// Enables testing command logic without starting actual web servers.
/// </summary>
public interface IWebApplicationHost
{
    /// <summary>
    /// Builds and runs the web application with specified options.
    /// Blocks until application stops.
    /// </summary>
    Task<int> RunAsync(ServerOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates server options before starting.
    /// </summary>
    ValidationResult Validate(ServerOptions options);
}

/// <summary>
/// Configuration options for starting the web server.
/// </summary>
public record ServerOptions
{
    public required int Port { get; init; }
    public required string OutputDirectory { get; init; }
    public required string LogLevel { get; init; }
    public bool Daemon { get; init; }
    public bool Silent { get; init; }
    public bool Verbose { get; init; }
    public bool EnableTails { get; init; }
    public bool TailsFilterErrorsOnly { get; init; }
}

/// <summary>
/// Result of server options validation.
/// </summary>
public record ValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = new();

    public static ValidationResult Success() => new() { IsValid = true };

    public static ValidationResult Failure(params string[] errors) =>
        new() { IsValid = false, Errors = errors.ToList() };
}

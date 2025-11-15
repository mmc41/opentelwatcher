namespace OpenTelWatcher.CLI.Models;

/// <summary>
/// Command-line options for start command
/// </summary>
public sealed record CommandOptions
{
    public int Port { get; init; } = 4318;
    public string OutputDirectory { get; init; } = "./telemetry-data";
    public LogLevel LogLevel { get; init; } = LogLevel.Information;
    public bool Daemon { get; init; } = false;
    public bool Silent { get; init; } = false;
    public bool Verbose { get; init; } = false;
}

/// <summary>
/// Log level enumeration matching .NET standard levels
/// </summary>
public enum LogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Result of command execution
/// </summary>
public sealed record CommandResult
{
    public required int ExitCode { get; init; }
    public bool IsSuccess => ExitCode == 0;
    public required string Message { get; init; }
    public string? Details { get; init; }

    public static CommandResult Success(string message, string? details = null) =>
        new() { ExitCode = 0, Message = message, Details = details };

    public static CommandResult UserError(string message, string? details = null) =>
        new() { ExitCode = 1, Message = message, Details = details };

    public static CommandResult SystemError(string message, string? details = null) =>
        new() { ExitCode = 2, Message = message, Details = details };
}

/// <summary>
/// Instance detection status
/// </summary>
public sealed record InstanceStatus
{
    public required bool IsRunning { get; init; }
    public VersionResponse? Version { get; init; }
    public int? Pid { get; init; }
    public bool IsCompatible { get; init; }
    public string? IncompatibilityReason { get; init; }
    public string? DetectionError { get; init; }
}

using System.Text.Json.Serialization;

namespace OpenTelWatcher.CLI.Models;

/// <summary>
/// Response from /api/info endpoint (combines version and diagnostics)
/// </summary>
public sealed record InfoResponse
{
    [JsonPropertyName("application")]
    public required string Application { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("versionComponents")]
    public required VersionComponents VersionComponents { get; init; }

    [JsonPropertyName("processId")]
    public required int ProcessId { get; init; }

    [JsonPropertyName("port")]
    public required int Port { get; init; }

    [JsonPropertyName("health")]
    public required DiagnoseHealth Health { get; init; }

    [JsonPropertyName("files")]
    public required FileStatistics Files { get; init; }

    [JsonPropertyName("configuration")]
    public required DiagnoseConfiguration Configuration { get; init; }
}

/// <summary>
/// Response from /api/version endpoint (deprecated - use /api/info instead)
/// </summary>
public sealed record VersionResponse
{
    [JsonPropertyName("application")]
    public required string Application { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("versionComponents")]
    public required VersionComponents VersionComponents { get; init; }

    [JsonPropertyName("processId")]
    public required int ProcessId { get; init; }
}

/// <summary>
/// Semantic version components
/// </summary>
public sealed record VersionComponents
{
    [JsonPropertyName("major")]
    public required int Major { get; init; }

    [JsonPropertyName("minor")]
    public required int Minor { get; init; }

    [JsonPropertyName("patch")]
    public required int Patch { get; init; }
}

/// <summary>
/// Response from /api/shutdown endpoint (already exists)
/// </summary>
public sealed record ShutdownResponse
{
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("timestamp")]
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// Response from /api/clear endpoint
/// </summary>
public sealed record ClearResponse
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("filesDeleted")]
    public required int FilesDeleted { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("timestamp")]
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// Response from /api/diagnose endpoint (deprecated - use /api/info instead)
/// </summary>
public sealed record DiagnoseResponse
{
    [JsonPropertyName("health")]
    public required DiagnoseHealth Health { get; init; }

    [JsonPropertyName("files")]
    public required FileStatistics Files { get; init; }

    [JsonPropertyName("configuration")]
    public required DiagnoseConfiguration Configuration { get; init; }
}

public sealed record DiagnoseHealth
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("consecutiveErrors")]
    public required int ConsecutiveErrors { get; init; }

    [JsonPropertyName("recentErrors")]
    public required List<string> RecentErrors { get; init; }
}

/// <summary>
/// Aggregate file statistics (count and total size)
/// </summary>
public sealed record FileStatistics
{
    [JsonPropertyName("count")]
    public required int Count { get; init; }

    [JsonPropertyName("totalSizeBytes")]
    public required long TotalSizeBytes { get; init; }
}

/// <summary>
/// Individual file information (deprecated - no longer returned by API)
/// </summary>
public sealed record DiagnoseFile
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("sizeBytes")]
    public required long SizeBytes { get; init; }

    [JsonPropertyName("lastModified")]
    public required string LastModified { get; init; }
}

public sealed record DiagnoseConfiguration
{
    [JsonPropertyName("outputDirectory")]
    public required string OutputDirectory { get; init; }
}

/// <summary>
/// Response from /api/stats endpoint
/// </summary>
public sealed record StatsResponse
{
    [JsonPropertyName("telemetry")]
    public required TelemetryStatistics Telemetry { get; init; }

    [JsonPropertyName("files")]
    public required FileBreakdown Files { get; init; }

    [JsonPropertyName("uptimeSeconds")]
    public required long UptimeSeconds { get; init; }
}

/// <summary>
/// Telemetry request counts
/// </summary>
public sealed record TelemetryStatistics
{
    [JsonPropertyName("traces")]
    public required TelemetryTypeStats Traces { get; init; }

    [JsonPropertyName("logs")]
    public required TelemetryTypeStats Logs { get; init; }

    [JsonPropertyName("metrics")]
    public required TelemetryTypeStats Metrics { get; init; }
}

/// <summary>
/// Statistics for a specific telemetry type
/// </summary>
public sealed record TelemetryTypeStats
{
    [JsonPropertyName("requests")]
    public required long Requests { get; init; }
}

/// <summary>
/// File counts and sizes by telemetry type
/// </summary>
public sealed record FileBreakdown
{
    [JsonPropertyName("traces")]
    public required FileTypeStats Traces { get; init; }

    [JsonPropertyName("logs")]
    public required FileTypeStats Logs { get; init; }

    [JsonPropertyName("metrics")]
    public required FileTypeStats Metrics { get; init; }
}

/// <summary>
/// File statistics for a specific telemetry type
/// </summary>
public sealed record FileTypeStats
{
    [JsonPropertyName("count")]
    public required int Count { get; init; }

    [JsonPropertyName("sizeBytes")]
    public required long SizeBytes { get; init; }
}

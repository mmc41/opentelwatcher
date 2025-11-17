using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Services.Interfaces;
using OpenTelWatcher.Utilities;

namespace OpenTelWatcher.Web;

/// <summary>
/// Health information for the status page.
/// </summary>
public class HealthInfo
{
    public HealthStatus Status { get; init; }
    public string StatusText { get; init; } = string.Empty;
    public TimeSpan Uptime { get; init; }
    public string UptimeFormatted { get; init; } = string.Empty;
    public DateTime LastUpdated { get; init; }
    public string LastUpdatedFormatted { get; init; } = string.Empty;
}

/// <summary>
/// Telemetry statistics for the status page.
/// </summary>
public class TelemetryStatistics
{
    public long TracesReceived { get; init; }
    public long LogsReceived { get; init; }
    public long MetricsReceived { get; init; }
    public int ConsecutiveErrors { get; init; }
    public string TracesFormatted { get; init; } = string.Empty;
    public string LogsFormatted { get; init; } = string.Empty;
    public string MetricsFormatted { get; init; } = string.Empty;
}

/// <summary>
/// Configuration information for the status page.
/// </summary>
public class ConfigurationInfo
{
    public string OutputDirectory { get; init; } = string.Empty;
    public int MaxFileSizeMB { get; init; }
    public bool PrettyPrint { get; init; }
    public int ErrorHistorySize { get; init; }
    public int MaxConsecutiveErrors { get; init; }
    public int RequestTimeoutSeconds { get; init; }
    public bool IsAvailable { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Endpoint information for the status page.
/// </summary>
public class EndpointInfo
{
    public string BaseUrl { get; init; } = string.Empty;
    public string TracesEndpoint { get; init; } = string.Empty;
    public string LogsEndpoint { get; init; } = string.Empty;
    public string MetricsEndpoint { get; init; } = string.Empty;
    public string HealthEndpoint { get; init; } = string.Empty;
    public string DiagnoseEndpoint { get; init; } = string.Empty;
    public string SwaggerUiEndpoint { get; init; } = string.Empty;
    public string OpenApiSpecEndpoint { get; init; } = string.Empty;
    public string ClearEndpoint { get; init; } = string.Empty;
}

/// <summary>
/// File statistics for the status page.
/// </summary>
public class FileInfo
{
    public int Count { get; init; }
    public long TotalSizeBytes { get; init; }
    public string TotalSizeFormatted { get; init; } = string.Empty;
}

/// <summary>
/// Page Model for the status dashboard.
/// </summary>
public class IndexModel : PageModel
{
    private readonly IDiagnosticsCollector _diagnostics;
    private readonly ILogger<IndexModel> _logger;
    private readonly IConfiguration _configuration;
    private readonly ITimeProvider _timeProvider;
    private readonly DateTime _startTime;

    public HealthInfo HealthInfo { get; private set; } = null!;
    public TelemetryStatistics Statistics { get; private set; } = null!;
    public ConfigurationInfo Configuration { get; private set; } = null!;
    public EndpointInfo Endpoints { get; private set; } = null!;
    public FileInfo Files { get; private set; } = null!;
    public string GitHubUrl { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;

    public IndexModel(
        IDiagnosticsCollector diagnostics,
        ILogger<IndexModel> logger,
        IConfiguration configuration,
        ITimeProvider timeProvider)
    {
        _diagnostics = diagnostics;
        _logger = logger;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _startTime = timeProvider.UtcNow;
    }

    public void OnGet()
    {
        try
        {
            // Populate HealthInfo
            var status = _diagnostics.GetHealthStatus();
            var uptime = _timeProvider.UtcNow - _startTime;

            HealthInfo = new HealthInfo
            {
                Status = status,
                StatusText = status == HealthStatus.Healthy ? "Healthy" : "Degraded",
                Uptime = uptime,
                UptimeFormatted = UptimeFormatter.FormatUptime(uptime),
                LastUpdated = _timeProvider.UtcNow,
                LastUpdatedFormatted = _timeProvider.UtcNow.ToString("o")
            };

            // Populate Statistics
            var stats = _diagnostics.GetTelemetryStatistics();
            var consecutiveErrors = _diagnostics.GetConsecutiveErrorCount();

            Statistics = new TelemetryStatistics
            {
                TracesReceived = stats.TracesReceived,
                LogsReceived = stats.LogsReceived,
                MetricsReceived = stats.MetricsReceived,
                ConsecutiveErrors = consecutiveErrors,
                TracesFormatted = NumberFormatter.FormatCount(stats.TracesReceived),
                LogsFormatted = NumberFormatter.FormatCount(stats.LogsReceived),
                MetricsFormatted = NumberFormatter.FormatCount(stats.MetricsReceived)
            };

            // Populate Configuration with graceful fallback
            try
            {
                var outputDir = _diagnostics.GetOutputDirectory();
                var maxFileSizeMB = _configuration.GetValue<int>("OpenTelWatcher:MaxFileSizeMB", 100);
                var prettyPrint = _configuration.GetValue<bool>("OpenTelWatcher:PrettyPrint", false);
                var errorHistorySize = _configuration.GetValue<int>("OpenTelWatcher:MaxErrorHistorySize", 50);
                var maxConsecutiveErrors = _configuration.GetValue<int>("OpenTelWatcher:MaxConsecutiveFileErrors", 10);
                var requestTimeoutSeconds = _configuration.GetValue<int>("OpenTelWatcher:RequestTimeoutSeconds", 30);

                Configuration = new ConfigurationInfo
                {
                    OutputDirectory = outputDir,
                    MaxFileSizeMB = maxFileSizeMB,
                    PrettyPrint = prettyPrint,
                    ErrorHistorySize = errorHistorySize,
                    MaxConsecutiveErrors = maxConsecutiveErrors,
                    RequestTimeoutSeconds = requestTimeoutSeconds,
                    IsAvailable = true,
                    ErrorMessage = null
                };
            }
            catch (Exception configEx)
            {
                _logger.LogWarning(configEx, "Error loading configuration for status page");
                Configuration = new ConfigurationInfo
                {
                    IsAvailable = false,
                    ErrorMessage = "Configuration unavailable"
                };
            }

            // Populate File Statistics
            var fileInfo = _diagnostics.GetFileInfo();
            var totalSize = fileInfo.Sum(f => f.SizeBytes);
            Files = new FileInfo
            {
                Count = fileInfo.Count,
                TotalSizeBytes = totalSize,
                TotalSizeFormatted = FormatBytes(totalSize)
            };

            // Populate Endpoints
            // Force ApiConstants.Network.LocalhostIp instead of localhost for consistency
            var port = Request.Host.Port ?? 4318;
            var baseUrl = $"{Request.Scheme}://{ApiConstants.Network.LocalhostIp}:{port}";
            Endpoints = new EndpointInfo
            {
                BaseUrl = baseUrl,
                TracesEndpoint = $"{baseUrl}/v1/traces",
                LogsEndpoint = $"{baseUrl}/v1/logs",
                MetricsEndpoint = $"{baseUrl}/v1/metrics",
                HealthEndpoint = $"{baseUrl}/healthz",
                DiagnoseEndpoint = $"{baseUrl}/api/status",
                SwaggerUiEndpoint = $"{baseUrl}/swagger",
                OpenApiSpecEndpoint = $"{baseUrl}/openapi/v1.json",
                ClearEndpoint = $"{baseUrl}/api/clear"
            };

            // Populate GitHubUrl
            GitHubUrl = _configuration.GetValue<string>("OpenTelWatcher:GitHubUrl") ?? "https://github.com/mmc41/opentelwatcher";

            // Populate Version
            Version = typeof(IndexModel).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error populating status page model");

            // Provide fallback values
            HealthInfo = new HealthInfo
            {
                Status = HealthStatus.Degraded,
                StatusText = "Unknown",
                Uptime = TimeSpan.Zero,
                UptimeFormatted = "0s",
                LastUpdated = _timeProvider.UtcNow,
                LastUpdatedFormatted = _timeProvider.UtcNow.ToString("o")
            };

            Statistics = new TelemetryStatistics
            {
                TracesReceived = 0,
                LogsReceived = 0,
                MetricsReceived = 0,
                ConsecutiveErrors = 0,
                TracesFormatted = "0",
                LogsFormatted = "0",
                MetricsFormatted = "0"
            };

            Configuration = new ConfigurationInfo
            {
                IsAvailable = false,
                ErrorMessage = "Configuration unavailable"
            };

            Endpoints = new EndpointInfo
            {
                BaseUrl = "Unknown",
                TracesEndpoint = "Unknown",
                LogsEndpoint = "Unknown",
                MetricsEndpoint = "Unknown",
                HealthEndpoint = "Unknown",
                DiagnoseEndpoint = "Unknown",
                SwaggerUiEndpoint = "Unknown",
                OpenApiSpecEndpoint = "Unknown"
            };

            GitHubUrl = "https://github.com/mmc41/opentelwatcher";

            Files = new FileInfo
            {
                Count = 0,
                TotalSizeBytes = 0,
                TotalSizeFormatted = "0 B"
            };
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F2} KB";

        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):F2} MB";

        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}

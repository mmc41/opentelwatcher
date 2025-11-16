using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using NLog.Web;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Services;
using OpenTelWatcher.Services.Interfaces;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using Google.Protobuf;

namespace OpenTelWatcher.Hosting;

/// <summary>
/// Production implementation of IWebApplicationHost.
/// Builds and runs the ASP.NET Core WebApplication with all required services and middleware.
/// </summary>
public class WebApplicationHost : IWebApplicationHost
{
    private readonly ILogger<WebApplicationHost>? _logger;

    public WebApplicationHost(ILogger<WebApplicationHost>? logger = null)
    {
        _logger = logger;
    }

    public async Task<int> RunAsync(ServerOptions options, CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder();

        ConfigureLogging(builder);
        ConfigureWebHost(builder, options);
        var watcherOptions = ConfigureOptions(builder, options);

        if (!ValidateOptions(watcherOptions))
            return 1;

        RegisterServices(builder, watcherOptions, options);

        var app = builder.Build();
        SetupApplication(app, options);

        var diagnosticsCollector = app.Services.GetRequiredService<IDiagnosticsCollector>();
        var pidFileService = app.Services.GetRequiredService<IPidFileService>();
        DisplayStartupBanner(options, watcherOptions.OutputDirectory, pidFileService.PidFilePath, diagnosticsCollector);

        await app.RunAsync(cancellationToken);
        return 0;
    }

    /// <summary>
    /// Configures logging with NLog.
    /// </summary>
    private void ConfigureLogging(WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Host.UseNLog();
    }

    /// <summary>
    /// Configures web host settings (URLs and log level).
    /// </summary>
    private void ConfigureWebHost(WebApplicationBuilder builder, ServerOptions options)
    {
        builder.WebHost.UseUrls($"http://{ApiConstants.Network.LocalhostIp}:{options.Port}");

        if (Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(options.LogLevel, ignoreCase: true, out var logLevel))
        {
            builder.Logging.SetMinimumLevel(logLevel);
        }
    }

    /// <summary>
    /// Configures and binds application options from configuration.
    /// </summary>
    private OpenTelWatcherOptions ConfigureOptions(WebApplicationBuilder builder, ServerOptions options)
    {
        var watcherOptions = new OpenTelWatcherOptions();
        builder.Configuration.GetSection("OpenTelWatcher").Bind(watcherOptions);
        watcherOptions.OutputDirectory = options.OutputDirectory;
        return watcherOptions;
    }

    /// <summary>
    /// Validates configuration options.
    /// </summary>
    private bool ValidateOptions(OpenTelWatcherOptions watcherOptions)
    {
        var validationResult = ConfigurationValidator.Validate(watcherOptions);
        if (!validationResult.IsValid)
        {
            foreach (var error in validationResult.Errors)
            {
                Console.Error.WriteLine($"Configuration error: {error}");
            }
            return false;
        }
        return true;
    }

    /// <summary>
    /// Registers all services in the DI container.
    /// </summary>
    private void RegisterServices(WebApplicationBuilder builder, OpenTelWatcherOptions watcherOptions, ServerOptions options)
    {
        // Core services
        builder.Services.AddSingleton(watcherOptions);
        builder.Services.AddSingleton<IFileRotationService, FileRotationService>();
        builder.Services.AddSingleton<IHealthMonitor, HealthMonitor>();
        builder.Services.AddSingleton<IErrorDetectionService, ErrorDetectionService>();
        builder.Services.AddSingleton<ITelemetryFileWriter, TelemetryFileWriter>();
        builder.Services.AddSingleton<ITelemetryStatistics, TelemetryStatisticsService>();
        builder.Services.AddSingleton<IDiagnosticsCollector, DiagnosticsCollector>();
        builder.Services.AddSingleton<IPidFileService, PidFileService>();
        builder.Services.AddSingleton<ITelemetryFileManager, TelemetryFileManager>();

        // Razor Pages
        builder.Services.AddRazorPages(opts =>
        {
            opts.RootDirectory = "/web";
        });

        // OpenAPI
        builder.Services.AddOpenApi(opts =>
        {
            opts.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info = new()
                {
                    Title = "OpenTelWatcher API",
                    Version = "v1",
                    Description = "OTLP/HTTP receiver management API for development and testing",
                    Contact = new()
                    {
                        Name = "GitHub Repository",
                        Url = new Uri("https://github.com/mmc41/opentelwatcher")
                    }
                };

                document.Servers = new List<OpenApiServer>
                {
                    new() { Url = $"http://{ApiConstants.Network.LocalhostIp}:{options.Port}" }
                };

                return Task.CompletedTask;
            });
        });
    }

    /// <summary>
    /// Sets up the application middleware and endpoints.
    /// </summary>
    private void SetupApplication(WebApplication app, ServerOptions options)
    {
        // Register PID file and setup cleanup handlers
        var pidFileService = app.Services.GetRequiredService<IPidFileService>();
        pidFileService.Register();
        SetupCleanupHandlers(app, pidFileService);

        // Configure static files
        var assembly = typeof(Program).Assembly;
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new EmbeddedFileProvider(assembly, "opentelwatcher.web"),
            RequestPath = "/web"
        });

        // Map endpoints
        app.MapRazorPages();
        app.MapOpenApi();

        app.UseSwaggerUI(opts =>
        {
            opts.SwaggerEndpoint("/openapi/v1.json", "OpenTelWatcher API v1");
            opts.RoutePrefix = "swagger";
        });

        ConfigureOtlpEndpoints(app);
        ConfigureManagementEndpoints(app, options.Port);
    }

    public ValidationResult Validate(ServerOptions options)
    {
        var errors = new List<string>();

        // Validate using ConfigurationValidator
        var watcherOptions = new OpenTelWatcherOptions
        {
            OutputDirectory = options.OutputDirectory
        };

        var result = ConfigurationValidator.Validate(watcherOptions);
        if (!result.IsValid)
        {
            errors.AddRange(result.Errors);
        }

        if (errors.Any())
        {
            return ValidationResult.Failure(errors.ToArray());
        }

        return ValidationResult.Success();
    }

    private void SetupCleanupHandlers(WebApplication app, IPidFileService pidFileService)
    {
        // 1. Graceful shutdown via ASP.NET Core lifecycle (Ctrl-C, SIGTERM)
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Register(() =>
        {
            pidFileService.Unregister();
        });

        // 2. AppDomain exit handler (catches more scenarios including unhandled exceptions)
        AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) =>
        {
            pidFileService.Unregister();
        };
    }

    private void ConfigureOtlpEndpoints(WebApplication app)
    {
        // OTLP Trace endpoint
        app.MapPost("/v1/traces", async (
            HttpRequest request,
            ITelemetryFileWriter writer,
            ITelemetryStatistics stats,
            CancellationToken cancellationToken) =>
        {
            return await ProcessOtlpRequestAsync<ExportTraceServiceRequest, ExportTraceServiceResponse>(
                request,
                writer,
                stats,
                app.Logger,
                cancellationToken,
                SignalTypes.Traces,
                ExportTraceServiceRequest.Parser,
                s => s.IncrementTraces());
        })
        .WithSummary("OTLP trace ingestion endpoint")
        .WithDescription("Accepts OTLP/HTTP trace data in Protobuf format and persists to disk")
        .Produces<ExportTraceServiceResponse>(200)
        .Produces<object>(400)
        .Produces(500)
        .AddOpenApiOperationTransformer((operation, context, ct) => Task.CompletedTask);

        // OTLP Logs endpoint
        app.MapPost("/v1/logs", async (
            HttpRequest request,
            ITelemetryFileWriter writer,
            ITelemetryStatistics stats,
            CancellationToken cancellationToken) =>
        {
            return await ProcessOtlpRequestAsync<ExportLogsServiceRequest, ExportLogsServiceResponse>(
                request,
                writer,
                stats,
                app.Logger,
                cancellationToken,
                SignalTypes.Logs,
                ExportLogsServiceRequest.Parser,
                s => s.IncrementLogs());
        })
        .WithSummary("OTLP logs ingestion endpoint")
        .WithDescription("Accepts OTLP/HTTP log data in Protobuf format and persists to disk")
        .Produces<ExportLogsServiceResponse>(200)
        .Produces<object>(400)
        .Produces(500)
        .AddOpenApiOperationTransformer((operation, context, ct) => Task.CompletedTask);

        // OTLP Metrics endpoint
        app.MapPost("/v1/metrics", async (
            HttpRequest request,
            ITelemetryFileWriter writer,
            ITelemetryStatistics stats,
            CancellationToken cancellationToken) =>
        {
            return await ProcessOtlpRequestAsync<ExportMetricsServiceRequest, ExportMetricsServiceResponse>(
                request,
                writer,
                stats,
                app.Logger,
                cancellationToken,
                SignalTypes.Metrics,
                ExportMetricsServiceRequest.Parser,
                s => s.IncrementMetrics());
        })
        .WithSummary("OTLP metrics ingestion endpoint")
        .WithDescription("Accepts OTLP/HTTP metrics data in Protobuf format and persists to disk")
        .Produces<ExportMetricsServiceResponse>(200)
        .Produces<object>(400)
        .Produces(500)
        .AddOpenApiOperationTransformer((operation, context, ct) => Task.CompletedTask);
    }

    /// <summary>
    /// Processes an OTLP request generically for traces, logs, or metrics.
    /// This eliminates code duplication across the three OTLP endpoints.
    /// </summary>
    /// <typeparam name="TRequest">The protobuf request type (e.g., ExportTraceServiceRequest)</typeparam>
    /// <typeparam name="TResponse">The protobuf response type (e.g., ExportTraceServiceResponse)</typeparam>
    /// <param name="request">The HTTP request</param>
    /// <param name="writer">The telemetry file writer service</param>
    /// <param name="stats">The telemetry statistics service</param>
    /// <param name="logger">The logger</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="signalType">The signal type (traces, logs, or metrics)</param>
    /// <param name="parser">The protobuf message parser</param>
    /// <param name="incrementStats">Action to increment the appropriate statistics counter</param>
    /// <returns>An HTTP result</returns>
    private static async Task<IResult> ProcessOtlpRequestAsync<TRequest, TResponse>(
        HttpRequest request,
        ITelemetryFileWriter writer,
        ITelemetryStatistics stats,
        Microsoft.Extensions.Logging.ILogger logger,
        CancellationToken cancellationToken,
        string signalType,
        MessageParser<TRequest> parser,
        Action<ITelemetryStatistics> incrementStats)
        where TRequest : IMessage<TRequest>
        where TResponse : IMessage<TResponse>, new()
    {
        try
        {
            using var memoryStream = new MemoryStream();
            await request.Body.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            var otlpRequest = parser.ParseFrom(memoryStream);
            await writer.WriteAsync(otlpRequest, signalType, cancellationToken);
            incrementStats(stats);

            return Results.Ok(new TResponse());
        }
        catch (InvalidProtocolBufferException ex)
        {
            logger.LogError(ex, "Invalid protobuf format in {SignalType} request", signalType);
            return Results.BadRequest(new { error = "Invalid protobuf format" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing {SignalType} request", signalType);
            return Results.StatusCode(500);
        }
    }

    private void ConfigureManagementEndpoints(WebApplication app, int port)
    {
        // Health endpoint
        app.MapGet("/healthz", (IHealthMonitor healthMonitor) =>
        {
            var status = healthMonitor.Status;
            var statusCode = status == HealthStatus.Healthy ? 200 : 503;

            return Results.Json(new
            {
                status = status.ToString().ToLowerInvariant()
            }, statusCode: statusCode);
        })
        .WithSummary("Health check endpoint")
        .WithDescription("Returns health status of the OTLP receiver. Status 200 indicates healthy, 503 indicates unhealthy.")
        .Produces<object>(200)
        .Produces<object>(503)
        .AddOpenApiOperationTransformer((operation, context, ct) => Task.CompletedTask);

        // Create API group for management endpoints
        var apiGroup = app.MapGroup("/api")
            .WithTags("API")
            .AddOpenApiOperationTransformer((operation, context, ct) => Task.CompletedTask);

        // Info endpoint - combines version and diagnostics
        apiGroup.MapGet("/info", (
            IDiagnosticsCollector diagnostics,
            [System.ComponentModel.Description("Optional signal type filter (traces, logs, or metrics)")]
            string? signal) =>
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version ?? new Version(1, 0, 0);
            var fileInfos = diagnostics.GetFileInfo(signal);

            return Results.Json(new
            {
                application = "OpenTelWatcher",
                version = $"{version.Major}.{version.Minor}.{version.Build}",
                versionComponents = new
                {
                    major = version.Major,
                    minor = version.Minor,
                    patch = version.Build
                },
                processId = Environment.ProcessId,
                port = port,
                health = new
                {
                    status = diagnostics.GetHealthStatus().ToString().ToLowerInvariant(),
                    consecutiveErrors = diagnostics.GetConsecutiveErrorCount(),
                    recentErrors = diagnostics.GetRecentErrors()
                },
                files = new
                {
                    count = fileInfos.Count(),
                    totalSizeBytes = fileInfos.Sum(f => f.SizeBytes)
                },
                configuration = new
                {
                    outputDirectory = diagnostics.GetOutputDirectory()
                }
            });
        })
        .WithSummary("Get application information")
        .WithDescription("Returns version, health status, file statistics, and configuration details")
        .Produces<object>(200)
        .AddOpenApiOperationTransformer((operation, context, ct) => Task.CompletedTask);

        // Stats endpoint - telemetry and file statistics
        apiGroup.MapGet("/stats", (
            ITelemetryStatistics telemetryStats,
            IDiagnosticsCollector diagnostics) =>
        {
            var processStartTime = Process.GetCurrentProcess().StartTime;
            var uptimeSeconds = (long)(DateTime.Now - processStartTime).TotalSeconds;

            // Get all files for breakdown
            var allFiles = diagnostics.GetFileInfo(null);
            var traceFiles = allFiles.Where(f => f.Path.Contains("traces.", StringComparison.OrdinalIgnoreCase)).ToList();
            var logFiles = allFiles.Where(f => f.Path.Contains("logs.", StringComparison.OrdinalIgnoreCase)).ToList();
            var metricFiles = allFiles.Where(f => f.Path.Contains("metrics.", StringComparison.OrdinalIgnoreCase)).ToList();

            return Results.Json(new
            {
                telemetry = new
                {
                    traces = new { requests = telemetryStats.TracesReceived },
                    logs = new { requests = telemetryStats.LogsReceived },
                    metrics = new { requests = telemetryStats.MetricsReceived }
                },
                files = new
                {
                    traces = new
                    {
                        count = traceFiles.Count,
                        sizeBytes = traceFiles.Sum(f => f.SizeBytes)
                    },
                    logs = new
                    {
                        count = logFiles.Count,
                        sizeBytes = logFiles.Sum(f => f.SizeBytes)
                    },
                    metrics = new
                    {
                        count = metricFiles.Count,
                        sizeBytes = metricFiles.Sum(f => f.SizeBytes)
                    }
                },
                uptimeSeconds = uptimeSeconds
            });
        })
        .WithSummary("Get telemetry and file statistics")
        .WithDescription("Returns telemetry request counts, file breakdown by type, and uptime")
        .Produces<object>(200)
        .AddOpenApiOperationTransformer((operation, context, ct) => Task.CompletedTask);

        // Shutdown endpoint
        apiGroup.MapPost("/shutdown", async (
            IHostApplicationLifetime lifetime,
            HttpContext context,
            ILogger<WebApplicationHost> logger) =>
        {
            logger.LogWarning("Shutdown requested via /api/shutdown endpoint");

            // Send response immediately
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Shutdown initiated",
                timestamp = DateTime.UtcNow
            });
            await context.Response.CompleteAsync();

            // Trigger shutdown after 1ms delay on background thread
            _ = Task.Run(async () =>
            {
                await Task.Delay(1);
                lifetime.StopApplication();
            });

            return Results.Empty;
        })
        .WithSummary("Initiate graceful shutdown")
        .WithDescription("Triggers application shutdown after responding with 200 OK")
        .Produces<object>(200)
        .AddOpenApiOperationTransformer((operation, context, ct) => Task.CompletedTask);

        // Clear endpoint - deletes all telemetry files
        apiGroup.MapPost("/clear", async (
            ITelemetryFileManager fileManager,
            OpenTelWatcherOptions options,
            CancellationToken cancellationToken) =>
        {
            var filesDeleted = await fileManager.ClearAllFilesAsync(options.OutputDirectory, cancellationToken);

            return Results.Json(new
            {
                success = true,
                filesDeleted,
                message = $"Successfully deleted {filesDeleted} telemetry file(s)",
                timestamp = DateTime.UtcNow
            });
        })
        .WithSummary("Clear all telemetry files")
        .WithDescription("Deletes all NDJSON telemetry files from the output directory. Safe to call while receiving telemetry data.")
        .Produces<object>(200)
        .AddOpenApiOperationTransformer((operation, context, ct) => Task.CompletedTask);

    }

    private void DisplayStartupBanner(ServerOptions options, string outputDirectory, string pidFilePath, IDiagnosticsCollector diagnostics)
    {
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

        // Get existing file statistics
        var fileInfos = diagnostics.GetFileInfo(signal: null);
        var fileCount = fileInfos.Count();
        var totalFileSize = fileInfos.Sum(f => f.SizeBytes);

        var config = new OpenTelWatcher.Utilities.ApplicationInfoConfig
        {
            Version = version,
            Port = options.Port,
            OutputDirectory = outputDirectory,
            PidFilePath = pidFilePath,
            Silent = options.Silent,
            Verbose = options.Verbose,
            FileCount = fileCount,
            TotalFileSize = totalFileSize
        };

        OpenTelWatcher.Utilities.ApplicationInfoDisplay.Display(OpenTelWatcher.Utilities.DisplayMode.Startup, config);

        // Also log for structured logging
        var urls = $"http://localhost:{options.Port}";
        _logger?.LogInformation("Status Dashboard: {Url}/", urls);
        _logger?.LogWarning("WARNING: No authentication enabled. For local development use only.");
    }
}

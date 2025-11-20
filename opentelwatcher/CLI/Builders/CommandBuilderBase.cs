using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelWatcher.CLI.Commands;
using OpenTelWatcher.CLI.Services;
using OpenTelWatcher.Configuration;
using OpenTelWatcher.Hosting;
using OpenTelWatcher.Services;
using OpenTelWatcher.Services.Interfaces;
using System.Text.Json;

namespace OpenTelWatcher.CLI.Builders;

/// <summary>
/// Base class for all CLI command builders, providing shared infrastructure for command construction.
/// Centralizes configuration loading (appsettings.json), dependency injection setup, and port resolution.
/// Derived builders inherit BuildServiceProvider() for DI, GetDefaultOutputDirectory() for config,
/// and ResolvePortForCommand() for PID file-based port auto-detection. Each builder constructs
/// System.CommandLine options and wiring, then delegates execution to injected Command classes.
/// </summary>
/// <remarks>
/// Scope: CLI argument parsing, validation, and Command class orchestration (no business logic).
/// </remarks>
public abstract class CommandBuilderBase : ICommandBuilder
{
    protected readonly IEnvironment Environment;

    protected CommandBuilderBase(IEnvironment environment)
    {
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <summary>
    /// Gets the default output directory from configuration.
    /// Reads from appsettings.json/appsettings.Development.json using standard .NET configuration system.
    /// </summary>
    protected string GetDefaultOutputDirectory()
    {
        var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                           ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                           ?? "Production";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Environment.CurrentDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
            .Build();

        return configuration["OpenTelWatcher:OutputDirectory"] ?? "./telemetry-data";
    }

    /// <summary>
    /// Builds a service provider for command execution with all necessary dependencies.
    /// </summary>
    protected IServiceProvider BuildServiceProvider(int port)
    {
        var services = new ServiceCollection();

        // HttpClient for API communication
        services.AddHttpClient<IOpenTelWatcherApiClient, OpenTelWatcherApiClient>(client =>
        {
            client.BaseAddress = new Uri($"http://{ApiConstants.Network.LocalhostIp}:{port}");
            // Short timeout for CLI commands - if service doesn't respond quickly, it's likely not running
            client.Timeout = TimeSpan.FromSeconds(ApiConstants.Timeouts.ApiRequestSeconds);
        });

        // System abstraction services (for testability)
        services.AddSingleton<IEnvironment, EnvironmentAdapter>();
        services.AddSingleton<IProcessProvider, ProcessProvider>();
        services.AddSingleton<ITimeProvider, SystemTimeProvider>();

        // Web application host (production implementation)
        services.AddSingleton<IWebApplicationHost, WebApplicationHost>();

        // PID file service
        services.AddSingleton<IPidFileService, PidFileService>();

        // Port resolution service
        services.AddSingleton<IPortResolver, PortResolver>();

        // Error file scanner service
        services.AddSingleton<IErrorFileScanner, ErrorFileScanner>();

        // Logging
        services.AddLogging();

        // Command handlers
        services.AddTransient<StartCommand>();
        services.AddTransient<StopCommand>();
        services.AddTransient<StatusCommand>();
        services.AddTransient<ClearCommand>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Resolves the port to use for a command. If explicit port is provided, returns it.
    /// Otherwise, attempts to auto-resolve from PID file.
    /// </summary>
    /// <param name="explicitPort">Explicitly provided port, or null for auto-resolution</param>
    /// <param name="silent">Whether to suppress console output on error</param>
    /// <param name="jsonOutput">Whether to output errors in JSON format</param>
    /// <param name="allowFallback">Whether to fallback to default port on resolution failure (for standalone modes)</param>
    /// <returns>Tuple of (resolvedPort, shouldContinue). shouldContinue=false means an error was already reported.</returns>
    protected (int port, bool shouldContinue) ResolvePortForCommand(
        int? explicitPort,
        bool silent = false,
        bool jsonOutput = false,
        bool allowFallback = false)
    {
        // If explicit port provided, use it
        if (explicitPort.HasValue)
            return (explicitPort.Value, true);

        // Attempt auto-resolution from PID file
        var tempServices = BuildServiceProvider(DefaultPorts.Otlp);
        var portResolver = tempServices.GetRequiredService<IPortResolver>();

        try
        {
            var resolvedPort = portResolver.ResolvePort(null);
            return (resolvedPort, true);
        }
        catch (InvalidOperationException ex)
        {
            // Auto-resolution failed
            if (allowFallback)
            {
                // Fallback to default port for standalone modes
                return (DefaultPorts.Otlp, true);
            }

            // Report error to user
            if (!silent && !jsonOutput)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            else if (jsonOutput)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    error = ex.Message
                }));
            }
            return (DefaultPorts.Otlp, false); // Return default port but signal not to continue
        }
    }

    /// <summary>
    /// Builds and returns a configured Command instance.
    /// </summary>
    public abstract System.CommandLine.Command Build();
}

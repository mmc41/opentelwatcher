using NLog;
using NLog.Web;
using OpenTelWatcher.CLI;
using OpenTelWatcher.Services;
using OpenTelWatcher.Services.Interfaces;

// Early init of NLog to allow startup and exception logging
// Make NLog.config optional - use default configuration if file doesn't exist
var configFileExists = File.Exists("NLog.config");
var logger = configFileExists
    ? LogManager.Setup().LoadConfigurationFromFile().GetCurrentClassLogger()
    : LogManager.Setup().GetCurrentClassLogger();

if (!configFileExists)
{
    logger.Info("NLog.config not found - using default NLog configuration");
}

logger.Debug("init main");

// PID file service reference for cleanup in finally block
IPidFileService? pidFileService = null;

try
{
    // ALL execution goes through CliApplication
    // System.CommandLine handles all argument parsing and routing
    // Note: IEnvironment is needed early for configuration loading, so we instantiate it here
    var environment = new EnvironmentAdapter();
    var cliApp = new CliApplication(environment);
    return await cliApp.RunAsync(args);
}
catch (Exception ex)
{
    // NLog: catch setup errors
    logger.Error(ex, "Stopped program because of exception");
    throw;
}
finally
{
    // Cleanup PID file if registered (handles startup exceptions and other failure cases)
    pidFileService?.Unregister();

    // Ensure to flush and stop internal timers/threads before application-exit
    LogManager.Shutdown();
}

public partial class Program { }

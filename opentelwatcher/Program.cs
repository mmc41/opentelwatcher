using NLog;
using NLog.Web;
using OpenTelWatcher.CLI;
using OpenTelWatcher.Services.Interfaces;

// Early init of NLog to allow startup and exception logging
// Make NLog.config optional - use default configuration if file doesn't exist
var logger = File.Exists("NLog.config")
    ? LogManager.Setup().LoadConfigurationFromFile().GetCurrentClassLogger()
    : LogManager.Setup().GetCurrentClassLogger();
logger.Debug("init main");

// PID file service reference for cleanup in finally block
IPidFileService? pidFileService = null;

try
{
    // ALL execution goes through CliApplication
    // System.CommandLine handles all argument parsing and routing
    var cliApp = new CliApplication();
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

using OpenTelWatcher.Hosting;

namespace UnitTests.Mocks;

/// <summary>
/// Mock implementation of IWebApplicationHost for unit testing.
/// Records all method calls and returns configurable results.
/// </summary>
public class MockWebApplicationHost : IWebApplicationHost
{
    /// <summary>
    /// Records all calls to RunAsync with their options.
    /// </summary>
    public List<ServerOptions> RunCalls { get; } = new();

    /// <summary>
    /// The validation result to return from Validate().
    /// </summary>
    public ValidationResult ValidationResultToReturn { get; set; } = ValidationResult.Success();

    /// <summary>
    /// The exit code to return from RunAsync().
    /// </summary>
    public int ExitCodeToReturn { get; set; } = 0;

    /// <summary>
    /// If set, RunAsync will throw this exception.
    /// </summary>
    public Exception? ExceptionToThrow { get; set; }

    /// <summary>
    /// If true, RunAsync will block indefinitely (simulating server running).
    /// </summary>
    public bool BlockIndefinitely { get; set; } = false;

    public Task<int> RunAsync(ServerOptions options, CancellationToken cancellationToken = default)
    {
        RunCalls.Add(options);

        if (ExceptionToThrow != null)
        {
            throw ExceptionToThrow;
        }

        if (BlockIndefinitely)
        {
            // Simulate server running until cancellation
            return Task.Run(async () =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return ExitCodeToReturn;
            }, cancellationToken);
        }

        return Task.FromResult(ExitCodeToReturn);
    }

    public ValidationResult Validate(ServerOptions options)
    {
        return ValidationResultToReturn;
    }
}

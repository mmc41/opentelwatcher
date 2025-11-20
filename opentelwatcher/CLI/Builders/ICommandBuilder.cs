using System.CommandLine;

namespace OpenTelWatcher.CLI.Builders;

/// <summary>
/// Defines the contract for CLI command builders in the builder pattern architecture.
/// Builders construct System.CommandLine Command instances with options, validators, and handlers.
/// Each builder creates the argument parsing structure, while the corresponding Command class
/// (e.g., StartCommand) contains the business logic. This separation keeps CliApplication minimal
/// and makes commands independently testable through dependency injection.
/// </summary>
/// <remarks>
/// Relationship: Builder creates Command → Command executes → Results returned to user
/// </remarks>
public interface ICommandBuilder
{
    /// <summary>
    /// Builds and returns a configured Command instance.
    /// </summary>
    Command Build();
}

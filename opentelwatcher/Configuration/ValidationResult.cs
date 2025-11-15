namespace OpenTelWatcher.Configuration;

/// <summary>
/// Result of configuration validation.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Indicates whether the configuration is valid.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// List of validation error messages.
    /// </summary>
    public List<string> Errors { get; set; } = new();
}

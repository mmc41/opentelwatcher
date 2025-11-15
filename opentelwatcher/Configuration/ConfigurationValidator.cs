namespace OpenTelWatcher.Configuration;

/// <summary>
/// Validates OpenTelWatcher configuration options.
/// </summary>
public static class ConfigurationValidator
{
    /// <summary>
    /// Validates the provided configuration options.
    /// </summary>
    /// <param name="options">Configuration options to validate.</param>
    /// <returns>Validation result containing success status and any error messages.</returns>
    public static ValidationResult Validate(OpenTelWatcherOptions options)
    {
        var errors = new List<string>();

        // Validate OutputDirectory
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            errors.Add("OutputDirectory cannot be null or whitespace");
        }

        // Validate MaxErrorHistorySize range (10-1000)
        if (options.MaxErrorHistorySize < 10 || options.MaxErrorHistorySize > 1000)
        {
            errors.Add("MaxErrorHistorySize must be between 10 and 1000");
        }

        // Validate MaxConsecutiveFileErrors range (3-100)
        if (options.MaxConsecutiveFileErrors < 3 || options.MaxConsecutiveFileErrors > 100)
        {
            errors.Add("MaxConsecutiveFileErrors must be between 3 and 100");
        }

        // Validate MaxFileSizeMB (must be positive)
        if (options.MaxFileSizeMB <= 0)
        {
            errors.Add("MaxFileSizeMB must be greater than 0");
        }

        // Validate RequestTimeoutSeconds (must be positive)
        if (options.RequestTimeoutSeconds <= 0)
        {
            errors.Add("RequestTimeoutSeconds must be greater than 0");
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }
}

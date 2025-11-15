namespace OpenTelWatcher.Serialization;

/// <summary>
/// Formats JSON strings as NDJSON (Newline Delimited JSON) lines.
/// </summary>
public static class NdjsonWriter
{
    /// <summary>
    /// Formats a JSON string as an NDJSON line by appending a newline.
    /// </summary>
    /// <param name="json">JSON string to format.</param>
    /// <returns>NDJSON line with newline terminator.</returns>
    /// <exception cref="ArgumentException">Thrown if json is null or whitespace.</exception>
    public static string FormatAsNdjsonLine(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("JSON cannot be null or whitespace", nameof(json));
        }

        // Ensure exactly one newline at the end
        // NDJSON format: one complete JSON object per line, separated by \n
        return json.TrimEnd('\n', '\r') + "\n";
    }
}

using System.Globalization;

namespace OpenTelWatcher.Utilities;

public static class NumberFormatter
{
    /// <summary>
    /// Formats a count with abbreviated notation (K, M).
    /// </summary>
    /// <param name="count">The count to format.</param>
    /// <returns>Formatted string (e.g., "1.2K", "1.2M").</returns>
    public static string FormatCount(long count)
    {
        if (count < 1000) return count.ToString(CultureInfo.InvariantCulture);
        if (count < 1000000) return (count / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + "K";
        return (count / 1000000.0).ToString("F1", CultureInfo.InvariantCulture) + "M";
    }

    /// <summary>
    /// Formats bytes into a human-readable size string (B, KB, MB, GB).
    /// </summary>
    /// <param name="bytes">The number of bytes to format.</param>
    /// <returns>Formatted string (e.g., "1.23 KB", "456.78 MB").</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when bytes is negative.</exception>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes), "Bytes cannot be negative");

        if (bytes < 1024)
            return $"{bytes} B";

        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F2} KB";

        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):F2} MB";

        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}

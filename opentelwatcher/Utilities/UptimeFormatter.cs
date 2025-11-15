namespace OpenTelWatcher.Utilities;

public static class UptimeFormatter
{
    /// <summary>
    /// Formats uptime as "Xh Ym Zs" or "Xd Yh Zm" for long uptimes.
    /// </summary>
    /// <param name="uptime">The uptime duration.</param>
    /// <returns>Formatted string (e.g., "2h 15m 32s").</returns>
    public static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
        if (uptime.TotalMinutes >= 1)
            return $"{uptime.Minutes}m {uptime.Seconds}s";
        return $"{uptime.Seconds}s";
    }
}

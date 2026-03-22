using System.Globalization;

namespace Trion.Core.Monitoring;

/// <summary>Human-readable formatting helpers for metric values.</summary>
public static class MetricsFormatter
{
    private static readonly CultureInfo _inv = CultureInfo.InvariantCulture;

    public static string FormatBps(long bps)
    {
        if (bps >= 1_073_741_824L) return (bps / 1_073_741_824.0).ToString("F2", _inv) + " GB/s";
        if (bps >= 1_048_576L)     return (bps / 1_048_576.0).ToString("F1", _inv) + " MB/s";
        if (bps >= 1_024L)         return (bps / 1_024.0).ToString("F1", _inv) + " KB/s";
        return bps.ToString(_inv) + " B/s";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824L) return (bytes / 1_073_741_824.0).ToString("F1", _inv) + " GB";
        if (bytes >= 1_048_576L)     return (bytes / 1_048_576.0).ToString("F0", _inv) + " MB";
        if (bytes >= 1_024L)         return (bytes / 1_024.0).ToString("F0", _inv) + " KB";
        return bytes.ToString(_inv) + " B";
    }
}

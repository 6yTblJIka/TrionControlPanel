using System.Runtime.InteropServices;
using System.Text.Json;

namespace Trion.Desktop.Services;

/// <summary>
/// Collects minimal, non-personal hardware and OS information for opt-in telemetry.
/// Data sent: OS description, CPU architecture, logical core count, total RAM (GB),
/// .NET runtime version, and app version. No user identifiers, no file paths.
/// </summary>
public static class HardwareInfoService
{
    public static string Collect(string appVersion)
    {
        var info = new
        {
            os        = RuntimeInformation.OSDescription,
            arch      = RuntimeInformation.OSArchitecture.ToString(),
            cpu_cores = Environment.ProcessorCount,
            ram_gb    = GetRamGb(),
            dotnet    = Environment.Version.ToString(3),
            app_ver   = appVersion,
        };

        return JsonSerializer.Serialize(info);
    }

    private static int GetRamGb()
    {
        try
        {
            var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            return (int) Math.Round(bytes / (1024.0 * 1024 * 1024));
        }
        catch
        {
            return 0;
        }
    }
}

using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using Trion.Desktop.Models;

namespace Trion.Desktop.Infrastructure.Helpers;

/// <summary>
/// Result of the pre-flight port and process check run at startup.
/// </summary>
public sealed class StartupPortCheckResult
{
    public bool      MySqlPortBlocked  { get; init; }
    public bool      HttpPortBlocked   { get; init; }
    public bool      HttpsPortBlocked  { get; init; }
    public int       MySqlPort         { get; init; }
    public int       HttpPort          { get; init; }
    public int       HttpsPort         { get; init; }
    /// <summary>
    /// True when the MySQL port is occupied by what appears to be a pre-existing
    /// system MySQL/MariaDB server rather than a conflicting unrelated process.
    /// In this case the port is NOT treated as blocked — Trion will configure itself
    /// to use the running server instead of installing a portable copy.
    /// </summary>
    public bool      SystemMySqlDetected { get; init; }
    /// <summary>
    /// MySQL processes found running when the MySQL port is NOT in use.
    /// These are orphan/stale processes that may cause I/O contention.
    /// </summary>
    public Process[] OrphanMySql       { get; init; } = [];

    public bool AnyPortBlocked => MySqlPortBlocked || HttpPortBlocked || HttpsPortBlocked;
}

/// <summary>
/// Pre-flight startup check: verifies required ports are free and detects
/// orphan MySQL processes before the main window is shown.
/// </summary>
public static class StartupPortCheck
{
    /// <param name="portableInstalled">
    /// Whether the Trion-managed portable MySQL is already installed.
    /// When <c>false</c> and the MySQL port is in use, Trion treats the occupying
    /// process as an existing system MySQL rather than a conflict.
    /// </param>
    public static StartupPortCheckResult Run(AppSettings settings, bool portableInstalled)
    {
        var mysqlPort  = int.TryParse(settings.DBServerPort, out var p) ? p : 3306;
        var httpPort   = settings.WebServerPort;
        var httpsPort  = settings.HttpsPort;
        var checkHttp  = settings.WebServerAutoStart;
        var checkHttps = checkHttp && settings.EnableHttps;

        var activePorts = GetActiveTcpPorts();

        var mysqlPortOccupied = activePorts.Contains(mysqlPort);
        var httpBlocked  = checkHttp  && activePorts.Contains(httpPort);
        var httpsBlocked = checkHttps && activePorts.Contains(httpsPort);

        // If the MySQL port is occupied but portable MySQL isn't installed yet,
        // a system server is running — not a conflict, an opportunity to reuse it.
        var systemMySql  = mysqlPortOccupied && !portableInstalled;
        var mysqlBlocked = mysqlPortOccupied && !systemMySql;

        // Only look for orphan processes when the port is free.
        var orphans = mysqlPortOccupied ? [] : FindMySqlProcesses(settings.DBExeName);

        return new StartupPortCheckResult
        {
            MySqlPortBlocked   = mysqlBlocked,
            HttpPortBlocked    = httpBlocked,
            HttpsPortBlocked   = httpsBlocked,
            SystemMySqlDetected = systemMySql,
            MySqlPort          = mysqlPort,
            HttpPort           = httpPort,
            HttpsPort          = httpsPort,
            OrphanMySql        = orphans,
        };
    }

    private static HashSet<int> GetActiveTcpPorts()
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Select(ep => ep.Port)
                .ToHashSet();
        }
        catch { return []; }
    }

    private static Process[] FindMySqlProcesses(string exeName)
    {
        var configured = Path.GetFileNameWithoutExtension(
            string.IsNullOrWhiteSpace(exeName) ? "mysqld" : exeName);

        var results = new List<Process>(Process.GetProcessesByName(configured));

        // Always include the default mysqld name as a fallback in case the
        // configured exe name differs from the running process name.
        if (!string.Equals(configured, "mysqld", StringComparison.OrdinalIgnoreCase))
            results.AddRange(Process.GetProcessesByName("mysqld"));

        return [.. results.DistinctBy(pr => pr.Id)];
    }
}

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Trion.Core.Database;

namespace Trion.Platform.Linux;

/// <summary>
/// MySQL lifecycle service for Linux.
/// Installs MySQL or MariaDB via the system package manager (apt-get / dnf / yum),
/// manages the systemd service, and runs first-time database setup identical to
/// the Windows portable version.
/// </summary>
public sealed class LinuxMySqlService : IMySqlSetupService
{
    private readonly ILogger<LinuxMySqlService> _log;
    private readonly DistroProfile              _profile;

    // -- State -----------------------------------------------------------------

    public bool      IsInstalled        => CheckInstalled();
    public bool      IsUsingSystemMySQL => false;   // Linux always uses the system package
    public bool      IsRunning          { get; private set; }
    public string    LastMessage     { get; private set; } = string.Empty;
    public DownloadProgress? CurrentDownload => null;  // package manager handles downloads
    public bool      IsProcessRunning => CheckServiceActive();
    public int?      ProcessId        => ReadPidFile();
    public DateTime? StartTime        { get; private set; }
    public TimeSpan  Uptime           => StartTime.HasValue
        ? DateTime.UtcNow - StartTime.Value
        : TimeSpan.Zero;

    // -- Events ----------------------------------------------------------------

    public event Action<string>?            ProgressChanged;
#pragma warning disable CS0067 // interface requires the event; package manager never raises download progress
    public event Action<DownloadProgress?>? DownloadProgressChanged;
#pragma warning restore CS0067
    public event Action<bool>?              StatusChanged;

    // -- Construction ----------------------------------------------------------

    public LinuxMySqlService(ILogger<LinuxMySqlService> logger)
    {
        _log     = logger;
        _profile = DistroProfile.Detect();
    }

    // -- RunStartupCheckAsync --------------------------------------------------

    public async Task RunStartupCheckAsync(CancellationToken ct = default)
    {
        if (IsRunning) return;
        IsRunning = true;

        try
        {
            if (!IsInstalled)
            {
                Report($"MySQL/MariaDB not found -- installing '{_profile.PackageName}' via {_profile.Manager}...");
                await InstallPackageAsync(ct);

                Report($"Enabling {_profile.ServiceName} service on boot...");
                await RunShellAsync($"systemctl enable {_profile.ServiceName}", ct);
            }

            if (!IsProcessRunning)
            {
                Report($"Starting {_profile.ServiceName}...");
                await StartAsync(ct: ct);
            }

            if (!await IsInitializedAsync(ct))
            {
                Report("Running first-time database setup...");
                await RunFirstTimeSetupAsync(ct);
                Report("Database accounts and schemas created successfully.");
            }

            Report("MySQL is ready.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "RunStartupCheckAsync failed.");
            Report($"Error: {ex.Message}");
            throw;
        }
        finally
        {
            IsRunning = false;
        }
    }

    // -- GenerateMyIni -- no-op on Linux ----------------------------------------

    /// <inheritdoc/>
    /// <remarks>No-op on Linux. MySQL uses /etc/mysql/conf.d/ or /etc/my.cnf.</remarks>
    public void GenerateMyIni() { }

    // -- Lifecycle -------------------------------------------------------------

    public async Task StartAsync(bool? hideConsole = null, CancellationToken ct = default)
    {
        if (IsProcessRunning) return;

        await RunShellAsync($"systemctl start {_profile.ServiceName}", ct);
        await WaitForServiceAsync(ct);

        StartTime = DateTime.UtcNow;
        StatusChanged?.Invoke(true);
        Report($"{_profile.ServiceName} started.");
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!IsProcessRunning) return;

        await RunShellAsync($"systemctl stop {_profile.ServiceName}", ct);

        StartTime = null;
        StatusChanged?.Invoke(false);
        Report($"{_profile.ServiceName} stopped.");
    }

    public async Task RepairAsync(CancellationToken ct = default)
    {
        Report($"Restarting {_profile.ServiceName}...");
        await RunShellAsync($"systemctl restart {_profile.ServiceName}", ct);
        await WaitForServiceAsync(ct);

        StartTime = IsProcessRunning ? DateTime.UtcNow : null;
        StatusChanged?.Invoke(IsProcessRunning);
        Report("MySQL restarted.");
    }

    public async Task UninstallAsync(CancellationToken ct = default)
    {
        if (IsProcessRunning)
        {
            Report("Stopping MySQL before uninstall...");
            await StopAsync(ct);
        }

        Report($"Removing package '{_profile.PackageName}'...");
        string cmd = _profile.Manager switch
        {
            PackageManager.Apt => $"apt-get remove -y {_profile.PackageName}",
            PackageManager.Dnf => $"dnf remove -y {_profile.PackageName}",
            PackageManager.Yum => $"yum remove -y {_profile.PackageName}",
            _                  => throw new NotSupportedException()
        };
        await RunShellAsync(cmd, ct);
        Report("MySQL removed. Database files in /var/lib/mysql are preserved.");
    }

    public async Task ReinstallAsync(CancellationToken ct = default)
    {
        await UninstallAsync(ct);
        await RunStartupCheckAsync(ct);
    }

    // -- Package installation --------------------------------------------------

    private async Task InstallPackageAsync(CancellationToken ct)
    {
        if (_profile.Manager == PackageManager.Apt)
        {
            Report("Updating package index...");
            await RunShellAsync("apt-get update -y", ct);
        }
        else
        {
            // RHEL-based: MySQL Community Server is not in the default AppStream repos.
            // Add the official MySQL 8.0 Community repository first.
            await EnsureMySqlRepoAsync(ct);
        }

        string cmd = _profile.Manager switch
        {
            PackageManager.Apt => $"apt-get install -y {_profile.PackageName}",
            PackageManager.Dnf => $"dnf install -y {_profile.PackageName}",
            PackageManager.Yum => $"yum install -y {_profile.PackageName}",
            _                  => throw new NotSupportedException()
        };

        Report($"Installing {_profile.PackageName}...");
        await RunShellAsync(cmd, ct);
        Report($"{_profile.PackageName} installed.");
    }

    /// <summary>
    /// Adds the MySQL 8.0 Community repository on RHEL-based systems (el7/el8/el9)
    /// if it is not already configured. Also disables the AppStream MySQL module on
    /// RHEL 8+ to prevent package conflicts.
    /// </summary>
    private async Task EnsureMySqlRepoAsync(CancellationToken ct)
    {
        // Check if mysql-community-server is already available in configured repos
        var (code, _, _) = await RunShellCaptureAsync(
            "rpm -q mysql-community-server 2>/dev/null || " +
            "yum list available mysql-community-server 2>/dev/null | grep -q mysql-community-server",
            ct);

        if (code == 0)
        {
            Report("MySQL Community repo already configured.");
            return;
        }

        int el = GetRhelMajorVersion();
        string rpmUrl = $"https://dev.mysql.com/get/mysql80-community-release-el{el}-1.noarch.rpm";
        Report($"Adding MySQL 8.0 Community repository for RHEL/CentOS el{el}...");

        string addRepoCmd = _profile.Manager == PackageManager.Dnf
            ? $"dnf install -y \"{rpmUrl}\""
            : $"yum install -y \"{rpmUrl}\"";
        await RunShellAsync(addRepoCmd, ct);

        // RHEL 8+ has a conflicting 'mysql' module in AppStream -- disable it
        if (el >= 8 && _profile.Manager == PackageManager.Dnf)
        {
            Report("Disabling conflicting AppStream MySQL module...");
            await RunShellAsync("dnf module disable mysql -y", ct);
        }
    }

    /// <summary>Reads the major OS version (7, 8, 9) from /etc/os-release.</summary>
    private static int GetRhelMajorVersion()
    {
        try
        {
            foreach (var line in File.ReadLines("/etc/os-release"))
            {
                if (!line.StartsWith("VERSION_ID=")) continue;
                var val = line["VERSION_ID=".Length..].Trim('"');
                if (int.TryParse(val.Split('.')[0], out int major))
                    return major;
            }
        }
        catch { /* ignored */ }
        return 9; // safe default -- el9 is the current mainstream
    }

    // -- First-time database setup ---------------------------------------------

    /// <summary>
    /// Returns true when the 'phoenix' application user already exists
    /// (i.e., first-run setup was previously completed).
    /// </summary>
    private async Task<bool> IsInitializedAsync(CancellationToken ct)
    {
        try
        {
            var csb = new MySqlConnectionStringBuilder
            {
                Server            = "127.0.0.1",
                Port              = 3306,
                UserID            = "phoenix",
                Password          = "phoenix",
                ConnectionTimeout = 5,
                SslMode           = MySqlSslMode.None,
            };
            await using var conn = new MySqlConnection(csb.ConnectionString);
            await conn.OpenAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task RunFirstTimeSetupAsync(CancellationToken ct)
    {
        // Write MySQL-compatible init SQL to a temp file.
        // FirstRunSetupLinux uses IDENTIFIED BY (no explicit auth plugin) which works on
        // MySQL 8.0 Community Server on both Debian/Ubuntu and RHEL/CentOS.
        var tmpSql = Path.Combine(Path.GetTempPath(), $"trion_init_{Guid.NewGuid():N}.sql");
        try
        {
            await File.WriteAllTextAsync(tmpSql, MySqlInitSql.FirstRunSetupLinux, ct);

            // Strategy 1: connect as root without password via TCP
            // (RHEL MySQL Community: no root password set immediately after install)
            var (code, _, err) = await RunShellCaptureAsync(
                $"mysql -u root -h 127.0.0.1 -P 3306 < \"{tmpSql}\"", ct);

            // Strategy 2: connect via Unix socket (Ubuntu/Debian -- root uses auth_socket plugin)
            if (code != 0)
                (code, _, err) = await RunShellCaptureAsync(
                    $"mysql -u root --socket=\"{_profile.SocketPath}\" < \"{tmpSql}\"", ct);

            // Strategy 3: sudo mysql (running as non-root user on Ubuntu/Debian)
            if (code != 0)
                (code, _, err) = await RunShellCaptureAsync(
                    $"sudo mysql < \"{tmpSql}\"", ct);

            // Strategy 4: RHEL MySQL Community Server -- temporary root password in log
            if (code != 0)
            {
                string tempPwd = ReadTempPassword(_profile.LogFile);
                if (!string.IsNullOrEmpty(tempPwd))
                    (code, _, err) = await RunShellCaptureAsync(
                        $"mysql -u root -p\"{tempPwd}\" --connect-expired-password < \"{tmpSql}\"", ct);
            }

            if (code != 0)
                throw new InvalidOperationException(
                    $"MySQL first-time setup failed (exit {code}). " +
                    $"Ensure this process has permission to connect as root. Details: {err.Trim()}");
        }
        finally
        {
            if (File.Exists(tmpSql)) File.Delete(tmpSql);
        }
    }

    /// <summary>
    /// Reads the temporary root password generated by MySQL Community Server
    /// on first install (RHEL/CentOS). Returns empty string on Debian/Ubuntu.
    /// </summary>
    private static string ReadTempPassword(string logFile)
    {
        try
        {
            if (!File.Exists(logFile)) return string.Empty;

            string? match = null;
            foreach (var line in File.ReadLines(logFile))
                if (line.Contains("temporary password")) match = line;

            if (match is null) return string.Empty;
            int idx = match.LastIndexOf(' ');
            return idx >= 0 ? match[(idx + 1)..].Trim() : string.Empty;
        }
        catch { return string.Empty; }
    }

    // -- Status helpers --------------------------------------------------------

    private bool CheckInstalled()
    {
        return File.Exists("/usr/sbin/mysqld")
            || File.Exists("/usr/libexec/mysqld")
            || File.Exists("/usr/local/sbin/mysqld");
    }

    private bool CheckServiceActive()
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo("systemctl")
                {
                    UseShellExecute = false,
                }
            };
            proc.StartInfo.ArgumentList.Add("is-active");
            proc.StartInfo.ArgumentList.Add("--quiet");
            proc.StartInfo.ArgumentList.Add(_profile.ServiceName);

            proc.Start();
            proc.WaitForExit(5_000);
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    private int? ReadPidFile()
    {
        try
        {
            foreach (var pidFile in _profile.PidFiles)
            {
                if (File.Exists(pidFile)
                    && int.TryParse(File.ReadAllText(pidFile).Trim(), out int pid))
                    return pid;
            }
            return null;
        }
        catch { return null; }
    }

    private async Task WaitForServiceAsync(CancellationToken ct, int timeoutSec = 30)
    {
        for (int i = 0; i < timeoutSec; i++)
        {
            if (CheckServiceActive()) return;
            await Task.Delay(1_000, ct);
        }
        throw new TimeoutException(
            $"MySQL service '{_profile.ServiceName}' did not become active within {timeoutSec} s.");
    }

    // -- Shell execution -------------------------------------------------------

    private async Task RunShellAsync(string command, CancellationToken ct)
    {
        var (code, _, err) = await RunShellCaptureAsync(command, ct);
        if (code != 0)
            throw new InvalidOperationException(
                $"Shell command failed (exit {code}): {err.Trim()}");
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)>
        RunShellCaptureAsync(string command, CancellationToken ct)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo("bash")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            }
        };
        proc.StartInfo.ArgumentList.Add("-c");
        proc.StartInfo.ArgumentList.Add(command);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync(ct);

        return (proc.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private void Report(string message)
    {
        LastMessage = message;
        _log.LogInformation("{Message}", message);
        ProgressChanged?.Invoke(message);
    }

    // -- Distro detection ------------------------------------------------------

    private enum PackageManager { Apt, Dnf, Yum }

    private sealed record DistroProfile(
        PackageManager Manager,
        string         PackageName,
        string         ServiceName,
        string[]       PidFiles,
        string         SocketPath,
        string         LogFile)
    {
        /// <summary>
        /// Auto-detects the package manager and returns the appropriate profile.
        /// Detection order: apt-get -> dnf -> yum.
        /// </summary>
        internal static DistroProfile Detect()
        {
            // Debian / Ubuntu -- MySQL 8
            if (File.Exists("/usr/bin/apt-get") || File.Exists("/bin/apt-get"))
                return new(
                    Manager:     PackageManager.Apt,
                    PackageName: "mysql-server",
                    ServiceName: "mysql",
                    PidFiles:    ["/run/mysqld/mysqld.pid", "/var/run/mysqld/mysqld.pid"],
                    SocketPath:  "/run/mysqld/mysqld.sock",
                    LogFile:     "/var/log/mysql/error.log");

            // RHEL 8/9 / CentOS Stream / AlmaLinux / Rocky -- MySQL Community Server
            // (repo added automatically by EnsureMySqlRepoAsync if not present)
            if (File.Exists("/usr/bin/dnf"))
                return new(
                    Manager:     PackageManager.Dnf,
                    PackageName: "mysql-community-server",
                    ServiceName: "mysqld",
                    PidFiles:    ["/var/run/mysqld/mysqld.pid", "/run/mysqld/mysqld.pid"],
                    SocketPath:  "/var/lib/mysql/mysql.sock",
                    LogFile:     "/var/log/mysqld.log");

            // Older RHEL / CentOS 7 -- MySQL Community Server via yum
            if (File.Exists("/usr/bin/yum"))
                return new(
                    Manager:     PackageManager.Yum,
                    PackageName: "mysql-community-server",
                    ServiceName: "mysqld",
                    PidFiles:    ["/var/run/mysqld/mysqld.pid"],
                    SocketPath:  "/var/lib/mysql/mysql.sock",
                    LogFile:     "/var/log/mysqld.log");

            throw new PlatformNotSupportedException(
                "No supported Linux package manager found (apt-get, dnf, yum).");
        }
    }
}

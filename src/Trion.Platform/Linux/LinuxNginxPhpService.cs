using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Trion.Core.Database;
using Trion.Core.WebServer;

namespace Trion.Platform.Linux;

/// <summary>
/// Nginx + PHP-FPM lifecycle service for Linux.
/// Installs both via the system package manager (apt-get / dnf / yum),
/// manages their systemd services, and keeps them in sync.
/// </summary>
public sealed class LinuxNginxPhpService : INginxPhpSetupService
{
    private readonly ILogger<LinuxNginxPhpService> _log;
    private readonly DistroProfile                 _profile;

    // -- State -----------------------------------------------------------------

    public bool              IsInstalled      => CheckInstalled();
    public bool              IsRunning        { get; private set; }
    public string            LastMessage      { get; private set; } = string.Empty;
    public DownloadProgress? CurrentDownload  => null;  // package manager handles downloads
    public bool              IsProcessRunning => CheckServiceActive(_profile.NginxServiceName);
    public int?              ProcessId        => ReadNginxPid();
    public DateTime?         StartTime        { get; private set; }
    public TimeSpan          Uptime           => StartTime.HasValue
        ? DateTime.UtcNow - StartTime.Value
        : TimeSpan.Zero;

    // -- Events ----------------------------------------------------------------

    public event Action<string>? ProgressChanged;
#pragma warning disable CS0067  // interface requires the event; package manager never raises download progress
    public event Action<DownloadProgress?>? DownloadProgressChanged;
#pragma warning restore CS0067
    public event Action<bool>? StatusChanged;

    // -- Construction ----------------------------------------------------------

    public LinuxNginxPhpService(ILogger<LinuxNginxPhpService> logger)
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
                Report($"Nginx and PHP-FPM not found -- installing via {_profile.Manager}...");
                await InstallPackagesAsync(ct);

                Report($"Enabling {_profile.NginxServiceName} on boot...");
                await RunShellAsync($"systemctl enable {_profile.NginxServiceName}", ct);

                Report($"Enabling {_profile.PhpFpmServiceName} on boot...");
                await RunShellAsync($"systemctl enable {_profile.PhpFpmServiceName}", ct);
            }

            if (!IsProcessRunning)
            {
                Report("Starting web server...");
                await StartAsync(ct: ct);
            }

            Report("Web server is ready.");
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

    // -- Config / cert generation -- no-op on Linux ----------------------------

    /// <inheritdoc/>
    /// <remarks>No-op on Linux. nginx uses /etc/nginx/ configuration.</remarks>
    public void GenerateNginxConf() { }

    /// <inheritdoc/>
    /// <remarks>No-op on Linux. Use certbot or the system CA for TLS certificates.</remarks>
    public void GenerateSelfSignedCert() { }

    // -- Lifecycle -------------------------------------------------------------

    public async Task StartAsync(bool? hideConsole = null, CancellationToken ct = default)
    {
        if (IsProcessRunning) return;

        // Start PHP-FPM before nginx so FastCGI socket is ready
        await RunShellAsync($"systemctl start {_profile.PhpFpmServiceName}", ct);
        await RunShellAsync($"systemctl start {_profile.NginxServiceName}", ct);
        await WaitForServiceAsync(_profile.NginxServiceName, ct);

        StartTime = DateTime.UtcNow;
        StatusChanged?.Invoke(true);
        Report("Web server started.");
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!IsProcessRunning) return;

        await RunShellAsync($"systemctl stop {_profile.NginxServiceName}", ct);
        await RunShellAsync($"systemctl stop {_profile.PhpFpmServiceName}", ct);

        StartTime = null;
        StatusChanged?.Invoke(false);
        Report("Web server stopped.");
    }

    public async Task RepairAsync(CancellationToken ct = default)
    {
        Report("Restarting web server...");
        await RunShellAsync($"systemctl restart {_profile.PhpFpmServiceName}", ct);
        await RunShellAsync($"systemctl restart {_profile.NginxServiceName}", ct);
        await WaitForServiceAsync(_profile.NginxServiceName, ct);

        StartTime = IsProcessRunning ? DateTime.UtcNow : null;
        StatusChanged?.Invoke(IsProcessRunning);
        Report("Web server restarted.");
    }

    public async Task UninstallAsync(CancellationToken ct = default)
    {
        if (IsProcessRunning)
        {
            Report("Stopping web server before uninstall...");
            await StopAsync(ct);
        }

        Report("Removing nginx and PHP-FPM packages...");
        string cmd = _profile.Manager switch
        {
            PackageManager.Apt => $"apt-get remove -y {_profile.NginxPackage} {_profile.PhpFpmPackage}",
            PackageManager.Dnf => $"dnf remove -y {_profile.NginxPackage} {_profile.PhpFpmPackage}",
            PackageManager.Yum => $"yum remove -y {_profile.NginxPackage} {_profile.PhpFpmPackage}",
            _                  => throw new NotSupportedException()
        };
        await RunShellAsync(cmd, ct);
        Report("Nginx and PHP-FPM removed.");
    }

    public async Task ReinstallAsync(CancellationToken ct = default)
    {
        await UninstallAsync(ct);
        await RunStartupCheckAsync(ct);
    }

    // -- Package installation --------------------------------------------------

    private async Task InstallPackagesAsync(CancellationToken ct)
    {
        string packages = $"{_profile.NginxPackage} {_profile.PhpFpmPackage}";

        if (_profile.Manager == PackageManager.Apt)
        {
            Report("Updating package index...");
            await RunShellAsync("apt-get update -y", ct);
            await RunShellAsync($"apt-get install -y {packages}", ct);
        }
        else if (_profile.Manager == PackageManager.Dnf)
        {
            // Enable EPEL for nginx on RHEL-based systems if not already present
            await RunShellAsync("dnf install -y epel-release 2>/dev/null || true", ct);
            await RunShellAsync($"dnf install -y {packages}", ct);
        }
        else
        {
            await RunShellAsync("yum install -y epel-release 2>/dev/null || true", ct);
            await RunShellAsync($"yum install -y {packages}", ct);
        }

        Report($"Nginx and PHP-FPM installed.");
    }

    // -- Status helpers --------------------------------------------------------

    private bool CheckInstalled() =>
        File.Exists("/usr/sbin/nginx")
        || File.Exists("/usr/bin/nginx")
        || File.Exists("/usr/local/sbin/nginx");

    private bool CheckServiceActive(string serviceName)
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
            proc.StartInfo.ArgumentList.Add(serviceName);

            proc.Start();
            proc.WaitForExit(5_000);
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    private int? ReadNginxPid()
    {
        try
        {
            foreach (var pidFile in new[] { "/run/nginx.pid", "/var/run/nginx.pid" })
            {
                if (File.Exists(pidFile)
                    && int.TryParse(File.ReadAllText(pidFile).Trim(), out int pid))
                    return pid;
            }
            return null;
        }
        catch { return null; }
    }

    private async Task WaitForServiceAsync(string serviceName, CancellationToken ct, int timeoutSec = 30)
    {
        for (int i = 0; i < timeoutSec; i++)
        {
            if (CheckServiceActive(serviceName)) return;
            await Task.Delay(1_000, ct);
        }
        throw new TimeoutException(
            $"Service '{serviceName}' did not become active within {timeoutSec} s.");
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
        string         NginxPackage,
        string         PhpFpmPackage,
        string         NginxServiceName,
        string         PhpFpmServiceName)
    {
        /// <summary>
        /// Auto-detects the package manager and returns the appropriate profile.
        /// Detection order: apt-get -> dnf -> yum.
        /// </summary>
        internal static DistroProfile Detect()
        {
            // Debian / Ubuntu -- nginx from official repos, php-fpm meta-package
            if (File.Exists("/usr/bin/apt-get") || File.Exists("/bin/apt-get"))
                return new(
                    Manager:          PackageManager.Apt,
                    NginxPackage:     "nginx",
                    PhpFpmPackage:    "php-fpm",
                    NginxServiceName: "nginx",
                    PhpFpmServiceName: "php-fpm");

            // RHEL 8/9 / CentOS Stream / AlmaLinux / Rocky -- nginx from EPEL
            if (File.Exists("/usr/bin/dnf"))
                return new(
                    Manager:          PackageManager.Dnf,
                    NginxPackage:     "nginx",
                    PhpFpmPackage:    "php-fpm",
                    NginxServiceName: "nginx",
                    PhpFpmServiceName: "php-fpm");

            // Older RHEL / CentOS 7 -- nginx from EPEL
            if (File.Exists("/usr/bin/yum"))
                return new(
                    Manager:          PackageManager.Yum,
                    NginxPackage:     "nginx",
                    PhpFpmPackage:    "php-fpm",
                    NginxServiceName: "nginx",
                    PhpFpmServiceName: "php-fpm");

            throw new PlatformNotSupportedException(
                "No supported Linux package manager found (apt-get, dnf, yum).");
        }
    }
}

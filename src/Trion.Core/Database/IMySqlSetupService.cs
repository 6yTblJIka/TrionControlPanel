namespace Trion.Core.Database;

/// <summary>Live snapshot of a MySQL package download in progress.</summary>
public record DownloadProgress(
    long      DownloadedBytes,
    long      TotalBytes,          // -1 when Content-Length header is absent
    double    SpeedBytesPerSec,
    TimeSpan? Eta,                 // null when total is unknown or speed is zero
    int       Percent);            // 0-100; 0 when total is unknown

public interface IMySqlSetupService
{
    // ── Installation state ────────────────────────────────────────────────────

    /// <summary>True when the MySQL binaries are present on this machine.</summary>
    bool IsInstalled { get; }

    // ── Setup operation state ─────────────────────────────────────────────────

    /// <summary>True while RunStartupCheckAsync is executing (download / extract / init).</summary>
    bool IsRunning { get; }

    /// <summary>The most recent progress message (empty string before first run).</summary>
    string LastMessage { get; }

    /// <summary>
    /// The most recent download snapshot while a download is active; null otherwise.
    /// Always null on Linux (package manager handles downloads transparently).
    /// </summary>
    DownloadProgress? CurrentDownload { get; }

    // ── Process state ─────────────────────────────────────────────────────────

    /// <summary>True when the mysqld process / systemd service is alive.</summary>
    bool IsProcessRunning { get; }

    /// <summary>The OS process ID of the running mysqld, or null if not running.</summary>
    int? ProcessId { get; }

    /// <summary>Wall-clock time when the service was last started, or null.</summary>
    DateTime? StartTime { get; }

    /// <summary>How long MySQL has been running. Zero when not running.</summary>
    TimeSpan Uptime { get; }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on every progress step and once more when the setup operation finishes.
    /// </summary>
    event Action<string>? ProgressChanged;

    /// <summary>
    /// Raised during active downloads with live speed / ETA / size data.
    /// Raised with <c>null</c> when the download completes or is aborted.
    /// Never raised on Linux (package manager handles downloads transparently).
    /// </summary>
    event Action<DownloadProgress?>? DownloadProgressChanged;

    /// <summary>Raised when mysqld starts (<c>true</c>) or stops / crashes (<c>false</c>).</summary>
    event Action<bool>? StatusChanged;

    // ── Setup ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called once at startup.
    /// Installs MySQL if absent, starts it if not running, and runs first-time
    /// database setup (phoenix user + all expansion schemas) if not yet done.
    /// </summary>
    Task RunStartupCheckAsync(CancellationToken ct = default);

    /// <summary>
    /// Writes (or refreshes) MySQL configuration tuned to the current hardware.
    /// No-op on Linux (system MySQL uses /etc/mysql/conf.d/).
    /// </summary>
    void GenerateMyIni();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>Starts MySQL. No-op if already running.</summary>
    Task StartAsync(bool? hideConsole = null, CancellationToken ct = default);

    /// <summary>Gracefully stops MySQL. No-op if not running.</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>Regenerates config and restarts MySQL.</summary>
    Task RepairAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops MySQL and removes the installation.
    /// On Linux, removes the package but preserves /var/lib/mysql data.
    /// On Windows, deletes the entire portable database/ folder.
    /// </summary>
    Task UninstallAsync(CancellationToken ct = default);

    /// <summary>Runs UninstallAsync then a full RunStartupCheckAsync.</summary>
    Task ReinstallAsync(CancellationToken ct = default);
}

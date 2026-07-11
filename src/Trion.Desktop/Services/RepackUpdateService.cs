using System.IO;
using Microsoft.Extensions.Hosting;
using Trion.Core.Abstractions.Services;
using Trion.Desktop.Models;

namespace Trion.Desktop.Services;

/// <summary>
/// Background service that periodically checks for Trion, repack (SSP), and database updates.
/// When auto-update flags are enabled in settings, updates are applied automatically;
/// otherwise a notification is posted. Running servers are stopped before any repack update.
/// </summary>
public sealed class RepackUpdateService : BackgroundService
{
    private static readonly TimeSpan InitialDelay  = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);

    private readonly ISettingsService          _settings;
    private readonly IEmulatorInstallerService _installer;
    private readonly INotificationService      _notifications;
    private readonly IAutoUpdater              _autoUpdater;

    public RepackUpdateService(ISettingsService settings,
                               IEmulatorInstallerService installer,
                               INotificationService notifications,
                               IAutoUpdater autoUpdater)
    {
        _settings      = settings;
        _installer     = installer;
        _notifications = notifications;
        _autoUpdater   = autoUpdater;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(InitialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckTrionAsync(stoppingToken);
            await CheckRepacksAsync(stoppingToken);

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    // -- Trion self-update -------------------------------------------------------

    private async Task CheckTrionAsync(CancellationToken ct)
    {
        UpdateCheckResult result;
        try { result = await _autoUpdater.CheckForUpdateAsync(ct); }
        catch { return; }

        if (!result.UpdateAvailable) return;

        var cfg = _settings.Current;
        if (cfg.AutoUpdateTrion)
        {
            _notifications.Add($"Trion update v{result.NewVersion} available — applying automatically...");
            try { await _autoUpdater.ApplyUpdateAsync(result, ct); }
            catch { }
        }
        else
        {
            _notifications.Add(
                $"Trion update available: v{result.NewVersion}. " +
                "Enable Auto-update Trion in Settings to apply automatically.");
        }
    }

    // -- Repack + database update ------------------------------------------------

    private async Task CheckRepacksAsync(CancellationToken ct)
    {
        var cfg = _settings.Current;

        var installedSlugs = cfg.RepackInstalled
            .Where(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (installedSlugs.Count == 0) return;

        List<RepackCatalogEntry> catalog;
        try { catalog = await _installer.FetchCatalogAsync(ct); }
        catch { return; }
        if (catalog.Count == 0) return;

        // DB-backed versions are more reliable than the live catalog field.
        Dictionary<string, string> dbVersions;
        try { dbVersions = await _installer.FetchRepackLatestVersionsAsync(ct); }
        catch { dbVersions = []; }

        foreach (var entry in catalog)
        {
            if (!installedSlugs.Contains(entry.Slug)) continue;

            var latestVersion = dbVersions.TryGetValue(entry.Slug, out var dbV) && dbV is not ("" or "--")
                ? dbV : entry.Version;
            if (latestVersion is "" or "--") continue;

            cfg.RepackVersion.TryGetValue(entry.Slug, out var installedVersion);
            if (string.IsNullOrEmpty(installedVersion) || installedVersion == "--") continue;

            if (string.Equals(installedVersion, latestVersion, StringComparison.OrdinalIgnoreCase))
                continue;

            var installPath = cfg.RepackInstallPaths.TryGetValue(entry.Slug, out var p) && !string.IsNullOrWhiteSpace(p)
                ? p
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "servers", entry.Slug);

            if (cfg.AutoUpdateCore)
                await ApplyRepackUpdateAsync(entry, installPath, installedVersion, latestVersion, cfg, ct);
            else
                _notifications.Add($"Repack update available for {entry.Slug}: {installedVersion} → {latestVersion}");
        }
    }

    private async Task ApplyRepackUpdateAsync(RepackCatalogEntry entry, string installPath,
        string installedVersion, string latestVersion, AppSettings cfg, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(entry.DownloadUrl))
        {
            _notifications.Add($"Repack update available for {entry.Slug}: {installedVersion} → {latestVersion} (no download URL yet).");
            return;
        }

        _notifications.Add($"Auto-updating {entry.Slug} ({installedVersion} → {latestVersion}): stopping servers...");

        try { await _installer.StopRepackProcessesAsync(installPath, ct); }
        catch { }

        try
        {
            _notifications.Add($"[{entry.Slug}] Downloading and installing update...");
            await _installer.InstallAsync(entry.Slug, installPath, null, ct, entry.DownloadUrl);
        }
        catch (Exception ex)
        {
            _notifications.Add($"[{entry.Slug}] Auto-update failed: {ex.Message}");
            return;
        }

        if (cfg.AutoUpdateDatabase)
        {
            try
            {
                var (ok, msg) = await _installer.CreateDatabasesAsync(installPath, ct);
                if (!ok) _notifications.Add($"[{entry.Slug}] Database update warning: {msg}");
            }
            catch { }
        }

        var newVersion = _installer.GetInstalledRepackVersion(installPath);
        if (newVersion != "--")
        {
            cfg.RepackVersion[entry.Slug] = newVersion;
            _settings.Save();
        }

        _notifications.Add($"[{entry.Slug}] Successfully updated to {latestVersion}.");
    }
}

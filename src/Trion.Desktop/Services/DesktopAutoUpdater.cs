using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Windows;
using Trion.Core.Abstractions.Services;
using Trion.Desktop.Models;

namespace Trion.Desktop.Services;

public sealed class DesktopAutoUpdater : IAutoUpdater
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly IApiKeyService        _apiKeys;
    private readonly INotificationService  _notifications;

    public DesktopAutoUpdater(IApiKeyService apiKeys, INotificationService notifications)
    {
        _apiKeys       = apiKeys;
        _notifications = notifications;
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, AppLinks.DesktopUpdateUrl);
            AddApiKeyHeader(req);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return NoUpdate;

            var info = await resp.Content.ReadFromJsonAsync<TrionUpdateDto>(ct);
            if (info is null || string.IsNullOrEmpty(info.Version))
                return NoUpdate;

            var current   = GetCurrentVersion();
            var available = ParseVersion(info.Version);
            bool hasUpdate = available is not null && current is not null && available > current;

            // Report the normalized 4-part form (not the server's raw string) so the
            // updater's --from/--to args and UI are always the same shape as `current`,
            // regardless of whether the release was entered as 3-part or 4-part.
            string displayVersion = available?.ToString(4) ?? info.Version;

            return new UpdateCheckResult(hasUpdate, displayVersion, info.Notes, info.DownloadUrl, null);
        }
        catch
        {
            return NoUpdate;
        }
    }

    public async Task ApplyUpdateAsync(UpdateCheckResult update, CancellationToken ct = default)
    {
        if (!update.UpdateAvailable || string.IsNullOrEmpty(update.DownloadUrl))
            return;

        string tempDir = Path.Combine(Path.GetTempPath(), "TrionUpdate");
        Directory.CreateDirectory(tempDir);
        string downloadPath = Path.Combine(tempDir, "trion_update.exe");

        using (var resp = await _http.GetAsync(update.DownloadUrl,
                   HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            using var fs = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await resp.Content.CopyToAsync(fs, ct);
        }

        string exePath    = Environment.ProcessPath
                         ?? Process.GetCurrentProcess().MainModule!.FileName;
        string exeDir     = Path.GetDirectoryName(exePath)!;
        string updaterExe = Path.Combine(exeDir, "Trion.Updater.exe");
        string current    = GetCurrentVersion()?.ToString(4) ?? "0.0.0.0";

        var psi = new ProcessStartInfo(updaterExe)
        {
            UseShellExecute = true,
            Arguments = $"--pid {Environment.ProcessId} " +
                        $"--source \"{downloadPath}\" " +
                        $"--target \"{exePath}\" " +
                        $"--from {current} " +
                        $"--to {update.NewVersion ?? "?"}",
        };

        Process.Start(psi);
        Application.Current.Dispatcher.Invoke(Application.Current.Shutdown);
    }

    private void AddApiKeyHeader(HttpRequestMessage req)
    {
        var key = _apiKeys.ActiveKey;
        if (!string.IsNullOrEmpty(key))
            req.Headers.TryAddWithoutValidation("X-API-Key", key);
    }

    private static Version? GetCurrentVersion()
        => Assembly.GetExecutingAssembly().GetName().Version;

    /// <summary>
    /// Parses a version string into a full 4-part <see cref="Version"/>. Older releases
    /// on the server may still be stored as 3-part (Major.Minor.Build) -- Version.Parse
    /// leaves a missing Revision as -1, which would make e.g. "0.5.0" compare as LESS
    /// THAN a locally-installed 4-part "0.5.0.16" and silently suppress the update. Pad
    /// any missing component with 0 so comparisons are consistent regardless of the
    /// source string's part-count.
    /// </summary>
    private static Version? ParseVersion(string? v)
    {
        if (!Version.TryParse(v, out var ver)) return null;
        return new Version(
            Math.Max(ver.Major, 0),
            Math.Max(ver.Minor, 0),
            Math.Max(ver.Build, 0),
            Math.Max(ver.Revision, 0));
    }

    private static readonly UpdateCheckResult NoUpdate =
        new(false, null, null, null, null);

    private sealed class TrionUpdateDto
    {
        [JsonPropertyName("version")]      public string? Version     { get; set; }
        [JsonPropertyName("download_url")] public string? DownloadUrl { get; set; }
        [JsonPropertyName("notes")]        public string? Notes       { get; set; }
    }
}

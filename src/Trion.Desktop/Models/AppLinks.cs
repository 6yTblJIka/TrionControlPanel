using Trion.Desktop.Infrastructure.Constants;

namespace Trion.Desktop.Models;

/// <summary>
/// Runtime URL registry — all API URLs are derived from <see cref="ExternalLinks.ApiDefaultHost"/>.
///
/// URL paths and external addresses live in:
///   <see cref="ApiEndpoints"/>   — Trion / Phoenix API routes
///   <see cref="ExternalLinks"/>  — Static websites, emulator repos, DDNS sign-up pages
///
/// API keys are sent via X-API-Key header — never in URLs.
/// </summary>
public static class AppLinks
{
    // ── API base — fixed to ExternalLinks.ApiDefaultHost ─────────────────────

    internal static readonly string ApiBaseUrl = ExternalLinks.ApiDefaultHost;

    // ── Derived API URLs (resolved against ApiBaseUrl) ────────────────────────

    public static string PingUrl            => ApiEndpoints.PingUrl(ApiBaseUrl);
    public static string GetExternalIpv4Url => ApiEndpoints.GetExternalIpv4Url(ApiBaseUrl);
    public static string UpdatesUrl         => ApiEndpoints.UpdatesUrl(ApiBaseUrl);
    public static string DownloadsUrl       => ApiEndpoints.DownloadsUrl(ApiBaseUrl);
    public static string NewsUrl            => ApiEndpoints.NewsUrl(ApiBaseUrl);
    public static string SupportersUrl      => ApiEndpoints.SupportersUrl(ApiBaseUrl);
    public static string VersionsUrl        => ApiEndpoints.VersionsUrl(ApiBaseUrl);
    public static string UpdateUrl          => ApiEndpoints.UpdateUrl(ApiBaseUrl);
    public static string TelemetryUrl       => ApiEndpoints.TelemetryUrl(ApiBaseUrl);
    public static string RepackMyBuildsUrl  => ApiEndpoints.RepackMyBuildsUrl(ApiBaseUrl);
    public static string RepackRequestUrl   => ApiEndpoints.RepackRequestUrl(ApiBaseUrl);

    // ── API request builders ──────────────────────────────────────────────────

    public static string GetFileVersionUrl =>
        ApiEndpoints.GetFileVersionUrl(ApiBaseUrl);

    public static string PackagesUrl =>
        ApiEndpoints.PackagesUrl(ApiBaseUrl);

    public static string AccountLoginUrl =>
        ApiEndpoints.AccountLoginUrl(ApiBaseUrl);

    public static string AccountProfileUrl =>
        ApiEndpoints.AccountProfileUrl(ApiBaseUrl);

    public static string GuestKeyUrl =>
        ApiEndpoints.GuestKeyUrl(ApiBaseUrl);

    public static string ValidateKeyUrl =>
        ApiEndpoints.ValidateKeyUrl(ApiBaseUrl);

    public static string InstallUrl(string emulator) =>
        ApiEndpoints.InstallUrl(ApiBaseUrl, emulator);

    public static string RepairUrl(string emulator) =>
        ApiEndpoints.RepairUrl(ApiBaseUrl, emulator);

    public static string ManifestUrl(string emulator) =>
        ApiEndpoints.ManifestUrl(ApiBaseUrl, emulator);

    public static string DownloadFileUrl(string emulator) =>
        ApiEndpoints.DownloadFileUrl(ApiBaseUrl, emulator);

    /// <summary>POST /Trion/DownloadFile — used for repair (individual file download).</summary>
    public static string DownloadSingleFileUrl =>
        ApiEndpoints.DownloadSingleFileUrl(ApiBaseUrl);

    public static string DownloadSpeedTestUrl(int sizeInMb = 100) =>
        ApiEndpoints.DownloadSpeedTestUrl(ApiBaseUrl, sizeInMb);

    public static string RepairSppUrl =>
        ApiEndpoints.RepairSppUrl(ApiBaseUrl);

    public static string RepackStatusUrl(string id) =>
        ApiEndpoints.RepackStatusUrl(ApiBaseUrl, id);

    /// <summary>MySQL package download URL. Falls back to <see cref="ExternalLinks.MySqlCdnDownload"/> if this fails.</summary>
    public static string MySqlApiDownloadUrl =>
        ApiEndpoints.MySqlDownloadUrl(ApiBaseUrl);

    /// <summary>POST /Trion/DownloadFile — web server package (supporter required).</summary>
    public static string WebServerApiDownloadUrl =>
        ApiEndpoints.WebServerDownloadUrl(ApiBaseUrl);

    // ── Static external URLs (convenience aliases to ExternalLinks) ───────────

    public const string DonationUrl = ExternalLinks.Donation;
    public const string DiscordUrl  = ExternalLinks.Discord;
    public const string SupportUrl  = ExternalLinks.Support;
    public const string PublicIpUrl = ExternalLinks.PublicIpFallback;
}

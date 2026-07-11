namespace Trion.Core.Constants;

/// <summary>
/// All Trion / Flying-Phoenix API route paths and URL builders.
/// Paths are relative to <see cref="ExternalLinks.ApiDefaultHost"/>.
/// Edit here when backend routes change -- nothing else needs updating.
///
/// API keys are sent via the X-API-Key request header, NOT in query strings.
/// </summary>
public static class ApiEndpoints
{
    // -- Route paths (relative) ------------------------------------------------

    // Account
    public const string AccountLogin      = "/Trion/account/login";
    public const string AccountVerify2fa  = "/Trion/account/verify-2fa";
    public const string AccountProfile    = "/Trion/account/profile";
    public const string GuestKey       = "/Trion/guest-key";
    public const string ValidateKey    = "/Trion/validate-key";

    // Health / Network
    public const string Ping           = "/Trion/Ping";
    public const string GetExternalIp  = "/Trion/GetExternalIPv4";

    // Packages / Versions
    public const string Packages       = "/Trion/packages";
    public const string PackageCatalog = "/Trion/catalog";
    public const string GetFileVersion = "/Trion/GetFileVersion";
    public const string Versions       = "/Trion/versions";
    public const string Update         = "/Trion/update";
    public const string DesktopUpdate  = "/Trion/desktop/update";

    // Install / Repair / Manifest  -- append /{emulator} when building URLs
    public const string InstallBase    = "/Trion/install";
    public const string RepairBase     = "/Trion/repair";
    public const string ManifestBase   = "/Trion/manifest";

    // Download / Repair (legacy WPF contract)
    public const string DownloadFile   = "/Trion/DownloadFile";
    public const string DownloadSpeedTest = "/Trion/DownloadSpeedTest";
    public const string RepairSpp      = "/Trion/RepairSPP";

    // Content
    public const string News           = "/Trion/news";
    public const string Updates        = "/Trion/updates";
    public const string Downloads      = "/Trion/downloads";
    public const string Supporters     = "/Trion/supporters";
    public const string DonationGoal   = "/Trion/donation-goal";

    // Repack builder
    public const string RepackCatalog  = "/Trion/repack/catalog";
    public const string RepackRequest  = "/Trion/repack/request";
    public const string RepackMyBuilds = "/Trion/repack/my-builds";
    public static string RepackStatus(string id) => $"/Trion/repack/status/{Uri.EscapeDataString(id)}";

    // Changelogs + versions
    public const string Changelogs          = "/Trion/changelogs";
    public const string RepackVersions      = "/Trion/repack/versions";
    public const string RepackChangelogBase = "/Trion/repack/changelog";
    public static string RepackChangelogUrl(string baseUrl, string slug) =>
        $"{baseUrl}{RepackChangelogBase}/{Uri.EscapeDataString(slug)}";
    public static string ChangelogsUrl(string baseUrl)      => $"{baseUrl}{Changelogs}";
    public static string RepackVersionsUrl(string baseUrl)  => $"{baseUrl}{RepackVersions}";

    // Telemetry
    public const string Telemetry      = "/Trion/telemetry";
    public const string DevicePing     = "/api/ping";
    public const string BugReport      = "/api/bugreports";

    // -- URL builders (accept the resolved base URL) ---------------------------

    public static string PingUrl(string baseUrl)              => $"{baseUrl}{Ping}";
    public static string GetExternalIpv4Url(string baseUrl)   => $"{baseUrl}{GetExternalIp}";
    public static string AccountLoginUrl(string baseUrl)       => $"{baseUrl}{AccountLogin}";
    public static string AccountVerify2faUrl(string baseUrl)  => $"{baseUrl}{AccountVerify2fa}";
    public static string AccountProfileUrl(string baseUrl)    => $"{baseUrl}{AccountProfile}";
    public static string GuestKeyUrl(string baseUrl)          => $"{baseUrl}{GuestKey}";
    public static string ValidateKeyUrl(string baseUrl)       => $"{baseUrl}{ValidateKey}";
    public static string PackagesUrl(string baseUrl)          => $"{baseUrl}{Packages}";
    public static string PackageCatalogUrl(string baseUrl)    => $"{baseUrl}{PackageCatalog}";
    public static string GetFileVersionUrl(string baseUrl)    => $"{baseUrl}{GetFileVersion}";
    public static string VersionsUrl(string baseUrl)          => $"{baseUrl}{Versions}";
    public static string UpdateUrl(string baseUrl)            => $"{baseUrl}{Update}";
    public static string DesktopUpdateUrl(string baseUrl)    => $"{baseUrl}{DesktopUpdate}";
    public static string InstallUrl(string baseUrl, string emulator) =>
        $"{baseUrl}{InstallBase}/{Uri.EscapeDataString(emulator)}";
    public static string RepairUrl(string baseUrl, string emulator) =>
        $"{baseUrl}{RepairBase}/{Uri.EscapeDataString(emulator)}";
    public static string ManifestUrl(string baseUrl, string emulator) =>
        $"{baseUrl}{ManifestBase}/{Uri.EscapeDataString(emulator)}";
    public static string DownloadFileUrl(string baseUrl, string emulator) =>
        $"{baseUrl}{DownloadFile}?emulator={Uri.EscapeDataString(emulator)}";
    public static string DownloadSingleFileUrl(string baseUrl) => $"{baseUrl}{DownloadFile}";
    public static string DownloadSpeedTestUrl(string baseUrl, int sizeInMb = 100) =>
        $"{baseUrl}{DownloadSpeedTest}?sizeInMB={sizeInMb}";
    public static string RepairSppUrl(string baseUrl)          => $"{baseUrl}{RepairSpp}";
    public static string NewsUrl(string baseUrl)               => $"{baseUrl}{News}";
    public static string UpdatesUrl(string baseUrl)            => $"{baseUrl}{Updates}";
    public static string DownloadsUrl(string baseUrl)          => $"{baseUrl}{Downloads}";
    public static string SupportersUrl(string baseUrl)         => $"{baseUrl}{Supporters}";
    public static string DonationGoalUrl(string baseUrl)       => $"{baseUrl}{DonationGoal}";
    public static string RepackCatalogUrl(string baseUrl)       => $"{baseUrl}{RepackCatalog}";
    public static string RepackRequestUrl(string baseUrl)      => $"{baseUrl}{RepackRequest}";
    public static string RepackMyBuildsUrl(string baseUrl)     => $"{baseUrl}{RepackMyBuilds}";
    public static string RepackStatusUrl(string baseUrl, string id) =>
        $"{baseUrl}{RepackStatus(id)}";
    public static string TelemetryUrl(string baseUrl)          => $"{baseUrl}{Telemetry}";
    public static string BugReportUrl(string baseUrl)          => $"{baseUrl}{BugReport}";

    /// <summary>MySQL package download URL.</summary>
    public static string MySqlDownloadUrl(string baseUrl) =>
        $"{baseUrl}{DownloadFile}?emulator=mysql";

    /// <summary>Web server package download URL (POST, supporter required).</summary>
    public static string WebServerDownloadUrl(string baseUrl) =>
        $"{baseUrl}{DownloadFile}";
}

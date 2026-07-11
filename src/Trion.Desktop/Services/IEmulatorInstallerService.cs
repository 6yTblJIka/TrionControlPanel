using Trion.Desktop.Models;

namespace Trion.Desktop.Services;

public interface IEmulatorInstallerService
{
    /// <summary>
    /// Fires whenever an API response includes X-Points-Balance / X-Points-Tier headers.
    /// Parameters: (balance, tier) -- balance is -1 for unlimited (Legend).
    /// </summary>
    event Action<int, string>? PointsUpdated;


    /// <summary>
    /// Fetch the active repack catalog from the Trion API (which proxies RepackService).
    /// Returns an empty list when the service is unreachable.
    /// </summary>
    Task<List<RepackCatalogEntry>> FetchCatalogAsync(CancellationToken ct);

    /// <summary>
    /// Fetch the package catalog from <c>GET /Trion/catalog</c>.
    /// Returns emulator metadata (engine type, auto-DB-import flag, exe paths, etc.)
    /// so the Desktop knows how to configure and launch each installed package.
    /// Returns an empty response on network failure.
    /// </summary>
    Task<PackageCatalogResponse> FetchPackageCatalogAsync(CancellationToken ct);

    /// <summary>
    /// Download the repack ZIP and extract it to <paramref name="installPath"/>.
    /// <paramref name="downloadUrl"/> is the direct URL from the catalog (RepackService build);
    /// if null the installer falls back to the legacy <c>/Trion/DownloadFile?emulator=</c> endpoint.
    /// </summary>
    Task InstallAsync(string emulatorName, string installPath,
        IProgress<InstallProgress>? progress, CancellationToken ct,
        string? downloadUrl = null);

    /// <summary>Re-download and re-extract, overwriting existing files (acts as full repair).</summary>
    Task RepairAsync(string emulatorName, string installPath,
        IProgress<InstallProgress>? progress, CancellationToken ct);

    /// <summary>Deletes the installation directory.</summary>
    Task UninstallAsync(string installPath,
        IProgress<InstallProgress>? progress, CancellationToken ct);

    /// <summary>Fetch the latest versions for all expansions from the API.</summary>
    Task<EmulatorVersions> FetchLatestVersionsAsync(CancellationToken ct);

    /// <summary>
    /// Fetch which packages are available for the current API key tier.
    /// Returns an empty response (all unavailable) on network failure.
    /// </summary>
    Task<AvailablePackagesResponse> FetchAvailablePackagesAsync(CancellationToken ct);

    /// <summary>
    /// Fetch the latest build version per repack slug from Phoenix DB.
    /// Returns a slug → version dictionary (e.g. "azerothcore-classic" → "2026.06.05").
    /// Returns an empty dictionary on network failure.
    /// </summary>
    Task<Dictionary<string, string>> FetchRepackLatestVersionsAsync(CancellationToken ct);

    /// <summary>Read the local version string from a world-server exe (returns "--" on failure).</summary>
    string GetLocalVersion(string exePath);

    /// <summary>
    /// Read the installed version from repack-manifest.json in the install directory.
    /// Returns the builtAt date formatted as "yyyy.MM.dd", or "--" when not found.
    /// </summary>
    string GetInstalledRepackVersion(string installPath);

    /// <summary>
    /// Runs create-databases.sql (if present in <paramref name="installPath"/>) against the local
    /// MySQL server using the admin credentials configured in app settings (MySqlAdminUser/Password).
    /// Returns (true, summary) on success, (false, error) if MySQL is unreachable or the file is missing.
    /// </summary>
    Task<(bool Success, string Message)> CreateDatabasesAsync(string installPath, CancellationToken ct);

    /// <summary>
    /// Drops every database listed in the manifest then recreates it empty (wipes all data).
    /// If a database does not yet exist it is simply created. Recreates the game user and grants.
    /// </summary>
    Task<(bool Success, string Message)> ClearDatabasesAsync(string installPath, CancellationToken ct);

    /// <summary>
    /// Kills worldserver/authserver processes whose executable lives inside
    /// <paramref name="installPath"/>. Reads process names from repack-manifest.json;
    /// does nothing when the manifest is absent.
    /// </summary>
    Task StopRepackProcessesAsync(string installPath, CancellationToken ct);
}

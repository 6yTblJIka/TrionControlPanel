using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MySqlConnector;
using Trion.Core.Database;
using Trion.Desktop.Infrastructure.Constants;
using Trion.Desktop.Models;

namespace Trion.Desktop.Services;

public sealed class EmulatorInstallerService : IEmulatorInstallerService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromHours(2) };

    private readonly IApiKeyService  _apiKeys;
    private readonly ISettingsService _settings;

    public event Action<int, string>? PointsUpdated;

    public EmulatorInstallerService(IApiKeyService apiKeys, ISettingsService settings)
    {
        _apiKeys  = apiKeys;
        _settings = settings;
    }

    private void NotifyPoints(HttpResponseMessage resp)
    {
        if (resp.Headers.TryGetValues("X-Points-Balance", out var balVals) &&
            int.TryParse(balVals.FirstOrDefault(), out int balance))
        {
            resp.Headers.TryGetValues("X-Points-Tier", out var tierVals);
            PointsUpdated?.Invoke(balance, tierVals?.FirstOrDefault() ?? "");
        }
    }

    // -- Public API ------------------------------------------------------------

    public Task InstallAsync(string emulatorName, string installPath,
        IProgress<InstallProgress>? progress, CancellationToken ct,
        string? downloadUrl = null)
        => RunInstallCoreAsync(emulatorName, installPath, progress, ct, downloadUrl);

    public Task RepairAsync(string emulatorName, string installPath,
        IProgress<InstallProgress>? progress, CancellationToken ct)
        => RunRepairCoreAsync(emulatorName, installPath, progress, ct);

    public async Task UninstallAsync(string installPath,
        IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        // Read the manifest before any files are deleted so we have exe + DB names.
        var manifest = RepackManifest.Load(installPath);

        progress?.Report(new InstallProgress("Removing", 0, 0, "Stopping server processes..."));
        if (manifest is not null)
            await StopRepackProcessesAsync(installPath, manifest);

        progress?.Report(new InstallProgress("Removing", 33, 0, "Dropping databases..."));
        await DropDatabasesAsync(manifest, ct);

        progress?.Report(new InstallProgress("Removing", 66, 0, "Deleting files..."));
        if (Directory.Exists(installPath))
            await Task.Run(() => Directory.Delete(installPath, recursive: true), ct);

        progress?.Report(new InstallProgress("Done", 100, 0, "Uninstalled."));
    }

    public Task StopRepackProcessesAsync(string installPath, CancellationToken ct)
    {
        var manifest = RepackManifest.Load(installPath);
        return manifest is null ? Task.CompletedTask : StopRepackProcessesAsync(installPath, manifest);
    }

    private static async Task StopRepackProcessesAsync(string installPath, RepackManifest manifest)
    {
        var normalized = Path.GetFullPath(installPath);

        // Collect the exe names declared in the manifest (without extension).
        var names = new[]
        {
            Path.GetFileNameWithoutExtension(manifest.ServerExecutable),
            Path.GetFileNameWithoutExtension(manifest.AuthExecutable),
        }.Where(n => !string.IsNullOrWhiteSpace(n))
         .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try
                {
                    // Only kill the process if its executable lives inside this install path.
                    // This prevents killing a worldserver from a different repack.
                    var exePath = proc.MainModule?.FileName ?? string.Empty;
                    if (!Path.GetFullPath(exePath).StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        proc.Dispose();
                        continue;
                    }

                    proc.Kill(entireProcessTree: true);
                    try { await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
                }
                catch { /* access denied or already exited */ }
                finally { proc.Dispose(); }
            }
        }
    }

    private async Task DropDatabasesAsync(RepackManifest? manifest, CancellationToken ct)
    {
        if (manifest is null) return;

        var databases = manifest.AllDatabases().ToList();
        if (databases.Count == 0) return;

        try
        {
            await using var conn = OpenAdminConnection();
            await conn.OpenAsync(ct);

            foreach (var db in databases)
            {
                await using var cmd = new MySqlCommand($"DROP DATABASE IF EXISTS `{db}`;", conn);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        catch { /* non-fatal — files are still deleted even if DB drop fails */ }
    }

    public async Task<EmulatorVersions> FetchLatestVersionsAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, AppLinks.GetFileVersionUrl);
            AddApiKeyHeader(req);
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            NotifyPoints(resp);
            return await resp.Content.ReadFromJsonAsync<EmulatorVersions>(ct) ?? new EmulatorVersions();
        }
        catch
        {
            return new EmulatorVersions();
        }
    }

    public async Task<List<RepackCatalogEntry>> FetchCatalogAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, AppLinks.RepackCatalogUrl);
            AddApiKeyHeader(req);
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            NotifyPoints(resp);
            return await resp.Content.ReadFromJsonAsync<List<RepackCatalogEntry>>(ct) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<PackageCatalogResponse> FetchPackageCatalogAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, AppLinks.PackageCatalogUrl);
            AddApiKeyHeader(req);
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            NotifyPoints(resp);
            return await resp.Content.ReadFromJsonAsync<PackageCatalogResponse>(ct)
                   ?? new PackageCatalogResponse();
        }
        catch
        {
            return new PackageCatalogResponse();
        }
    }

    public async Task<AvailablePackagesResponse> FetchAvailablePackagesAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, AppLinks.PackagesUrl);
            AddApiKeyHeader(req);
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            NotifyPoints(resp);
            return await resp.Content.ReadFromJsonAsync<AvailablePackagesResponse>(ct)
                   ?? new AvailablePackagesResponse();
        }
        catch
        {
            return new AvailablePackagesResponse();
        }
    }

    public async Task<Dictionary<string, string>> FetchRepackLatestVersionsAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, AppLinks.RepackVersionsUrl);
            AddApiKeyHeader(req);
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            var data   = await resp.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>(ct) ?? [];
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (slug, el) in data)
            {
                if (el.TryGetProperty("version", out var v))
                    result[slug] = v.GetString() ?? "--";
            }
            return result;
        }
        catch { return []; }
    }

    public string GetInstalledRepackVersion(string installPath)
    {
        var raw = RepackManifest.Load(installPath)?.BuiltAt ?? "";
        return DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToUniversalTime().ToString("yyyy.MM.dd")
            : "--";
    }

    public string GetLocalVersion(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return "--";
        try
        {
            var info    = FileVersionInfo.GetVersionInfo(exePath);
            var version = info.FileVersion ?? "";

            if (version.Contains("SPP", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(version, @"\b\d{4}[-/]\d{2}[-/]\d{2}\b");
                if (m.Success) return m.Value;
            }

            return string.IsNullOrEmpty(version) ? "--" : version;
        }
        catch
        {
            return "--";
        }
    }

    // -- Install: download full ZIP and extract --------------------------------

    private async Task RunInstallCoreAsync(string emulatorName, string installPath,
        IProgress<InstallProgress>? progress, CancellationToken ct, string? downloadUrl = null)
    {
        Directory.CreateDirectory(installPath);

        var url     = !string.IsNullOrWhiteSpace(downloadUrl) ? downloadUrl : AppLinks.DownloadFileUrl(emulatorName);
        var zipPath = Path.Combine(Path.GetTempPath(), $"trion_{emulatorName}_{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadZipAsync(url, zipPath, "Installing", progress, ct);
            VerifyZip(zipPath, progress);
            await ExtractZipAsync(zipPath, installPath, progress, ct);
            progress?.Report(new InstallProgress("Cleaning up", 99, 0, "Removing temporary files..."));
        }
        finally
        {
            TryDelete(zipPath);
        }

        // Auto-create databases when the package ships a repack manifest.
        // Retry up to 3 times with 3-second gaps so a slow MySQL startup isn't fatal.
        var manifest  = RepackManifest.Load(installPath);
        var dbSuccess = true;
        var dbMessage = "";

        if (manifest is not null)
        {
            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                progress?.Report(new InstallProgress("Setting up databases", 99, 0,
                    attempt == 1 ? "Creating MySQL databases..."
                                 : $"Retrying database setup (attempt {attempt}/{maxAttempts})..."));

                (dbSuccess, dbMessage) = await CreateDatabasesAsync(installPath, ct);
                if (dbSuccess) break;

                if (attempt < maxAttempts)
                    await Task.Delay(3000, ct);
            }

            progress?.Report(new InstallProgress("Setting up databases", 99, 0,
                dbSuccess ? dbMessage : $"Database setup failed: {dbMessage}"));
        }

        // Create log directories from every .conf / .conf.dist in the install tree.
        CreateServerLogDirs(installPath, manifest, progress);

        // Surface DB failure in the final status so it isn't overwritten by "Installation complete."
        var finalStatus = dbSuccess || manifest is null
            ? "Installation complete."
            : $"Files installed. Databases not created — click 'Clear Databases' on the card to retry. ({dbMessage})";

        progress?.Report(new InstallProgress("Done", 100, 0, finalStatus));
    }

    // -- Repair: diff-based -- only download what's missing or corrupt ----------

    private async Task RunRepairCoreAsync(string emulatorName, string installPath,
        IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        // 1. Fetch the server file manifest
        progress?.Report(new InstallProgress("Fetching manifest", 0, 0, "Contacting server..."));
        var manifest = await FetchRepairManifestAsync(emulatorName, ct);

        if (manifest.Count == 0)
            throw new InvalidOperationException("Server returned an empty file manifest.");

        progress?.Report(new InstallProgress("Fetching manifest", 100, 0, $"{manifest.Count} files on server."));

        // 2. Scan local files and compute MD5 hashes concurrently
        var localHashes = await ScanLocalFilesAsync(installPath, emulatorName, progress, ct);

        // 3. Compare -- find files that are missing or have a wrong hash
        progress?.Report(new InstallProgress("Comparing", 100, 0, "Identifying files to repair..."));
        var toRepair = FindFilesToRepair(manifest, localHashes, emulatorName);

        if (toRepair.Count == 0)
        {
            progress?.Report(new InstallProgress("Done", 100, 0, "All files verified. Nothing to repair."));
            return;
        }

        progress?.Report(new InstallProgress("Comparing", 100, 0, $"{toRepair.Count} file(s) need repair."));

        // 4. Download only the missing / corrupt files
        await DownloadRepairFilesAsync(emulatorName, installPath, toRepair, progress, ct);

        progress?.Report(new InstallProgress("Done", 100, 0, $"Repair complete -- {toRepair.Count} file(s) restored."));
    }

    // -- Manifest fetch --------------------------------------------------------

    private async Task<List<FileManifestEntry>> FetchRepairManifestAsync(
        string emulatorName, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, AppLinks.RepairSppUrl);
        AddApiKeyHeader(req);
        req.Content = JsonContent.Create(new { emulator = emulatorName });

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        NotifyPoints(resp);

        var manifest = await resp.Content.ReadFromJsonAsync<FileManifest>(ct);
        return manifest?.Files ?? [];
    }

    // -- Local scan (concurrent MD5 hashing) -----------------------------------

    private static async Task<Dictionary<string, string>> ScanLocalFilesAsync(
        string installPath, string emulatorName,
        IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        // Key: normalised server-style path  e.g. "/wotlk/bin/worldserver.exe"
        // Value: MD5 hash (uppercase hex)
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(installPath))
            return result;

        var files      = Directory.EnumerateFiles(installPath, "*", SearchOption.AllDirectories).ToList();
        var prefix     = $"/{emulatorName.Trim('/')}";
        int done       = 0;
        var semaphore  = new SemaphoreSlim(Environment.ProcessorCount * 2);

        var tasks = files.Select(async file =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();

                var hash     = await ComputeSha256Async(file, ct);
                var relative = Path.GetRelativePath(installPath, file).Replace('\\', '/');
                var key      = $"{prefix}/{relative}";

                lock (result)
                    result[key] = hash;

                int scanned = Interlocked.Increment(ref done);
                if (scanned % 50 == 0 || scanned == files.Count)
                {
                    int pct = files.Count > 0 ? scanned * 100 / files.Count : 100;
                    progress?.Report(new InstallProgress("Scanning", pct, 0,
                        $"{scanned} / {files.Count} files scanned"));
                }
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);
        return result;
    }

    // -- Comparison ------------------------------------------------------------

    private static List<FileManifestEntry> FindFilesToRepair(
        List<FileManifestEntry> manifest,
        Dictionary<string, string> localHashes,
        string emulatorName)
    {
        // Build the same normalised key the scanner uses: "/wotlk/bin/worldserver.exe"
        return manifest
            .Where(entry =>
            {
                var key = $"{entry.Path.TrimEnd('/')}/{entry.Name}";
                return !localHashes.TryGetValue(key, out var localHash)
                       || !string.Equals(localHash, entry.Hash, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
    }

    // -- Download individual repair files --------------------------------------

    private async Task DownloadRepairFilesAsync(
        string emulatorName, string installPath,
        List<FileManifestEntry> files,
        IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var prefix    = $"/{emulatorName.Trim('/')}/";
        var url       = AppLinks.DownloadSingleFileUrl;  // POST body carries emulator + filePath
        int total     = files.Count;
        var sw        = new Stopwatch();

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            var entry    = files[i];
            var fullPath = $"{entry.Path.TrimEnd('/')}/{entry.Name}";

            // Derive the local destination path by stripping the emulator prefix
            var relative = fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? fullPath[prefix.Length..]
                : fullPath.TrimStart('/');

            var destPath = Path.GetFullPath(Path.Combine(installPath, relative.Replace('/', Path.DirectorySeparatorChar)));

            // Path traversal guard
            if (!destPath.StartsWith(Path.GetFullPath(installPath), StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            progress?.Report(new InstallProgress("Repairing", i * 100 / total, 0,
                $"{entry.Name}  ({i + 1} / {total})"));

            sw.Restart();
            await DownloadSingleFileAsync(url, emulatorName, fullPath, destPath, i, total, progress, ct);

            // Preserve file timestamps from manifest
            if (File.Exists(destPath) && entry.LastWriteTime != default)
            {
                File.SetLastWriteTimeUtc(destPath, entry.LastWriteTime);
                File.SetCreationTimeUtc(destPath, entry.LastWriteTime);
            }
        }
    }

    private async Task DownloadSingleFileAsync(
        string url, string emulatorName, string serverFilePath, string destPath,
        int fileIndex, int total,
        IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        AddApiKeyHeader(req);
        req.Content = JsonContent.Create(new { emulator = emulatorName, filePath = serverFilePath });

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        NotifyPoints(resp);

        long   contentLen = resp.Content.Headers.ContentLength ?? -1;
        long   total_     = 0;
        long   prevRead   = 0;
        var    sw         = Stopwatch.StartNew();

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destPath);

        var buf  = new byte[81_920];
        int read;

        while ((read = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, read), ct);
            total_ += read;

            if (sw.Elapsed.TotalMilliseconds >= 400)
            {
                double speed  = (total_ - prevRead) / sw.Elapsed.TotalSeconds / 1_048_576.0;
                prevRead      = total_;
                sw.Restart();

                int    pct    = contentLen > 0 ? (int)(total_ * 100L / contentLen) : 0;
                string status = contentLen > 0
                    ? $"{FormatBytes(total_)} / {FormatBytes(contentLen)}  ({fileIndex + 1} / {total})"
                    : $"{FormatBytes(total_)}  ({fileIndex + 1} / {total})";

                progress?.Report(new InstallProgress("Repairing", pct, speed, status));
            }
        }
    }

    // -- Install helpers -------------------------------------------------------

    private const int DownloadMaxAttempts  = 4;
    private const int DownloadRetryDelayMs = 3_000;

    /// <summary>
    /// Downloads a ZIP to <paramref name="destPath"/> with automatic retry and
    /// Range-based resume so a network blip mid-download doesn't restart from zero.
    /// </summary>
    private async Task DownloadZipAsync(string url, string destPath, string phase,
        IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        long contentLen = -1;

        for (int attempt = 1; attempt <= DownloadMaxAttempts; attempt++)
        {
            long startByte = 0;

            // If a partial file already exists from a previous attempt, resume from there.
            if (File.Exists(destPath))
                startByte = new FileInfo(destPath).Length;

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            AddApiKeyHeader(req);
            if (startByte > 0)
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(startByte, null);

            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (HttpRequestException ex) when (attempt < DownloadMaxAttempts)
            {
                progress?.Report(new InstallProgress(phase, 0, 0,
                    $"Network error (attempt {attempt}/{DownloadMaxAttempts}): {ex.Message}. Retrying..."));
                await Task.Delay(DownloadRetryDelayMs * attempt, ct);
                continue;
            }

            // 416 = server doesn't support Range or file changed  -  start fresh
            if (resp.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                TryDelete(destPath);
                startByte = 0;
                req.Dispose();
                resp.Dispose();
                continue;
            }

            resp.EnsureSuccessStatusCode();
            NotifyPoints(resp);

            // 206 Partial Content → resuming; 200 OK → full download (Range not supported)
            bool resuming = resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
            if (!resuming && startByte > 0)
            {
                // Server sent full content despite our Range header  -  discard partial file
                TryDelete(destPath);
                startByte = 0;
            }

            if (contentLen < 0)
                contentLen = (resp.Content.Headers.ContentLength ?? -1) + startByte;

            long total    = startByte;
            long prevRead = startByte;
            var  sw       = Stopwatch.StartNew();

            try
            {
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(destPath, resuming ? FileMode.Append : FileMode.Create,
                                                     FileAccess.Write, FileShare.None, 81_920, useAsync: true);
                var buf = new byte[81_920];
                int read;

                while ((read = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, read), ct);
                    total += read;

                    if (sw.Elapsed.TotalMilliseconds >= 400)
                    {
                        double speed  = (total - prevRead) / sw.Elapsed.TotalSeconds / 1_048_576.0;
                        prevRead      = total;
                        sw.Restart();

                        int    pct    = contentLen > 0 ? (int)(total * 100L / contentLen) : 0;
                        string status = contentLen > 0
                            ? $"{FormatBytes(total)} / {FormatBytes(contentLen)}"
                            : FormatBytes(total);

                        progress?.Report(new InstallProgress(phase, pct, speed, status));
                    }
                }

                progress?.Report(new InstallProgress(phase, 100, 0, "Download complete."));
                return; // success
            }
            catch (IOException ex) when (attempt < DownloadMaxAttempts)
            {
                progress?.Report(new InstallProgress(phase, 0, 0,
                    $"Download interrupted (attempt {attempt}/{DownloadMaxAttempts}): {ex.Message}. Resuming..."));
                await Task.Delay(DownloadRetryDelayMs * attempt, ct);
                // partial file kept  -  next iteration will resume
            }
            finally
            {
                resp.Dispose();
            }
        }
    }

    /// <summary>
    /// Opens the ZIP archive to verify it is not truncated or corrupt.
    /// Throws <see cref="InvalidDataException"/> on failure so the caller's finally
    /// block deletes the partial file before surfacing the error to the user.
    /// </summary>
    private static void VerifyZip(string zipPath, IProgress<InstallProgress>? progress)
    {
        progress?.Report(new InstallProgress("Verifying", 100, 0, "Checking downloaded file..."));
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            // Reading the entry count forces the central directory to be parsed.
            // A truncated ZIP will throw here before any extraction begins.
            _ = archive.Entries.Count;
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException(
                $"The downloaded file is corrupt or incomplete: {ex.Message} " +
                "Please try downloading again.", ex);
        }
    }

    private static async Task ExtractZipAsync(string zipPath, string destPath,
        IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        // Run extraction on a thread pool thread without passing ct to Task.Run so
        // the task always completes in RanToCompletion state.  We check the token
        // via IsCancellationRequested (no throw inside the worker) and then call
        // ThrowIfCancellationRequested() back on the async thread so the exception
        // propagates cleanly through the await chain instead of escaping from a
        // thread-pool dispatch loop.
        bool cancelled = await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(zipPath);

            int total = archive.Entries.Count(e => !string.IsNullOrEmpty(e.Name));
            int done  = 0;

            foreach (var entry in archive.Entries)
            {
                if (ct.IsCancellationRequested) return true;

                var targetPath = Path.GetFullPath(Path.Combine(destPath, entry.FullName));

                if (!targetPath.StartsWith(Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                entry.ExtractToFile(targetPath, overwrite: true);

                // Preserve archive timestamps
                File.SetLastWriteTime(targetPath, entry.LastWriteTime.DateTime);
                File.SetCreationTime(targetPath, entry.LastWriteTime.DateTime);

                done++;
                int pct = done * 100 / Math.Max(total, 1);
                progress?.Report(new InstallProgress("Extracting", pct, 0, entry.Name));
            }
            return false;
        });

        // Throw on the async thread — caught cleanly by the caller's catch block.
        ct.ThrowIfCancellationRequested();

        if (!cancelled)
            progress?.Report(new InstallProgress("Extracting", 100, 0, "Extraction complete."));
    }

    // -- MD5 hashing (for file integrity, not cryptography) -------------------

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);

        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // -- Database helpers --------------------------------------------------------

    /// <summary>
    /// Opens a MySQL connection using admin credentials from app settings.
    /// Falls back to root / portable-MySQL password when nothing is configured.
    /// </summary>
    private MySqlConnection OpenAdminConnection(string? hostOverride = null, string? portOverride = null)
    {
        var cfg       = _settings.Current;
        var adminHost = hostOverride
                        ?? (string.IsNullOrWhiteSpace(cfg.DBServerHost) ? "127.0.0.1" : cfg.DBServerHost);
        var adminPort = int.TryParse(portOverride ?? cfg.DBServerPort, out var p) ? p : 3306;
        var adminUser = string.IsNullOrWhiteSpace(cfg.MySqlAdminUser) ? "root" : cfg.MySqlAdminUser;
        var adminPass = string.IsNullOrWhiteSpace(cfg.MySqlAdminPassword)
            ? MySqlInitSql.RootPassword
            : cfg.MySqlAdminPassword;

        return new MySqlConnection(new MySqlConnectionStringBuilder
        {
            Server            = adminHost,
            Port              = (uint)adminPort,
            UserID            = adminUser,
            Password          = adminPass,
            ConnectionTimeout = 10,
        }.ConnectionString);
    }

    public async Task<(bool Success, string Message)> CreateDatabasesAsync(
        string installPath, CancellationToken ct)
    {
        var manifest = RepackManifest.Load(installPath);
        if (manifest is null)
            return (false, "repack-manifest.json not found in the install directory.");

        var dbUser    = manifest.DbUser;
        var dbPass    = manifest.DbPass;
        var databases = manifest.AllDatabases().ToList();

        // Validate manifest fields before executing any SQL
        if (string.IsNullOrWhiteSpace(dbUser))
            return (false, "repack-manifest.json: DbUser is empty. Cannot create databases.");

        if (databases.Count == 0)
            return (false, "repack-manifest.json: no database names defined. Cannot create databases.");

        var blankDb = databases.FirstOrDefault(string.IsNullOrWhiteSpace);
        if (blankDb is not null)
            return (false, "repack-manifest.json: one or more database names are blank.");

        try
        {
            await using var conn = OpenAdminConnection(manifest.DbHost, manifest.DbPort);
            await conn.OpenAsync(ct);

            // Create each required database
            foreach (var db in databases)
            {
                ct.ThrowIfCancellationRequested();
                await using var cmd = new MySqlCommand(
                    $"CREATE DATABASE IF NOT EXISTS `{db}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;",
                    conn);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Create the emulator DB user and grant access on each database.
            // We create two accounts -- one for 127.0.0.1 and one for localhost -- because
            // MySQL treats them as distinct accounts; emulators may connect via either.
            if (!string.IsNullOrWhiteSpace(dbUser))
            {
                foreach (var host in new[] { "127.0.0.1", "localhost" })
                {
                    ct.ThrowIfCancellationRequested();

                    await using var createUser = new MySqlCommand(
                        $"CREATE USER IF NOT EXISTS '{dbUser}'@'{host}' IDENTIFIED BY '{dbPass}';",
                        conn);
                    await createUser.ExecuteNonQueryAsync(ct);

                    foreach (var db in databases)
                    {
                        ct.ThrowIfCancellationRequested();
                        await using var grant = new MySqlCommand(
                            $"GRANT ALL PRIVILEGES ON `{db}`.* TO '{dbUser}'@'{host}';",
                            conn);
                        await grant.ExecuteNonQueryAsync(ct);
                    }
                }

                await using var flush = new MySqlCommand("FLUSH PRIVILEGES;", conn);
                await flush.ExecuteNonQueryAsync(ct);
            }

            var dbList = string.Join(", ", databases);
            return (true, $"Created {databases.Count} database(s): {dbList}.");
        }
        catch (MySqlException ex)
        {
            return (false,
                $"MySQL error {ex.Number}: {ex.Message}. " +
                $"Check admin credentials in Settings (Connection tab).");
        }
        catch (Exception ex)
        {
            return (false, $"Could not connect to MySQL: {ex.Message}.");
        }
    }

    /// <summary>
    /// Drops each database listed in the manifest (if it exists) then recreates it empty.
    /// If a database does not exist yet it is simply created. User accounts and grants are
    /// also recreated so the game server can connect to the fresh databases.
    /// </summary>
    public async Task<(bool Success, string Message)> ClearDatabasesAsync(
        string installPath, CancellationToken ct)
    {
        var manifest = RepackManifest.Load(installPath);
        if (manifest is null)
            return (false, "repack-manifest.json not found in the install directory.");

        var dbUser    = manifest.DbUser;
        var dbPass    = manifest.DbPass;
        var databases = manifest.AllDatabases().ToList();

        if (databases.Count == 0)
            return (false, "repack-manifest.json: no database names defined.");

        try
        {
            await using var conn = OpenAdminConnection(manifest.DbHost, manifest.DbPort);
            await conn.OpenAsync(ct);

            foreach (var db in databases)
            {
                ct.ThrowIfCancellationRequested();
                await using var drop = new MySqlCommand($"DROP DATABASE IF EXISTS `{db}`;", conn);
                await drop.ExecuteNonQueryAsync(ct);

                await using var create = new MySqlCommand(
                    $"CREATE DATABASE `{db}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;", conn);
                await create.ExecuteNonQueryAsync(ct);
            }

            if (!string.IsNullOrWhiteSpace(dbUser))
            {
                foreach (var host in new[] { "127.0.0.1", "localhost" })
                {
                    ct.ThrowIfCancellationRequested();

                    await using var createUser = new MySqlCommand(
                        $"CREATE USER IF NOT EXISTS '{dbUser}'@'{host}' IDENTIFIED BY '{dbPass}';", conn);
                    await createUser.ExecuteNonQueryAsync(ct);

                    foreach (var db in databases)
                    {
                        ct.ThrowIfCancellationRequested();
                        await using var grant = new MySqlCommand(
                            $"GRANT ALL PRIVILEGES ON `{db}`.* TO '{dbUser}'@'{host}';", conn);
                        await grant.ExecuteNonQueryAsync(ct);
                    }
                }

                await using var flush = new MySqlCommand("FLUSH PRIVILEGES;", conn);
                await flush.ExecuteNonQueryAsync(ct);
            }

            var dbList = string.Join(", ", databases);
            return (true, $"Cleared and recreated {databases.Count} database(s): {dbList}.");
        }
        catch (MySqlException ex)
        {
            return (false,
                $"MySQL error {ex.Number}: {ex.Message}. " +
                $"Check admin credentials in Settings (Connection tab).");
        }
        catch (Exception ex)
        {
            return (false, $"Could not connect to MySQL: {ex.Message}.");
        }
    }

    // -- Shared helpers --------------------------------------------------------

    private void AddApiKeyHeader(HttpRequestMessage req)
    {
        var key = _apiKeys.ActiveKey;
        if (!string.IsNullOrEmpty(key))
            req.Headers.TryAddWithoutValidation("X-API-Key", key);
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024         => $"{bytes / 1_024.0:F0} KB",
        _                => $"{bytes} B"
    };

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    // -- Log directory creation ------------------------------------------------

    private static void CreateServerLogDirs(string installPath, RepackManifest? manifest,
        IProgress<InstallProgress>? progress)
    {
        if (!Directory.Exists(installPath)) return;

        // Prefer manifest-declared conf paths; fall back to scanning the whole tree.
        var candidateAbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (manifest is not null)
        {
            foreach (var rel in new[] { manifest.WorldConf, manifest.AuthConf })
            {
                if (!string.IsNullOrWhiteSpace(rel))
                    candidateAbs.Add(Path.GetFullPath(Path.Combine(installPath, rel)));
            }
        }

        // Also scan for *.conf and *.conf.dist so the directory is created even
        // when the manifest is absent or its paths don't match the extracted layout.
        foreach (var f in Directory.EnumerateFiles(installPath, "*.conf",      SearchOption.AllDirectories))
            candidateAbs.Add(Path.GetFullPath(f));
        foreach (var f in Directory.EnumerateFiles(installPath, "*.conf.dist", SearchOption.AllDirectories))
            candidateAbs.Add(Path.GetFullPath(f));

        foreach (var confAbs in candidateAbs)
        {
            if (!File.Exists(confAbs)) continue;

            var logsDirRaw = ReadConfValue(confAbs, "LogsDir");
            if (string.IsNullOrWhiteSpace(logsDirRaw)) continue;

            // Resolve relative to installPath (the server's working directory when it runs),
            // not relative to the conf file's own directory. AzerothCore/TrinityCore always
            // use the exe's working dir as the base for relative conf paths, so
            // "../../Logs/Wotlk-Bots" from servers/<slug>/ correctly reaches the app-root Logs.
            var logsAbs = Path.IsPathRooted(logsDirRaw)
                ? logsDirRaw
                : Path.GetFullPath(Path.Combine(installPath, logsDirRaw));

            if (!Directory.Exists(logsAbs))
            {
                Directory.CreateDirectory(logsAbs);
                progress?.Report(new InstallProgress("Setting up", 99, 0,
                    $"Created log directory: {logsAbs}"));
            }
        }
    }

    /// <summary>
    /// Returns the value for <paramref name="key"/> from an AzerothCore / TrinityCore
    /// .conf file, stripping surrounding double-quotes.  Returns <c>null</c> when the
    /// key is not present.
    /// </summary>
    private static string? ReadConfValue(string confPath, string key)
    {
        foreach (var line in File.ReadLines(confPath))
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            var eq = trimmed.IndexOf('=');
            if (eq < 0) continue;

            if (!string.Equals(trimmed[..eq].TrimEnd(), key, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = trimmed[(eq + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1].Trim();

            return value;
        }
        return null;
    }

    // -- API response models ---------------------------------------------------

    private sealed record FileManifest(
        [property: JsonPropertyName("files")] List<FileManifestEntry> Files);

    private sealed record FileManifestEntry(
        [property: JsonPropertyName("name")]           string   Name,
        [property: JsonPropertyName("size")]           double   SizeMb,
        [property: JsonPropertyName("hash")]           string   Hash,
        [property: JsonPropertyName("path")]           string   Path,
        [property: JsonPropertyName("last_write_utc")] DateTime LastWriteTime);
}

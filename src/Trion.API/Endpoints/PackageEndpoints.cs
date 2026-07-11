using System.Globalization;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Trion.API.Data;
using Trion.API.Models;
using Trion.API.Utilities;

namespace Trion.API.Endpoints;

public static class PackageEndpoints
{
    public static WebApplication MapPackageEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/Trion").WithTags("Packages");

        g.MapGet("Ping", Ping);
        g.MapGet("GetExternalIPv4", GetExternalIPv4);
        g.MapGet("GetFileVersion",  GetFileVersion);
        g.MapGet("catalog",         GetPackageCatalogAsync);
        g.MapGet("DownloadFile",    DownloadFileGet).RequireRateLimiting("download").RequireRateLimiting("downloads-ip");
        g.MapPost("DownloadFile",   DownloadFilePost).RequireRateLimiting("download").RequireRateLimiting("downloads-ip");
        g.MapPost("RepairSPP",      RepairSpp);
        g.MapGet("DownloadSpeedTest", DownloadSpeedTest);

        return app;
    }

    // -- Ping ------------------------------------------------------------------

    private static IResult Ping(HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        return Results.Ok();
    }

    // -- External IP -----------------------------------------------------------

    private static IResult GetExternalIPv4(HttpContext ctx)
    {
        var addr  = ctx.Connection.RemoteIpAddress;
        var ipStr = addr?.IsIPv4MappedToIPv6 == true
            ? addr.MapToIPv4().ToString()
            : addr?.ToString();

        return string.IsNullOrEmpty(ipStr)
            ? Results.BadRequest(new { message = "Unable to determine IP address." })
            : Results.Ok(new { iPv4Address = ipStr });
    }

    // -- File versions ---------------------------------------------------------

    private static async Task<IResult> GetFileVersion(
        [FromHeader(Name = "X-API-Key")] string? key,
        IConfiguration                           cfg,
        TrionDbAccess                            db,
        IMemoryCache                             cache)
    {
        var isEarly = await VerifyKeyAsync(key, db, cache);
        var tier    = isEarly ? "EarlyAccess" : "Default";

        return Results.Ok(new
        {
            ClassicSPP = ReadVersion(cfg, "classicSPP", tier),
            TbcSPP     = ReadVersion(cfg, "tbcSPP",     tier),
            WotlkSPP   = ReadVersion(cfg, "wotlkSPP",   tier),
            CataSPP    = ReadVersion(cfg, "cataSPP",     tier),
            MopSPP     = ReadVersion(cfg, "mopSPP",      tier),
            Trion      = ReadVersion(cfg, "trion",       tier),
            Database   = ReadVersion(cfg, "database",    tier),
        });
    }

    // -- Download ZIP (GET) ----------------------------------------------------

    private static async Task<IResult> DownloadFileGet(
        [FromQuery]                      string  emulator,
        [FromHeader(Name = "X-API-Key")] string? key,
        IConfiguration                           cfg,
        TrionDbAccess                            db,
        IMemoryCache                             cache,
        ILogger<Program>                         log)
    {
        if (string.IsNullOrWhiteSpace(emulator))
            return Results.BadRequest(new { message = "emulator parameter is required." });

        var isEarly = await VerifyKeyAsync(key, db, cache);
        var tier    = isEarly ? "EarlyAccess" : "Default";
        var cfgKey  = NormalizeEmulatorName(emulator);
        var zipPath = cfg[$"{cfgKey}:ZipPath:{tier}"];

        if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
        {
            log.LogWarning("DownloadFile: package not found for '{Emulator}' (tier={Tier}) at '{Path}'",
                emulator, tier, zipPath);
            return Results.NotFound(new { message = $"Package '{emulator}' is not available." });
        }

        var fullPath = Path.GetFullPath(zipPath);
        log.LogInformation("Streaming '{File}' ({Tier}) -- {Bytes:N0} bytes",
            Path.GetFileName(fullPath), tier, new FileInfo(fullPath).Length);

        return Results.File(File.OpenRead(fullPath), "application/zip", Path.GetFileName(fullPath));
    }

    // -- Download single file (POST, repair) -----------------------------------

    private static async Task<IResult> DownloadFilePost(
        [FromBody]                       DownloadFileRequest req,
        [FromHeader(Name = "X-API-Key")] string?             key,
        IConfiguration                                       cfg,
        TrionDbAccess                                        db,
        IMemoryCache                                         cache,
        ILogger<Program>                                     log)
    {
        if (string.IsNullOrWhiteSpace(req.Emulator) || string.IsNullOrWhiteSpace(req.FilePath))
            return Results.BadRequest(new { message = "emulator and filePath are required." });

        var cfgKey   = NormalizeEmulatorName(req.Emulator);
        var isEarly  = await VerifyKeyAsync(key, db, cache);
        var tier     = isEarly ? "EarlyAccess" : "Default";
        var filesDir = cfg[$"{cfgKey}:FilesPath:{tier}"];

        if (isEarly && string.IsNullOrEmpty(filesDir))
        {
            tier     = "Default";
            filesDir = cfg[$"{cfgKey}:FilesPath:Default"];
        }

        if (string.IsNullOrEmpty(filesDir) || !Directory.Exists(filesDir))
            return Results.NotFound(new { message = $"Files directory for '{req.Emulator}' is not configured." });

        var prefix   = $"/{req.Emulator.Trim('/')}/";
        var relative = req.FilePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? req.FilePath[prefix.Length..]
            : req.FilePath.TrimStart('/');

        var baseDir  = Path.GetFullPath(filesDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(filesDir, relative.Replace('/', Path.DirectorySeparatorChar)));

        if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { message = "Invalid file path." });

        if (!File.Exists(fullPath))
            return Results.NotFound(new { message = "File not found." });

        log.LogInformation("Repair-streaming '{File}' ({Emulator}, {Tier})",
            Path.GetFileName(fullPath), req.Emulator, tier);

        return Results.File(File.OpenRead(fullPath), "application/octet-stream", Path.GetFileName(fullPath));
    }

    // -- Repair manifest -------------------------------------------------------

    private static async Task<IResult> RepairSpp(
        [FromBody]                       RepairSppRequest req,
        [FromHeader(Name = "X-API-Key")] string?          key,
        IConfiguration                                    cfg,
        TrionDbAccess                                     db,
        IMemoryCache                                      cache)
    {
        if (string.IsNullOrWhiteSpace(req.Emulator))
            return Results.BadRequest(new { message = "emulator is required." });

        var cfgKey   = NormalizeEmulatorName(req.Emulator);
        var isEarly  = await VerifyKeyAsync(key, db, cache);
        var tier     = isEarly ? "EarlyAccess" : "Default";
        var filesDir = cfg[$"{cfgKey}:FilesPath:{tier}"];

        if (isEarly && string.IsNullOrEmpty(filesDir))
        {
            tier     = "Default";
            filesDir = cfg[$"{cfgKey}:FilesPath:Default"];
        }

        if (string.IsNullOrEmpty(filesDir))
            return Results.NotFound(new { message = $"Emulator '{req.Emulator}' is not configured." });

        var manifestPath = Path.Combine(filesDir, "manifest.json");
        if (!File.Exists(manifestPath))
            return Results.NotFound(new { message = $"Manifest not found for '{req.Emulator}'." });

        var cacheKey = $"manifest_{cfgKey}_{tier}";
        if (!cache.TryGetValue<string>(cacheKey, out var json))
        {
            json = await File.ReadAllTextAsync(manifestPath);
            cache.Set(cacheKey, json, TimeSpan.FromMinutes(5));
        }

        return Results.Content(json!, "application/json");
    }

    // -- Speed test ------------------------------------------------------------

    private static IResult DownloadSpeedTest([FromQuery] int sizeInMB = 100)
    {
        sizeInMB = Math.Clamp(sizeInMB, 1, 1_000);
        return Results.Stream(
            Network.GenerateRandomStream(sizeInMB),
            contentType:      "application/octet-stream",
            fileDownloadName: $"speedtest_{sizeInMB}MB.bin",
            enableRangeProcessing: false);
    }

    // -- GET /Trion/catalog -- dynamic repack catalog (anonymous, JWT optional) --

    /// <summary>
    /// Returns a PackageCatalogResponse built from the RepackService /api/repacks feed.
    /// Version is derived from lastBuiltAt so the catalog updates automatically after each build.
    /// Availability is determined by the caller's api_tier JWT claim vs each entry's accessType.
    /// </summary>
    private static async Task<IResult> GetPackageCatalogAsync(
        HttpContext        ctx,
        IConfiguration     cfg,
        TrionDbAccess      db,
        IHttpClientFactory httpFactory,
        IMemoryCache       cache,
        CancellationToken  ct)
    {
        var tier    = ctx.User.FindFirstValue("api_tier") ?? "guest";
        var isAdmin = ctx.User.IsInRole("admin");
        var apiKey  = ctx.Request.Headers["X-API-Key"].FirstOrDefault();
        var isEarly = await VerifyKeyAsync(apiKey, db, cache);

        var svc    = await db.QuerySingleAsync<RepackServiceInstance>(TrionSql.SelectLeastLoadedRepackService);
        var svcUrl = svc is not null
            ? svc.Url.TrimEnd('/')
            : (cfg["Repack:ServiceUrl"] ?? "").TrimEnd('/');

        List<JsonElement> repacks = [];
        if (!string.IsNullOrEmpty(svcUrl))
        {
            try
            {
                var http = httpFactory.CreateClient("repack");
                var resp = await http.GetAsync($"{svcUrl}/api/repacks", ct);
                if (resp.IsSuccessStatusCode)
                    repacks = await resp.Content.ReadFromJsonAsync<List<JsonElement>>(
                        cancellationToken: ct) ?? [];
            }
            catch { }
        }

        var callerRank = TierRank(tier);
        var isTester   = string.Equals(tier, "tester", StringComparison.OrdinalIgnoreCase);
        var catalog    = repacks.Select(r =>
        {
            var expansion  = RepackStr(r, "expansion").ToLowerInvariant();
            var baseType   = RepackStr(r, "baseType") is { Length: > 0 } bt ? bt : RepackStr(r, "emulatorId");
            var accessType = RepackStr(r, "accessType", "free");
            var name       = RepackStr(r, "name") is { Length: > 0 } n ? n : RepackStr(r, "slug");
            var isTesterOnly = string.Equals(accessType, "tester", StringComparison.OrdinalIgnoreCase);
            var available  = isAdmin || isEarly
                || (isTesterOnly ? isTester : callerRank >= TierRank(accessType));
            return (object) new
            {
                emulator         = expansion,
                displayName      = name,
                description      = "",
                baseType,
                expansion,
                version          = RepackStr(r, "buildVersion") is { Length: > 0 } bv
                                       ? bv
                                       : FormatRepackVersion(RepackStr(r, "lastBuiltAt")),
                available,
                minTier          = accessType,
                autoDbImport     = baseType.Contains("azerothcore", StringComparison.OrdinalIgnoreCase),
                serverExecutable  = RepackStr(r, "serverExecutable", "worldserver.exe"),
                authExecutable    = RepackStr(r, "authExecutable",   "authserver.exe"),
                worldConf         = RepackStr(r, "worldConf",        "worldserver.conf"),
                authConf          = RepackStr(r, "authConf",         "authserver.conf"),
                requiredDatabases = r.TryGetProperty("requiredDatabases", out var dbsProp)
                    ? dbsProp.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                    : new[] { "auth", "world", "characters" },
                repackSlug        = RepackStr(r, "slug"),
            };
        }).ToList();

        return Results.Ok(new { tier, earlyAccess = isEarly, catalog });
    }

    // -- Helpers ---------------------------------------------------------------

    private static async Task<bool> VerifyKeyAsync(string? key, TrionDbAccess db, IMemoryCache cache)
    {
        if (string.IsNullOrEmpty(key)) return false;

        var cacheKey = $"key_{key}";
        if (cache.TryGetValue<bool>(cacheKey, out var cached)) return cached;

        var valid = await db.QuerySingleAsync<bool>(TrionSql.GetKeyExists, new { ApiKey = key });
        cache.Set(cacheKey, valid, TimeSpan.FromMinutes(30));
        return valid;
    }

    private static string ReadVersion(IConfiguration cfg, string emulator, string tier)
    {
        var value = cfg[$"{emulator}:Version:{tier}"];
        if (string.IsNullOrEmpty(value)) return "--";
        try
        {
            return File.Exists(value) ? File.ReadAllText(value).Trim() : value;
        }
        catch { return "--"; }
    }

    private static string NormalizeEmulatorName(string emulator) =>
        emulator.Trim().ToLowerInvariant() switch
        {
            "classic" => "classicSPP",
            "tbc"     => "tbcSPP",
            "wotlk"   => "wotlkSPP",
            "cata"    => "cataSPP",
            "mop"     => "mopSPP",
            var other => other   // "trion", "database", "mysql"
        };

    // Tier hierarchy used by GetPackageCatalogAsync
    private static readonly string[] TierOrder =
        ["guest", "free", "support", "guardian", "champion", "legend"];

    private static int TierRank(string t)
    {
        var i = Array.IndexOf(TierOrder, t.ToLowerInvariant().Trim());
        return i < 0 ? 0 : i;
    }

    private static string FormatRepackVersion(string iso) =>
        !string.IsNullOrEmpty(iso)
        && DateTime.TryParse(iso, null, DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToString("yyyy.MM.dd")
            : "--";

    private static string RepackStr(JsonElement e, string key, string fallback = "") =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? fallback
            : fallback;
}

public sealed record DownloadFileRequest(string Emulator, string FilePath);
public sealed record RepairSppRequest(string Emulator);

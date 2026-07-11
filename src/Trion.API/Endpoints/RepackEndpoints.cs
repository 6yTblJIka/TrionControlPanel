using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Trion.API.Data;
using Trion.API.Models;

namespace Trion.API.Endpoints;

public static class RepackEndpoints
{
    public static WebApplication MapRepackEndpoints(this WebApplication app)
    {
        // Authenticated user endpoints
        var g = app.MapGroup("/repack").WithTags("Repack").RequireAuthorization();
        g.MapPost("request",        SubmitRequest);
        g.MapGet("status/{id:int}", GetStatus);
        g.MapGet("my-builds",       GetMyBuilds);
        g.MapGet("catalog",         GetCatalog);

        // Callback from RepackService -- no JWT, HMAC-verified inside the handler
        app.MapPost("/repack/callback", HandleCallback).WithTags("Repack");

        return app;
    }

    // -- POST /repack/request --------------------------------------------------

    private static async Task<IResult> SubmitRequest(
        [FromBody]       RepackRequest    req,
        HttpContext                       ctx,
        TrionDbAccess                    db,
        IHttpClientFactory               httpFactory,
        IConfiguration                   cfg,
        ILogger<Program>                 log,
        CancellationToken                ct)
    {
        if (string.IsNullOrWhiteSpace(req.RealmName) || req.RealmName.Length > 64)
            return Results.BadRequest(new { error = "realm_name is required (max 64 chars)." });

        var userId = GetUserId(ctx);
        if (userId == 0) return Results.Unauthorized();

        // -- Compute fingerprint for cache lookup ------------------------------
        var fingerprint = ComputeFingerprint(
            req.EmulatorId ?? "azerothcore",
            req.AcTag ?? "master",
            req.SelectedModules ?? []);

        // -- Check for a cached completed build with identical parameters ------
        var cached = await db.QuerySingleAsync<RepackBuild>(
            TrionSql.GetRepackBuildCached, new { Fingerprint = fingerprint });
        if (cached is not null)
        {
            log.LogInformation("Repack cache hit (fingerprint={FP}, cached_build={Id})", fingerprint, cached.Id);
            return Results.Ok(new
            {
                buildId      = cached.Id,
                serviceJobId = cached.ServiceJobId,
                status       = "done",
                downloadUrl  = cached.DownloadUrl,
                cached       = true,
            });
        }

        // -- Insert build record -----------------------------------------------
        await db.ExecuteAsync(TrionSql.InsertRepackBuild, new
        {
            UserId    = userId,
            RealmName = req.RealmName,
            EmulatorId = req.EmulatorId ?? "azerothcore",
        });
        var buildId = await db.QuerySingleAsync<int>("SELECT LAST_INSERT_ID()");

        // -- Pick a service instance from the registry (fall back to env vars) -
        var serviceInstance = await db.QuerySingleAsync<RepackServiceInstance>(
            TrionSql.SelectLeastLoadedRepackService);

        string serviceUrl;
        string hmacSecret;

        if (serviceInstance is not null)
        {
            serviceUrl = serviceInstance.Url.TrimEnd('/');
            hmacSecret = serviceInstance.HmacSecret;
        }
        else
        {
            // Legacy single-instance fallback via appsettings / env vars
            serviceUrl = (cfg["Repack:ServiceUrl"] ?? "").TrimEnd('/');
            hmacSecret = cfg["Repack:HmacSecret"] ?? "";
        }

        if (string.IsNullOrEmpty(serviceUrl))
        {
            log.LogError("No repack service available (registry empty and Repack:ServiceUrl not configured).");
            return Results.Problem("Repack service is not configured.", statusCode: 503);
        }

        // -- Build the callback URL so RepackService POSTs back to us ---------
        var publicBase  = (cfg["Repack:PublicBaseUrl"] ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}").TrimEnd('/');
        var callbackUrl = $"{publicBase}/repack/callback";

        var payload = JsonSerializer.Serialize(new
        {
            phoenixJobId    = buildId.ToString(),
            userId,
            emulatorId      = req.EmulatorId ?? "azerothcore",
            selectedModules = req.SelectedModules ?? [],
            realmName       = req.RealmName,
            dbHost          = req.DbHost,
            dbPort          = req.DbPort.ToString(),
            dbUser          = req.DbUser,
            dbPass          = req.DbPass,
            dbName          = req.DbName,
            authDbName      = req.AuthDbName,
            charDbName      = req.CharDbName,
            bindIp          = req.BindIp,
            externalIp      = req.ExternalIp,
            realmId         = req.RealmId,
            worldPort       = req.WorldPort,
            authPort        = req.AuthPort,
            azerothCoreTag  = req.AcTag,
            repackSlug      = req.RepackSlug,
            callbackUrl,
        });

        var (signature, timestamp) = ComputeSignature(payload, hmacSecret);

        string serviceJobId;
        try
        {
            var http     = httpFactory.CreateClient("repack");
            var request  = new HttpRequestMessage(HttpMethod.Post, $"{serviceUrl}/api/build")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Signature-256", signature);
            request.Headers.Add("X-Timestamp", timestamp);

            var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                log.LogError("RepackService returned {Code} for build #{Id}: {Body}",
                    (int)response.StatusCode, buildId, body);
                return Results.Problem("Build service rejected the request.", statusCode: 503);
            }

            using var doc  = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            serviceJobId   = doc.RootElement.GetProperty("jobId").GetString() ?? "";
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to reach RepackService for build #{Id}.", buildId);
            return Results.Problem("Could not reach the build service.", statusCode: 503);
        }

        // -- Mark queued; track which instance is handling it ------------------
        if (serviceInstance is not null)
        {
            await db.ExecuteAsync(TrionSql.IncrementRepackServiceJobs,
                new { Id = serviceInstance.Id });

            await db.ExecuteAsync(TrionSql.UpdateRepackBuildQueuedWithService, new
            {
                ServiceJobId     = serviceJobId,
                ServiceId        = serviceInstance.Id,
                BuildFingerprint = fingerprint,
                Id               = buildId,
            });
        }
        else
        {
            await db.ExecuteAsync(TrionSql.UpdateRepackBuildQueued,
                new { ServiceJobId = serviceJobId, Id = buildId });
        }

        log.LogInformation(
            "Repack build #{Id} queued (serviceJobId={SvcId}, user={UserId}, serviceId={SvcInstanceId})",
            buildId, serviceJobId, userId, serviceInstance?.Id);

        return Results.Accepted($"/repack/status/{buildId}", new
        {
            buildId,
            serviceJobId,
            status = "queued",
        });
    }

    // -- GET /repack/catalog -- proxied from the least-loaded RepackService --------

    private static async Task<IResult> GetCatalog(
        TrionDbAccess      db,
        IHttpClientFactory httpFactory,
        IConfiguration     cfg,
        ILogger<Program>   log,
        CancellationToken  ct)
    {
        // Pick a service instance (same priority as build dispatch)
        var serviceInstance = await db.QuerySingleAsync<RepackServiceInstance>(
            TrionSql.SelectLeastLoadedRepackService);

        string serviceUrl = serviceInstance is not null
            ? serviceInstance.Url.TrimEnd('/')
            : (cfg["Repack:ServiceUrl"] ?? "").TrimEnd('/');

        if (string.IsNullOrEmpty(serviceUrl))
            return Results.Problem("No repack service is configured.", statusCode: 503);

        try
        {
            var http     = httpFactory.CreateClient("repack");
            var response = await http.GetAsync($"{serviceUrl}/api/repacks", ct);

            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("RepackService /api/repacks returned {Code}", (int)response.StatusCode);
                return Results.Problem("Could not fetch repack catalog from the build service.", statusCode: 502);
            }

            var items = await response.Content.ReadFromJsonAsync<List<RepackCatalogItem>>(
                cancellationToken: ct);

            return Results.Ok(items ?? []);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to fetch repack catalog from {Url}", serviceUrl);
            return Results.Problem("Could not reach the build service.", statusCode: 503);
        }
    }

    // -- GET /repack/status/{id} -----------------------------------------------

    private static async Task<IResult> GetStatus(
        int           id,
        HttpContext   ctx,
        TrionDbAccess db)
    {
        var build = await db.QuerySingleAsync<RepackBuild>(TrionSql.GetRepackBuildById, new { Id = id });
        if (build is null) return Results.NotFound();

        var userId  = GetUserId(ctx);
        var isAdmin = ctx.User.IsInRole("admin");
        if (build.UserId != userId && !isAdmin) return Results.Forbid();

        return Results.Ok(ToResponse(build));
    }

    // -- GET /repack/my-builds -------------------------------------------------

    private static async Task<IResult> GetMyBuilds(
        HttpContext   ctx,
        TrionDbAccess db)
    {
        var userId = GetUserId(ctx);
        if (userId == 0) return Results.Unauthorized();

        var builds = await db.QueryAsync<RepackBuild>(
            TrionSql.GetRepackBuildsForUser, new { UserId = userId });

        return Results.Ok(builds.Select(ToResponse));
    }

    // -- POST /repack/callback -- called by RepackService -----------------------

    private static async Task<IResult> HandleCallback(
        HttpRequest      request,
        TrionDbAccess    db,
        IConfiguration   cfg,
        ILogger<Program> log,
        CancellationToken ct)
    {
        // Read raw body -- we need it for both parsing and HMAC verification
        request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(request.Body, leaveOpen: true))
            rawBody = await reader.ReadToEndAsync(ct);
        request.Body.Position = 0;

        var sigHeader = request.Headers["X-Signature-256"].FirstOrDefault() ?? "";
        var tsHeader  = request.Headers["X-Timestamp"].FirstOrDefault();

        // -- Parse first (unauthenticated) to find build and its service secret --
        RepackCallbackPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<RepackCallbackPayload>(
                rawBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException) { return Results.BadRequest(); }

        if (payload is null || string.IsNullOrWhiteSpace(payload.PhoenixJobId))
            return Results.BadRequest();

        if (!int.TryParse(payload.PhoenixJobId, out var buildId))
            return Results.BadRequest();

        // -- Resolve the HMAC secret: per-service if known, global as fallback -
        var build = await db.QuerySingleAsync<dynamic>(
            "SELECT b.service_id AS ServiceId, s.hmac_secret AS HmacSecret " +
            "FROM RepackBuilds b " +
            "LEFT JOIN repack_services s ON s.id = b.service_id " +
            "WHERE b.Id = @Id LIMIT 1",
            new { Id = buildId });

        string hmacSecret = (build?.HmacSecret as string)
            ?? cfg["Repack:HmacSecret"]
            ?? "";

        // -- Verify HMAC-SHA256 + timestamp (replay protection) ---------------
        if (!VerifySignature(rawBody, sigHeader, hmacSecret, tsHeader))
        {
            log.LogWarning("Repack callback: invalid HMAC from {IP} for build #{Id}",
                request.HttpContext.Connection.RemoteIpAddress, buildId);
            return Results.Unauthorized();
        }

        // -- Update build status -----------------------------------------------
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "queued","cloning","compiling","configuring","packaging","done","failed" };
        var status = allowed.Contains(payload.Status) ? payload.Status.ToLowerInvariant() : "failed";

        await db.ExecuteAsync(TrionSql.UpdateRepackBuildFromCallback, new
        {
            Id           = buildId,
            Status       = status,
            DownloadUrl  = payload.DownloadUrl,
            ErrorMessage = payload.Error,
        });

        // -- Release service slot on terminal status ---------------------------
        if (status is "done" or "failed")
        {
            int? svcId = build?.ServiceId is not null
                ? (int?) Convert.ToInt32(build.ServiceId)
                : null;

            if (svcId is > 0)
                await db.ExecuteAsync(TrionSql.DecrementRepackServiceJobs, new { Id = svcId });
        }

        log.LogInformation("Repack callback for build #{Id}: status={Status}", buildId, status);
        return Results.Ok(new { ok = true });
    }

    // -- Helpers ---------------------------------------------------------------

    /// <summary>
    /// Deterministic 16-char hex fingerprint. Must exactly match
    /// FingerprintService.Compute() in TrionRepackService.
    /// </summary>
    private static string ComputeFingerprint(string emulatorId, string tag, IEnumerable<string> modules)
    {
        var sorted = modules
            .Select(m => m.Trim().ToLowerInvariant())
            .Where(m => m.Length > 0)
            .OrderBy(m => m, StringComparer.Ordinal);

        var input = $"{emulatorId.ToLowerInvariant()}:{tag.Trim()}:{string.Join(",", sorted)}";
        var hash  = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static int GetUserId(HttpContext ctx)
    {
        var claim = ctx.User.FindFirst("sub") ?? ctx.User.FindFirst("id");
        return int.TryParse(claim?.Value, out var id) ? id : 0;
    }

    private static RepackBuildResponse ToResponse(RepackBuild b) => new(
        b.Id, b.ServiceJobId, b.Status, b.RealmName, b.EmulatorId,
        b.DownloadUrl, b.ErrorMessage, b.CreatedAt, b.UpdatedAt);

    private const int MaxAgeSeconds = 300; // 5 minutes -- matches SignatureVerifier.MaxAgeSeconds

    /// <summary>Sign a payload, returning (sha256=hex, unix-epoch-string).</summary>
    private static (string signature, string timestamp) ComputeSignature(string payload, string secret)
    {
        var ts         = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signedData = $"ts={ts}\n{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash       = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedData));
        return ("sha256=" + Convert.ToHexString(hash).ToLowerInvariant(), ts);
    }

    private static bool VerifySignature(string rawBody, string sigHeader, string secret, string? tsHeader = null)
    {
        if (string.IsNullOrEmpty(secret) || !sigHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            return false;

        // Replay check -- optional: old callers without X-Timestamp still pass
        if (!string.IsNullOrWhiteSpace(tsHeader))
        {
            if (!long.TryParse(tsHeader, out var ts)) return false;
            var age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts;
            if (age < 0 || age > MaxAgeSeconds) return false;
        }

        var signedData = string.IsNullOrWhiteSpace(tsHeader)
            ? rawBody
            : $"ts={tsHeader}\n{rawBody}";

        using var hmac    = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedData));
        var expected      = "sha256=" + Convert.ToHexString(expectedBytes).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(sigHeader));
    }
}

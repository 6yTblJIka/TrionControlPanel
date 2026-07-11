using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Trion.API.Data;
using Trion.API.Models;

namespace Trion.API.Endpoints;

public static class RepackServiceEndpoints
{
    public static WebApplication MapRepackServiceEndpoints(this WebApplication app)
    {
        // Admin-only management
        var admin = app.MapGroup("/repack-services")
            .WithTags("RepackServices")
            .RequireAuthorization("AdminPolicy");

        admin.MapGet("",                   GetAll);
        admin.MapPost("",                  CreateInvite);
        admin.MapDelete("/invites/{id:int}", RevokeInvite);
        admin.MapPost("/{id:int}/toggle",  ToggleService);

        // Self-registration -- no JWT, HMAC-verified inside handler
        app.MapPost("/repack-services/register", Register).WithTags("RepackServices");

        return app;
    }

    // -- GET /repack-services --------------------------------------------------

    private static async Task<IResult> GetAll(TrionDbAccess db)
    {
        var services = await db.QueryAsync<RepackServiceInstance>(TrionSql.GetAllRepackServices);
        var invites  = await db.QueryAsync<RepackServiceInviteRow>(TrionSql.GetAllRepackServiceInvites);

        return Results.Ok(new { services, invites });
    }

    // -- POST /repack-services -- generate an invite key ------------------------

    private static async Task<IResult> CreateInvite(
        HttpContext      ctx,
        TrionDbAccess    db,
        ILogger<Program> log)
    {
        var adminUserId = GetAdminUserId(ctx);

        var key  = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(); // 48-char
        var note = ctx.Request.Query["note"].FirstOrDefault();

        await db.ExecuteAsync(TrionSql.InsertRepackServiceInvite, new
        {
            InviteKey = key,
            Note      = note,
            CreatedBy = adminUserId,
        });

        log.LogInformation("Repack service invite created by admin {User}", adminUserId);

        // Return the key once -- caller must store it
        return Results.Ok(new { inviteKey = key });
    }

    // -- DELETE /repack-services/invites/{id} ----------------------------------

    private static async Task<IResult> RevokeInvite(
        int           id,
        TrionDbAccess db,
        ILogger<Program> log)
    {
        var rows = await db.ExecuteAsync(TrionSql.RevokeRepackServiceInvite, new { Id = id });
        if (rows == 0) return Results.Conflict(new { error = "Invite not found or already claimed." });

        log.LogInformation("Repack service invite #{Id} revoked", id);
        return Results.Ok(new { ok = true });
    }

    // -- POST /repack-services/{id}/toggle -------------------------------------

    private static async Task<IResult> ToggleService(int id, TrionDbAccess db)
    {
        await db.ExecuteAsync(TrionSql.ToggleRepackService, new { Id = id });
        return Results.Ok(new { ok = true });
    }

    // -- POST /repack-services/register -- called by a TrionRepackService node --

    private static async Task<IResult> Register(
        HttpRequest      request,
        TrionDbAccess    db,
        IHttpClientFactory httpFactory,
        ILogger<Program> log,
        CancellationToken ct)
    {
        request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(request.Body, leaveOpen: true))
            rawBody = await reader.ReadToEndAsync(ct);
        request.Body.Position = 0;

        RegisterServiceRequest? payload;
        try
        {
            payload = JsonSerializer.Deserialize<RegisterServiceRequest>(
                rawBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException) { return Results.BadRequest(new { error = "Invalid JSON." }); }

        if (payload is null
            || string.IsNullOrWhiteSpace(payload.InviteKey)
            || string.IsNullOrWhiteSpace(payload.Name)
            || string.IsNullOrWhiteSpace(payload.Url))
        {
            return Results.BadRequest(new { error = "invite_key, name, and url are all required." });
        }

        if (payload.Name.Length > 100)
            return Results.UnprocessableEntity(new { error = "name must be 100 characters or fewer." });

        // -- Validate invite ---------------------------------------------------

        var invite = await db.QuerySingleAsync<RepackServiceInviteRow>(
            TrionSql.GetRepackServiceInviteByKey,
            new { InviteKey = payload.InviteKey });

        if (invite is null)
        {
            log.LogWarning("Repack registration: unknown invite key from {IP}",
                request.HttpContext.Connection.RemoteIpAddress);
            return Results.Unauthorized();
        }

        if (invite.ClaimedAt is not null)
            return Results.Conflict(new { error = "This invite key has already been used." });

        if (invite.ExpiresAt is not null && invite.ExpiresAt < DateTime.UtcNow)
            return Results.Unauthorized();

        // -- Create service record ---------------------------------------------

        var url = payload.Url.TrimEnd('/');

        await db.ExecuteAsync(TrionSql.InsertRepackService, new
        {
            Name               = payload.Name,
            Url                = url,
            HmacSecret         = payload.InviteKey,   // invite key IS the permanent secret
            MaxConcurrentJobs  = payload.MaxConcurrentJobs,
        });
        var serviceId = await db.QuerySingleAsync<int>("SELECT LAST_INSERT_ID()");

        // -- Claim atomically --------------------------------------------------

        var claimed = await db.ExecuteAsync(TrionSql.ClaimRepackServiceInvite,
            new { Id = invite.Id, ServiceId = serviceId });

        if (claimed == 0)
        {
            // Race: another request claimed first -- delete the service record we just created
            await db.ExecuteAsync("DELETE FROM repack_services WHERE Id = @Id", new { Id = serviceId });
            return Results.Conflict(new { error = "Invite key was just claimed by another request." });
        }

        // -- Probe reachability ------------------------------------------------

        var reachable = await ProbeServiceAsync(url, httpFactory, ct);

        log.LogInformation(
            "RepackService instance registered: id={Id} name={Name} url={Url} reachable={Reachable}",
            serviceId, payload.Name, url, reachable);

        return Results.Created($"/repack-services/{serviceId}", new
        {
            registered  = true,
            serviceId,
            reachable,
            message = reachable
                ? "Registered and connectivity probe passed. Service is now active."
                : "Registered, but the connectivity probe failed. Verify the service URL is publicly reachable.",
        });
    }

    // -- Helpers ---------------------------------------------------------------

    private static async Task<bool> ProbeServiceAsync(
        string url, IHttpClientFactory factory, CancellationToken ct)
    {
        try
        {
            var http     = factory.CreateClient("repack");
            var response = await http.GetAsync($"{url}/api/info", ct);
            if (!response.IsSuccessStatusCode) return false;

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.TryGetProperty("status", out var s)
                && s.GetString() == "ok";
        }
        catch { return false; }
    }

    private static int GetAdminUserId(HttpContext ctx)
    {
        var claim = ctx.User.FindFirst("sub") ?? ctx.User.FindFirst("id");
        return int.TryParse(claim?.Value, out var id) ? id : 0;
    }
}

using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trion.Core.Abstractions.Auth;
using Trion.Core.Abstractions.Settings;
using Trion.Core.Constants;
using Trion.Core.Logging;
using Trion.Core.Setup;

namespace Trion.Core.Auth;

public sealed class EmulatorAuthService : IEmulatorAuthService
{
    private const string LockoutKeyPrefix = "lockout:";
    private const string FailCountPrefix  = "failcount:";

    private static readonly HttpClient _phoenixHttp =
        new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly ISettingsRepository                  _settings;
    private readonly AuditLogger                          _audit;
    private readonly IOptionsMonitor<EmulatorAuthOptions> _opts;
    private readonly ILogger                              _log;

    public EmulatorAuthService(
        ISettingsRepository                  settings,
        AuditLogger                          audit,
        IOptionsMonitor<EmulatorAuthOptions> opts,
        TrionLogger                          trionLogger)
    {
        _settings = settings;
        _audit    = audit;
        _opts     = opts;
        _log      = trionLogger.CreateLogger(nameof(EmulatorAuthService));
    }

    public async Task<AuthResult> AuthenticateAsync(
        string username,
        string password,
        string ip = "",
        CancellationToken ct = default)
    {
        var lower = username.ToLowerInvariant();

        // -- Lockout check -----------------------------------------------------
        var lockoutKey    = LockoutKeyPrefix + lower;
        var lockoutExpiry = await _settings.GetAsync<DateTimeOffset?>(lockoutKey, ct);
        if (lockoutExpiry.HasValue && lockoutExpiry.Value > DateTimeOffset.UtcNow)
        {
            var remaining = lockoutExpiry.Value - DateTimeOffset.UtcNow;
            RecordFailure(username, "locked", ip);
            return AuthResult.Fail($"Account locked. Try again in {(int)remaining.TotalMinutes + 1} minute(s).");
        }

        // -- Owner account check -----------------------------------------------
        var ownerUsername = await _settings.GetAsync<string>("bootstrap_username", ct);
        if (ownerUsername is null ||
            !ownerUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
        {
            return await Fail(lower, username, ip, "Invalid username or password.", ct);
        }

        var accountType = await _settings.GetAsync<string>(SetupKeys.AccountType, ct);

        if (accountType == "phoenix_api")
        {
            // Live verification against the Flying-Phoenix API
            var (apiOk, apiErr, apiKey, apiTier) = await VerifyPhoenixApiAsync(username, password, ct);
            if (apiOk)
            {
                // Persist API key + tier so the Plugin Store can use them
                if (!string.IsNullOrEmpty(apiKey))
                {
                    await _settings.SetAsync(SetupKeys.PhoenixApiKey,  apiKey!,  ct);
                    await _settings.SetAsync(SetupKeys.PhoenixApiTier, apiTier!, ct);
                }

                await ClearFailCount(lower, ct);
                _audit.Record(new AuditEvent(
                    Timestamp: DateTimeOffset.UtcNow,
                    Username:  username,
                    IpAddress: ip.Length > 0 ? ip : "internal",
                    Action:    AuditAction.LoginSuccess,
                    Result:    "phoenix_api",
                    Detail:    null));
                return AuthResult.Ok(username, gmLevel: 3);
            }
            return await Fail(lower, username, ip, apiErr ?? "Phoenix API verification failed.", ct);

        }
        else
        {
            // Local account -- BCrypt hash comparison
            if (await BootstrapAccount.ValidateAsync(_settings, username, password, ct))
            {
                await ClearFailCount(lower, ct);
                _audit.Record(new AuditEvent(
                    Timestamp: DateTimeOffset.UtcNow,
                    Username:  username,
                    IpAddress: ip.Length > 0 ? ip : "internal",
                    Action:    AuditAction.LoginSuccess,
                    Result:    "bootstrap",
                    Detail:    null));
                return AuthResult.Ok(username, gmLevel: 3);
            }
            return await Fail(lower, username, ip, "Invalid username or password.", ct);
        }
    }

    // -- Failure tracking ------------------------------------------------------

    private async Task<AuthResult> Fail(
        string lower, string username, string ip, string reason, CancellationToken ct)
    {
        RecordFailure(username, reason, ip);

        var failKey   = FailCountPrefix + lower;
        var failCount = (await _settings.GetAsync<int>(failKey, ct)) + 1;
        await _settings.SetAsync(failKey, failCount, ct);

        var opts = _opts.CurrentValue;
        if (failCount >= opts.MaxFailedAttempts)
        {
            var expiry = DateTimeOffset.UtcNow.Add(opts.LockoutDuration);
            await _settings.SetAsync(LockoutKeyPrefix + lower, expiry, ct);
            await _settings.DeleteAsync(failKey, ct);
            return AuthResult.Fail(
                $"Too many failed attempts. Account locked for {(int)opts.LockoutDuration.TotalMinutes} minute(s).");
        }

        return AuthResult.Fail(reason);
    }

    private async Task ClearFailCount(string lower, CancellationToken ct)
    {
        await _settings.DeleteAsync(FailCountPrefix + lower, ct);
        await _settings.DeleteAsync(LockoutKeyPrefix + lower, ct);
    }

    private void RecordFailure(string username, string reason, string ip)
    {
        _audit.Record(new AuditEvent(
            Timestamp: DateTimeOffset.UtcNow,
            Username:  username,
            IpAddress: ip.Length > 0 ? ip : "internal",
            Action:    AuditAction.LoginFailure,
            Result:    "failure",
            Detail:    reason));
    }

    // -- Phoenix API verification ----------------------------------------------

    private async Task<(bool Ok, string? Error, string? ApiKey, string? ApiTier)> VerifyPhoenixApiAsync(
        string username, string password, CancellationToken ct)
    {
        try
        {
            var url  = ApiEndpoints.AccountLoginUrl(ExternalLinks.ApiDefaultHost);
            var resp = await _phoenixHttp.PostAsJsonAsync(
                url, new { username, password }, ct);

            if (!resp.IsSuccessStatusCode)
                return (false, $"Phoenix API returned {(int)resp.StatusCode}.", null, null);

            var body = await resp.Content.ReadFromJsonAsync<PhoenixLoginResponse>(ct);
            if (body is null || string.IsNullOrEmpty(body.Token))
                return (false, "Invalid response from the Phoenix API.", null, null);

            if (body.IsBanned)
                return (false, "This account is banned.", null, null);

            return (true, null, body.ApiKey, body.ApiTier);
        }
        catch (HttpRequestException)
        {
            return (false, "Cannot reach the Trion/Phoenix server. Check your internet connection.", null, null);
        }
        catch (TaskCanceledException)
        {
            return (false, "Request timed out.", null, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected error during Phoenix API verification for {Username}.", username);
            return (false, "Authentication service unavailable.", null, null);
        }
    }

    private sealed record PhoenixLoginResponse(
        [property: JsonPropertyName("token")]     string  Token,
        [property: JsonPropertyName("is_banned")] bool    IsBanned,
        [property: JsonPropertyName("api_key")]   string? ApiKey,
        [property: JsonPropertyName("api_tier")]  string? ApiTier
    );
}

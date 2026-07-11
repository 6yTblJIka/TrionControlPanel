using Microsoft.Extensions.Caching.Memory;
using Trion.Core.Abstractions.Settings;

namespace Trion.Core.Auth;

/// <summary>
/// Maps panel accounts to <see cref="TrionRole"/> with a 5-minute memory-cache TTL.
/// The web panel only supports the owner account -- the owner always gets <see cref="TrionRole.Owner"/>.
/// </summary>
public sealed class RoleMappingService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ISettingsRepository _settings;
    private readonly IMemoryCache        _cache;

    public RoleMappingService(ISettingsRepository settings, IMemoryCache cache)
    {
        _settings = settings;
        _cache    = cache;
    }

    /// <summary>
    /// Returns the <see cref="TrionRole"/> for a user.
    /// Owner account (bootstrap_username) always gets <see cref="TrionRole.Owner"/>.
    /// </summary>
    public async Task<TrionRole> GetRoleAsync(string username, CancellationToken ct = default)
    {
        var lower    = username.ToLowerInvariant();
        var cacheKey = $"role:{lower}";

        if (_cache.TryGetValue(cacheKey, out TrionRole cached))
            return cached;

        var ownerUsername = await _settings.GetAsync<string>("bootstrap_username", ct);
        var role = ownerUsername is not null &&
                   ownerUsername.Equals(username, StringComparison.OrdinalIgnoreCase)
            ? TrionRole.Owner
            : TrionRole.None;

        _cache.Set(cacheKey, role, CacheTtl);
        return role;
    }

    /// <summary>Removes the cached role so the next call performs a fresh lookup.</summary>
    public void InvalidateCache(string username)
        => _cache.Remove($"role:{username.ToLowerInvariant()}");

    /// <summary>
    /// Flags the user for forced re-authentication on next request.
    /// </summary>
    public async Task ForceReauthAsync(string username, CancellationToken ct = default)
        => await _settings.SetAsync(
            $"force_reauth:{username.ToLowerInvariant()}",
            DateTimeOffset.UtcNow,
            ct);
}

using System.Net.Http;
using System.Net.Http.Json;
using Trion.Core.Abstractions.Settings;
using Trion.Core.Constants;
using Trion.Core.Plugins;
using Trion.Core.Setup;

namespace Trion.Web.Services;

public sealed class PluginStoreService
{
    private readonly ISettingsRepository _settings;
    private readonly HttpClient          _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public PluginStoreService(ISettingsRepository settings) => _settings = settings;

    public async Task<(IReadOnlyList<PluginStoreEntry> Plugins, string Tier, string? Error)>
        GetAvailablePluginsAsync(CancellationToken ct = default)
    {
        var apiKey = await _settings.GetAsync<string>(SetupKeys.PhoenixApiKey, ct);
        if (string.IsNullOrEmpty(apiKey))
            return ([], "free", "No Phoenix API key found. Log in to your account to access the Plugin Store.");

        using var req = new HttpRequestMessage(
            HttpMethod.Get, $"{ExternalLinks.ApiDefaultHost}/Trion/plugins");
        req.Headers.Add("X-API-Key", apiKey);

        try
        {
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return ([], "free", $"Could not reach plugin store ({(int)resp.StatusCode}).");

            var body = await resp.Content.ReadFromJsonAsync<PluginStoreResponse>(ct);
            return body is null
                ? ([], "free", "Invalid response from plugin store.")
                : (body.Plugins, body.Tier, null);
        }
        catch (Exception ex)
        {
            return ([], "free", $"Network error: {ex.Message}");
        }
    }

    public async Task<string?> GetApiKeyAsync(CancellationToken ct = default) =>
        await _settings.GetAsync<string>(SetupKeys.PhoenixApiKey, ct);

    public async Task<string> GetTierAsync(CancellationToken ct = default) =>
        await _settings.GetAsync<string>(SetupKeys.PhoenixApiTier, ct) ?? "free";
}

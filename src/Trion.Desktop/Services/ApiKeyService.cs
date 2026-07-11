using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Trion.Desktop.Infrastructure.Constants;
using Trion.Desktop.Models;

namespace Trion.Desktop.Services;

public sealed class ApiKeyService : IApiKeyService
{
    private readonly ISettingsService _settings;
    private readonly HttpClient       _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public string? GuestKey   { get; private set; }
    public string? UserApiKey { get; set; }
    public string  ActiveKey  => UserApiKey ?? GuestKey ?? string.Empty;

    public ApiKeyService(ISettingsService settings)
    {
        _settings = settings;
    }

    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        var cfg = _settings.Current;

        // Ensure a stable install UUID exists
        if (string.IsNullOrEmpty(cfg.InstallUuid))
        {
            cfg.InstallUuid = Guid.NewGuid().ToString();
            _settings.Save();
        }

        // Clear any previously-persisted guest key -- it is now memory-only.
        if (!string.IsNullOrEmpty(cfg.GuestKey))
        {
            cfg.GuestKey = "";
            _settings.Save();
        }

        // Always fetch a fresh guest key on every startup (never written to disk).
        try
        {
            var url     = $"{AppLinks.ApiBaseUrl}{ApiEndpoints.GuestKey}";
            var payload = new { uuid = cfg.InstallUuid, app_version = GetAppVersion() };
            var resp    = await _http.PostAsJsonAsync(url, payload, ct);

            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadFromJsonAsync<GuestKeyResponse>(cancellationToken: ct);
                if (body is not null && !string.IsNullOrEmpty(body.Key))
                    GuestKey = body.Key; // in-memory only -- never saved to settings
            }
        }
        catch
        {
            // Guest key is optional -- app still works without one
        }

        // Device ping -- only when user opted in (Settings → Share hardware info)
        if (cfg.HardwareInfoOptIn)
        {
            try
            {
                var specs = HardwareInfoService.Collect(GetAppVersion());
                var url   = $"{AppLinks.ApiBaseUrl}{ApiEndpoints.DevicePing}";

                // DeviceController.php reads $_POST['UUID'] and $_POST['Specs'] (uppercase, form-encoded)
                var form = new FormUrlEncodedContent([
                    new KeyValuePair<string, string>("UUID",  cfg.InstallUuid),
                    new KeyValuePair<string, string>("Specs", specs),
                ]);
                await _http.PostAsync(url, form, ct);
            }
            catch
            {
                // Telemetry is fire-and-forget -- never block startup
            }
        }
    }

    private static string GetAppVersion()
        => System.Reflection.Assembly.GetExecutingAssembly()
               .GetName().Version?.ToString(4) ?? "unknown";

    private sealed record GuestKeyResponse(
        [property: JsonPropertyName("guest_key")] string Key);
}

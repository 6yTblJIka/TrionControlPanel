using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Trion.Core.Abstractions.Settings;
using Trion.Core.Constants;

namespace Trion.Core.Plugins;

public sealed class PluginInstaller
{
    private readonly string               _pluginsFolder;
    private readonly ISettingsRepository  _settings;
    private readonly ILogger?             _logger;
    private readonly HttpClient           _http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public PluginInstaller(
        string              pluginsFolder,
        ISettingsRepository settings,
        ILogger?            logger = null)
    {
        _pluginsFolder = pluginsFolder;
        _settings      = settings;
        _logger        = logger;
    }

    public sealed record InstallResult(bool Success, string? Error = null, string? InstalledPath = null);

    /// <summary>Downloads, verifies, and writes the plugin DLL to the plugins folder.</summary>
    public async Task<InstallResult> InstallAsync(string slug, CancellationToken ct = default)
    {
        var apiKey = await _settings.GetAsync<string>("phoenix:api_key", ct);
        if (string.IsNullOrEmpty(apiKey))
            return new InstallResult(false,
                "No Phoenix API key found. Log in to your Phoenix account in Settings.");

        var url = $"{ExternalLinks.ApiDefaultHost}/Trion/plugins/{slug}/download";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-API-Key", apiKey);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, $"Network error: {ex.Message}");
        }

        if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            return new InstallResult(false, "Your account tier does not have access to this plugin.");

        if (!resp.IsSuccessStatusCode)
            return new InstallResult(false, $"Download failed ({(int)resp.StatusCode}).");

        // Signature is in the response header
        var sigBase64 = resp.Headers.TryGetValues("X-Plugin-Signature", out var vals)
            ? vals.FirstOrDefault() : null;

        if (string.IsNullOrEmpty(sigBase64))
            return new InstallResult(false, "Server did not return a plugin signature. Refusing to install.");

        var dllBytes = await resp.Content.ReadAsByteArrayAsync(ct);

        byte[] sigBytes;
        try   { sigBytes = Convert.FromBase64String(sigBase64); }
        catch { return new InstallResult(false, "Invalid signature format in server response."); }

        if (!PluginVerifier.Verify(dllBytes, sigBytes))
        {
            _logger?.LogWarning("RSA signature verification FAILED for plugin {Slug}", slug);
            return new InstallResult(false,
                "Plugin signature verification failed. The file may have been tampered with.");
        }

        if (!Directory.Exists(_pluginsFolder))
            Directory.CreateDirectory(_pluginsFolder);

        var filename = resp.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                    ?? $"{slug}.dll";
        // Sanitise -- only allow basename
        filename = Path.GetFileName(filename);
        if (!filename.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            filename = slug + ".dll";

        var dllPath = Path.Combine(_pluginsFolder, filename);
        var sigPath = dllPath + ".sig";

        await File.WriteAllBytesAsync(dllPath, dllBytes, ct);
        await File.WriteAllTextAsync(sigPath, sigBase64, ct);

        _logger?.LogInformation("Plugin {Slug} installed to {Path}", slug, dllPath);
        return new InstallResult(true, InstalledPath: dllPath);
    }

    /// <summary>Removes the DLL and .sig file for a plugin slug.</summary>
    public void Uninstall(string slug)
    {
        foreach (var dll in Directory.GetFiles(_pluginsFolder, "*.dll"))
        {
            if (Path.GetFileNameWithoutExtension(dll).Equals(slug, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(dll);
                var sig = dll + ".sig";
                if (File.Exists(sig)) File.Delete(sig);
                _logger?.LogInformation("Plugin {Slug} uninstalled.", slug);
                return;
            }
        }
    }

    /// <summary>Returns the slugs of plugins currently present in the plugins folder.</summary>
    public IReadOnlyList<string> InstalledSlugs()
    {
        if (!Directory.Exists(_pluginsFolder)) return [];
        return Directory.GetFiles(_pluginsFolder, "*.dll")
            .Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant())
            .ToList();
    }
}

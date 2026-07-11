using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using Trion.Desktop.Models;

namespace Trion.Desktop.Services;

/// <summary>Submits bug reports from the "Report a Bug" dialog to Phoenix.</summary>
public sealed class BugReportService : IBugReportService
{
    private readonly IApiKeyService _apiKeys;
    private readonly HttpClient     _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public BugReportService(IApiKeyService apiKeys)
    {
        _apiKeys = apiKeys;
    }

    public async Task<BugReportResult> SubmitAsync(
        string category,
        string? repackSlug,
        string description,
        string content,
        CancellationToken ct = default)
    {
        try
        {
            var payload = new
            {
                trion_version = GetAppVersion(),
                category,
                repack_slug   = string.IsNullOrWhiteSpace(repackSlug) ? null : repackSlug.Trim(),
                description   = description.Trim(),
                content       = content.Trim(),
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, AppLinks.BugReportUrl)
            {
                Content = JsonContent.Create(payload),
            };

            var key = _apiKeys.ActiveKey;
            if (!string.IsNullOrEmpty(key))
                req.Headers.TryAddWithoutValidation("X-API-Key", key);

            var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
                return new BugReportResult(true, null);

            var body = await resp.Content.ReadAsStringAsync(ct);
            return new BugReportResult(false, $"Server returned {(int)resp.StatusCode}: {body}");
        }
        catch (HttpRequestException ex)
        {
            return new BugReportResult(false, ex.Message);
        }
        catch (TaskCanceledException)
        {
            return new BugReportResult(false, "The request timed out.");
        }
    }

    private static string GetAppVersion()
        => Assembly.GetExecutingAssembly().GetName().Version?.ToString(4) ?? "unknown";
}

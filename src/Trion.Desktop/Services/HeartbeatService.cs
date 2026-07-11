using System.Net.Http;
using Microsoft.Extensions.Hosting;
using Trion.Desktop.Infrastructure.Constants;
using Trion.Desktop.Models;

namespace Trion.Desktop.Services;

/// <summary>
/// Sends a lightweight heartbeat ping to Phoenix every 90 seconds so the server
/// can count how many Trion Desktop instances are currently online.
/// Only the stable install UUID is sent  -  no personal data.
/// </summary>
public sealed class HeartbeatService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(90);

    private readonly ISettingsService _settings;
    private readonly HttpClient       _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public HeartbeatService(ISettingsService settings)
    {
        _settings = settings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Brief delay so startup completes before the first ping
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await PingAsync(stoppingToken);

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PingAsync(CancellationToken ct)
    {
        var uuid = _settings.Current.InstallUuid;
        if (string.IsNullOrEmpty(uuid)) return;

        try
        {
            var url  = $"{AppLinks.ApiBaseUrl}{ApiEndpoints.DevicePing}";
            var form = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("UUID", uuid),
            ]);
            await _http.PostAsync(url, form, ct);
        }
        catch
        {
            // Fire-and-forget  -  never surface heartbeat failures to the user
        }
    }
}

using System.Net.Http;
using System.Net.Http.Json;
using Trion.Desktop.Infrastructure.Constants;
using Trion.Desktop.Models;

namespace Trion.Desktop.Services;

public sealed class DonationGoalService : IDonationGoalService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<DonationGoal?> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{AppLinks.ApiBaseUrl}{ApiEndpoints.DonationGoal}";
            return await _http.GetFromJsonAsync<DonationGoal>(url, ct);
        }
        catch
        {
            return null;
        }
    }
}

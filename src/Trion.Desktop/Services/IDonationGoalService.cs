using Trion.Desktop.Models;

namespace Trion.Desktop.Services;

public interface IDonationGoalService
{
    Task<DonationGoal?> GetAsync(CancellationToken ct = default);
}

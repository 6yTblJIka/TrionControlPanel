using System.Text.Json.Serialization;

namespace Trion.Desktop.Models;

public sealed class DonationGoal
{
    [JsonPropertyName("currency")]
    public string Currency   { get; init; } = "EUR";

    [JsonPropertyName("goal")]
    public decimal Goal      { get; init; }

    [JsonPropertyName("raised")]
    public decimal Raised    { get; init; }

    [JsonPropertyName("supportUrl")]
    public string? SupportUrl { get; init; }

    /// <summary>True when the admin has configured a goal (goal > 0). Widgets should hide when false.</summary>
    public bool IsConfigured => Goal > 0;

    public double Percent => Goal > 0 ? Math.Min(100, (double) (Raised / Goal) * 100) : 0;
}

using System.Text.Json.Serialization;

namespace Trion.Desktop.Models;

/// <summary>
/// One entry from the <c>GET /Trion/packages</c> API response.
/// </summary>
public sealed class AvailablePackage
{
    [JsonPropertyName("emulator")]  public string Emulator  { get; init; } = "";
    [JsonPropertyName("name")]      public string Name      { get; init; } = "";
    [JsonPropertyName("shortName")] public string ShortName { get; init; } = "";
    [JsonPropertyName("available")] public bool   Available { get; init; }
}

/// <summary>
/// Full response from <c>GET /Trion/packages</c>.
/// </summary>
public sealed class AvailablePackagesResponse
{
    [JsonPropertyName("tier")]        public string                 Tier        { get; init; } = "guest";
    [JsonPropertyName("earlyAccess")] public bool                   EarlyAccess { get; init; }
    [JsonPropertyName("packages")]    public List<AvailablePackage> Packages    { get; init; } = [];
}

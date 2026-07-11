using System.Text.Json.Serialization;

namespace Trion.Core.Plugins;

public sealed record PluginStoreEntry(
    [property: JsonPropertyName("slug")]        string  Slug,
    [property: JsonPropertyName("name")]        string  Name,
    [property: JsonPropertyName("version")]     string  Version,
    [property: JsonPropertyName("description")] string  Description,
    [property: JsonPropertyName("author")]      string  Author,
    [property: JsonPropertyName("icon_url")]    string? IconUrl,
    [property: JsonPropertyName("min_tier")]    string  MinTier,
    [property: JsonPropertyName("file_size")]   long    FileSize,
    [property: JsonPropertyName("created_at")]  string  CreatedAt
);

public sealed record PluginStoreResponse(
    [property: JsonPropertyName("tier")]    string                   Tier,
    [property: JsonPropertyName("plugins")] List<PluginStoreEntry>   Plugins
);

using System.Text.Json.Serialization;

namespace Trion.Desktop.Models;

/// <summary>
/// One catalog entry returned by GET /Trion/repack/catalog (proxied from RepackService /api/repacks).
/// </summary>
public sealed class RepackCatalogEntry
{
    [JsonPropertyName("slug")]             public string Slug             { get; init; } = "";
    [JsonPropertyName("name")]             public string Name             { get; init; } = "";
    [JsonPropertyName("emulatorId")]       public string EmulatorId       { get; init; } = "";
    [JsonPropertyName("emulatorName")]     public string EmulatorName     { get; init; } = "";
    [JsonPropertyName("expansion")]        public string Expansion        { get; init; } = "";
    [JsonPropertyName("accessType")]       public string AccessType       { get; init; } = "free";
    [JsonPropertyName("branch")]           public string Branch           { get; init; } = "master";
    [JsonPropertyName("serverExecutable")] public string  ServerExecutable { get; init; } = "worldserver.exe";
    [JsonPropertyName("authExecutable")]   public string  AuthExecutable   { get; init; } = "authserver.exe";
    [JsonPropertyName("version")]          public string  Version          { get; init; } = "";
    /// <summary>Direct download URL of the latest successful build, or null when no build exists yet.</summary>
    [JsonPropertyName("downloadUrl")]      public string? DownloadUrl      { get; init; }
    /// <summary>Total number of times this repack has been downloaded through Phoenix.</summary>
    [JsonPropertyName("download_count")]   public int     DownloadCount    { get; init; }
}

using System.IO;
using System.Text.Json.Serialization;

namespace Trion.Desktop.Models;

/// <summary>
/// One entry from <c>GET /Trion/catalog</c>.
/// Provides everything Trion Desktop needs to install, launch, and configure
/// an emulator package: executables, config paths, DB import behaviour, etc.
/// </summary>
public sealed class PackageCatalogEntry
{
    [JsonPropertyName("emulator")]         public string Emulator         { get; init; } = "";
    [JsonPropertyName("displayName")]      public string DisplayName      { get; init; } = "";
    [JsonPropertyName("description")]      public string Description      { get; init; } = "";

    /// <summary>Engine type: "azerothcore" | "cmangos" | "trinitycore" | "spp" | "other"</summary>
    [JsonPropertyName("baseType")]         public string BaseType         { get; init; } = "spp";

    /// <summary>WoW expansion key: "classic" | "tbc" | "wotlk" | "cata" | "mop" | "legion"</summary>
    [JsonPropertyName("expansion")]        public string Expansion        { get; init; } = "";

    [JsonPropertyName("version")]          public string Version          { get; init; } = "Unknown";
    [JsonPropertyName("available")]        public bool   Available        { get; init; }

    /// <summary>Minimum tier required: "guest" | "free" | "support" | "guardian" | "champion" | "legend"</summary>
    [JsonPropertyName("minTier")]          public string MinTier          { get; init; } = "guest";

    /// <summary>
    /// When true, the world server imports its own SQL database on first startup (AzerothCore).
    /// When false, Trion must import DB dumps before the server can start (CMaNGOS, TrinityCore).
    /// </summary>
    [JsonPropertyName("autoDbImport")]     public bool   AutoDbImport     { get; init; }

    /// <summary>Relative path to the world-server executable inside the extracted package folder.</summary>
    [JsonPropertyName("serverExecutable")] public string ServerExecutable { get; init; } = "worldserver.exe";

    /// <summary>Relative path to the auth/logon-server executable inside the extracted package folder.</summary>
    [JsonPropertyName("authExecutable")]   public string AuthExecutable   { get; init; } = "authserver.exe";

    /// <summary>Relative path to the world-server configuration file.</summary>
    [JsonPropertyName("worldConf")]        public string WorldConf        { get; init; } = "worldserver.conf";

    /// <summary>Relative path to the auth-server configuration file.</summary>
    [JsonPropertyName("authConf")]         public string AuthConf         { get; init; } = "authserver.conf";

    /// <summary>Slug of the matching RepackService build definition (null if not linked).</summary>
    [JsonPropertyName("repackSlug")]       public string? RepackSlug      { get; init; }

    /// <summary>
    /// Logical databases this emulator requires: "auth", "world", "characters", and optionally "hotfix".
    /// Trion Desktop uses this to know which databases to create during installation.
    /// </summary>
    [JsonPropertyName("requiredDatabases")] public string[] RequiredDatabases { get; init; } = [];

    // -- Convenience helpers ---------------------------------------------------

    /// <summary>World-server exe name without extension (e.g. "worldserver" or "mangosd").</summary>
    public string ServerExeName =>
        Path.GetFileNameWithoutExtension(ServerExecutable);

    /// <summary>Auth-server exe name without extension (e.g. "authserver", "realmd", "bnetserver").</summary>
    public string AuthExeName =>
        Path.GetFileNameWithoutExtension(AuthExecutable);

    /// <summary>
    /// Resolve the full path to the world-server executable given the package install directory.
    /// </summary>
    public string ResolveServerExePath(string installDir) =>
        Path.GetFullPath(Path.Combine(installDir, ServerExecutable));

    /// <summary>
    /// Resolve the full path to the auth-server executable given the package install directory.
    /// </summary>
    public string ResolveAuthExePath(string installDir) =>
        Path.GetFullPath(Path.Combine(installDir, AuthExecutable));
}

/// <summary>
/// Full response from <c>GET /Trion/catalog</c>.
/// </summary>
public sealed class PackageCatalogResponse
{
    [JsonPropertyName("tier")]        public string                    Tier        { get; init; } = "guest";
    [JsonPropertyName("earlyAccess")] public bool                      EarlyAccess { get; init; }
    [JsonPropertyName("catalog")]     public List<PackageCatalogEntry> Catalog     { get; init; } = [];
}

using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trion.Desktop.Models;

/// <summary>
/// Deserialized from <c>repack-manifest.json</c> bundled inside each repack ZIP.
/// Written by RepackService/PackageStep -- Trion Desktop reads it to auto-create
/// the required MySQL databases without any SQL files or batch scripts.
/// </summary>
public sealed class RepackManifest
{
    [JsonPropertyName("emulatorId")]       public string  EmulatorId       { get; init; } = "";
    [JsonPropertyName("baseType")]         public string  BaseType         { get; init; } = "";
    [JsonPropertyName("expansion")]        public string  Expansion        { get; init; } = "";
    [JsonPropertyName("realmName")]        public string  RealmName        { get; init; } = "";

    [JsonPropertyName("dbHost")]           public string  DbHost           { get; init; } = "127.0.0.1";
    [JsonPropertyName("dbPort")]           public string  DbPort           { get; init; } = "3306";
    [JsonPropertyName("dbUser")]           public string  DbUser           { get; init; } = "";
    [JsonPropertyName("dbPass")]           public string  DbPass           { get; init; } = "";

    [JsonPropertyName("authDatabase")]     public string  AuthDatabase     { get; init; } = "";
    [JsonPropertyName("worldDatabase")]    public string  WorldDatabase    { get; init; } = "";
    [JsonPropertyName("charDatabase")]     public string  CharDatabase     { get; init; } = "";

    /// <summary>Null when the emulator does not require a hotfix database (e.g. AzerothCore, CMaNGOS).</summary>
    [JsonPropertyName("hotfixDatabase")]   public string? HotfixDatabase   { get; init; }

    /// <summary>
    /// Standalone databases requested by included modules, beyond the core
    /// auth/world/characters/hotfix set (e.g. a module with its own schema).
    /// Empty when no included module needs one.
    /// </summary>
    [JsonPropertyName("extraDatabases")]   public List<string> ExtraDatabases { get; init; } = [];

    [JsonPropertyName("serverExecutable")] public string  ServerExecutable { get; init; } = "worldserver.exe";
    [JsonPropertyName("authExecutable")]   public string  AuthExecutable   { get; init; } = "authserver.exe";
    [JsonPropertyName("worldConf")]        public string  WorldConf        { get; init; } = "worldserver.conf";
    [JsonPropertyName("authConf")]         public string  AuthConf         { get; init; } = "authserver.conf";

    [JsonPropertyName("builtAt")]          public string  BuiltAt          { get; init; } = "";

    // -- Convenience ----------------------------------------------------------

    /// <summary>
    /// Loads and deserializes <c>repack-manifest.json</c> from <paramref name="installPath"/>.
    /// Returns <c>null</c> when the file is absent or cannot be parsed.
    /// </summary>
    public static RepackManifest? Load(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return null;
        var path = Path.Combine(installPath, "repack-manifest.json");
        if (!File.Exists(path)) return null;
        try   { return JsonSerializer.Deserialize<RepackManifest>(File.ReadAllText(path)); }
        catch { return null; }
    }

    /// <summary>All database names this emulator requires, in creation order (deduplicated).</summary>
    public IEnumerable<string> AllDatabases()
    {
        IEnumerable<string> All()
        {
            if (!string.IsNullOrWhiteSpace(AuthDatabase))   yield return AuthDatabase;
            if (!string.IsNullOrWhiteSpace(WorldDatabase))  yield return WorldDatabase;
            if (!string.IsNullOrWhiteSpace(CharDatabase))   yield return CharDatabase;
            if (!string.IsNullOrWhiteSpace(HotfixDatabase)) yield return HotfixDatabase!;

            foreach (var db in ExtraDatabases)
                if (!string.IsNullOrWhiteSpace(db))
                    yield return db;
        }

        return All().Distinct(StringComparer.OrdinalIgnoreCase);
    }
}

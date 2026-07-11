using System.IO;
using Trion.Core.Services.Emulator;

namespace Trion.Desktop.Services;

public sealed class GameClientService(ISettingsService settings) : IGameClientService
{
    private static readonly string AppDataBase =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData");

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["classic"] = "Classic (Vanilla)",
        ["tbc"]     = "The Burning Crusade",
        ["wotlk"]   = "Wrath of the Lich King",
        ["cata"]    = "Cataclysm",
        ["mop"]     = "Mists of Pandaria",
    };

    private static readonly Dictionary<string, string> Versions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["classic"] = "1.12.1",
        ["tbc"]     = "2.4.3",
        ["wotlk"]   = "3.3.5a",
        ["cata"]    = "4.3.4",
        ["mop"]     = "5.4.8",
    };

    public bool IsClientConfigured(string expansion) =>
        settings.Current.GameClientPaths.TryGetValue(expansion, out var p)
        && !string.IsNullOrEmpty(p)
        && Directory.Exists(p);

    public bool IsDataExtracted(string expansion, string emulatorId = "") =>
        File.Exists(Path.Combine(GetDataPath(expansion, emulatorId), ClientDataExtractor.SentinelFileName));

    public string? GetClientPath(string expansion) =>
        settings.Current.GameClientPaths.TryGetValue(expansion, out var p) && !string.IsNullOrEmpty(p)
            ? p : null;

    public string GetDataPath(string expansion, string emulatorId = "")
    {
        // Custom per-expansion override from Settings takes full precedence.
        if (settings.Current.GameDataPaths.TryGetValue(expansion, out var custom) && !string.IsNullOrEmpty(custom))
            return custom;

        // Default: <AppDir>/GameData/<expansion>/<emulatorId>
        var basePath = Path.Combine(AppDataBase, expansion);
        return string.IsNullOrEmpty(emulatorId) ? basePath : Path.Combine(basePath, emulatorId);
    }

    public void Configure(string expansion, string clientPath, string? dataPath = null)
    {
        settings.Current.GameClientPaths[expansion] = clientPath;
        if (!string.IsNullOrEmpty(dataPath))
            settings.Current.GameDataPaths[expansion] = dataPath;
        settings.Save();
    }

    public void PatchDataDir(string repackInstallPath, string expansion, string emulatorId = "")
    {
        var conf = Directory.GetFiles(repackInstallPath, "worldserver.conf", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        if (conf is null) return;

        var dataPath = GetDataPath(expansion, emulatorId);
        var lines    = File.ReadAllLines(conf);
        bool patched = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("DataDir", StringComparison.OrdinalIgnoreCase) && trimmed.Contains('='))
            {
                lines[i] = $"DataDir = \"{dataPath}\"";
                patched   = true;
                break;
            }
        }

        if (patched) File.WriteAllLines(conf, lines);
    }

    public string GetExpansionDisplayName(string expansion) =>
        DisplayNames.TryGetValue(expansion, out var name) ? name : expansion.ToUpperInvariant();

    public string GetExpansionVersion(string expansion) =>
        Versions.TryGetValue(expansion, out var ver) ? ver : "";
}

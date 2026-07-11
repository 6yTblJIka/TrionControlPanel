namespace Trion.Desktop.Services;

public interface IGameClientService
{
    /// <summary>True when the user has pointed us at a WoW client for this expansion.</summary>
    bool IsClientConfigured(string expansion);

    /// <summary>True when the extraction sentinel exists in the shared output directory.</summary>
    bool IsDataExtracted(string expansion, string emulatorId = "");

    string? GetClientPath(string expansion);

    /// <summary>Returns the shared output directory: GameData/{expansion}/{emulatorId} (or custom path from settings).</summary>
    string GetDataPath(string expansion, string emulatorId = "");

    void Configure(string expansion, string clientPath, string? dataPath = null);

    /// <summary>Patches the DataDir setting in the repack's worldserver.conf to point at the shared data path.</summary>
    void PatchDataDir(string repackInstallPath, string expansion, string emulatorId = "");

    string GetExpansionDisplayName(string expansion);
    string GetExpansionVersion(string expansion);
}

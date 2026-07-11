namespace Trion.Desktop.Models;

/// <summary>
/// Describes the emulator core that backs a specific catalog repack. Resolved from
/// repack-manifest.json (see <see cref="EmulatorCardViewModel"/>), filling in executable
/// names, database names, and the correct auth-DB schema so that the server control page
/// and database editor work without any manual configuration.
/// </summary>
public sealed record EmulatorProfile(
    /// <summary>World-server process name (without .exe), e.g. "mangosd" or "worldserver".</summary>
    string WorldExeName,
    /// <summary>Auth/logon process name (without .exe), e.g. "realmd", "authserver", "bnetserver".</summary>
    string LogonExeName,
    /// <summary>Default world database name.</summary>
    string DefaultWorldDb,
    /// <summary>Default auth/logon database name.</summary>
    string DefaultAuthDb,
    /// <summary>Default characters database name.</summary>
    string DefaultCharactersDb,
    /// <summary>
    /// Auth-DB schema index that matches <see cref="AppSettings.SelectedDatabases"/>:
    ///   0 = TrinityCore 3.3.5a  (sha_pass_hash, SHA1)
    ///   1 = AzerothCore          (binary salt+verifier, LegacySHA1 SRP6)
    ///   2 = CMaNGOS / VMaNGOS   (hex v/s columns, CMaNGOS SRP6)
    ///   3 = TrinityCore 4.x+    (battlenet_accounts, BNet SHA256)
    /// </summary>
    int DatabaseSchema
);

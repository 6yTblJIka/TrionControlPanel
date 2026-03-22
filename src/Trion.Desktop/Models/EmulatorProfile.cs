namespace Trion.Desktop.Models;

/// <summary>
/// Describes the emulator core that backs a specific SPP expansion.
/// Applied automatically to <see cref="AppSettings"/> when the user enables an expansion,
/// filling in executable names, database names, and the correct auth-DB schema so that
/// the server control page and database editor work without any manual configuration.
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

/// <summary>
/// Canonical emulator profiles for every SPP expansion.
/// Each profile drives:
///   - which executables to look for / launch
///   - which database names to default to
///   - which auth-DB schema the DatabaseService should use
/// </summary>
public static class EmulatorProfiles
{
    /// <summary>
    /// Classic SPP — CMaNGOS Classic.
    /// Executables: mangosd + realmd.
    /// Schema: CMaNGOS/VMaNGOS (hex v/s SRP6).
    /// </summary>
    public static readonly EmulatorProfile Classic = new(
        WorldExeName:        "mangosd",
        LogonExeName:        "realmd",
        DefaultWorldDb:      "classicmangos",
        DefaultAuthDb:       "realmd",
        DefaultCharactersDb: "characters",
        DatabaseSchema:      2
    );

    /// <summary>
    /// TBC SPP — CMaNGOS TBC.
    /// Executables: mangosd + realmd.
    /// Schema: CMaNGOS/VMaNGOS (hex v/s SRP6).
    /// </summary>
    public static readonly EmulatorProfile Tbc = new(
        WorldExeName:        "mangosd",
        LogonExeName:        "realmd",
        DefaultWorldDb:      "tbcmangos",
        DefaultAuthDb:       "realmd",
        DefaultCharactersDb: "characters",
        DatabaseSchema:      2
    );

    /// <summary>
    /// WotLK SPP — AzerothCore 3.3.5a.
    /// Executables: worldserver + authserver.
    /// Schema: AzerothCore (binary salt+verifier, LegacySHA1 SRP6).
    /// </summary>
    public static readonly EmulatorProfile Wotlk = new(
        WorldExeName:        "worldserver",
        LogonExeName:        "authserver",
        DefaultWorldDb:      "acore_world",
        DefaultAuthDb:       "acore_auth",
        DefaultCharactersDb: "acore_characters",
        DatabaseSchema:      1
    );

    /// <summary>
    /// Cataclysm SPP — TrinityCore 4.3.4.
    /// Executables: worldserver + authserver.
    /// Schema: TrinityCore 3.3.5a-style (sha_pass_hash, SHA1) — TC 4.3.4 still uses the classic auth table.
    /// </summary>
    public static readonly EmulatorProfile Cata = new(
        WorldExeName:        "worldserver",
        LogonExeName:        "authserver",
        DefaultWorldDb:      "world",
        DefaultAuthDb:       "auth",
        DefaultCharactersDb: "characters",
        DatabaseSchema:      0
    );

    /// <summary>
    /// MoP SPP — TrinityCore / CypherCore 5.4.x.
    /// Executables: worldserver + bnetserver.
    /// Schema: TrinityCore 4.x BattleNet (battlenet_accounts + account, BNet SHA256).
    /// </summary>
    public static readonly EmulatorProfile Mop = new(
        WorldExeName:        "worldserver",
        LogonExeName:        "bnetserver",
        DefaultWorldDb:      "world",
        DefaultAuthDb:       "auth",
        DefaultCharactersDb: "characters",
        DatabaseSchema:      3
    );
}

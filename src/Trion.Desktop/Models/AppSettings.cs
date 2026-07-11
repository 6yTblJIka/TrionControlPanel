using Trion.Desktop.Infrastructure.Constants;

namespace Trion.Desktop.Models;

/// <summary>
/// All application settings persisted to Settings.db (SQLite).
/// Loaded into memory on startup; flushed back on exit and on explicit user save.
/// Property names match the legacy JSON schema so old Settings.json files remain compatible.
/// </summary>
public class AppSettings
{
    // -- Trion News --------------------------------------------------------------

    /// <summary>Highest Trion News item ID the user has already seen (title-bar popup opened).
    /// Items with a higher ID than this are shown as "unread" via the badge.</summary>
    public int LastSeenNewsId { get; set; }

    // -- Web server (nginx + PHP) ----------------------------------------------

    /// <summary>True once nginx + PHP have been successfully installed.</summary>
    public bool   WebServerInstalled { get; set; }
    /// <summary>Root directory of the portable web server installation (webserver/ folder).</summary>
    public string WebServerLocation  { get; set; } = "";
    /// <summary>HTTP port nginx listens on. Defaults to 80.</summary>
    public int    WebServerPort      { get; set; } = 80;
    /// <summary>When true, nginx also listens on HttpsPort with TLS.</summary>
    public bool   EnableHttps        { get; set; }
    /// <summary>HTTPS port. Defaults to 443. Only used when EnableHttps is true.</summary>
    public int    HttpsPort          { get; set; } = 443;
    /// <summary>Path to the PEM certificate file. Empty = use auto-generated self-signed cert.</summary>
    public string SslCertPath        { get; set; } = "";
    /// <summary>Path to the PEM private key file. Empty = use auto-generated self-signed key.</summary>
    public string SslKeyPath         { get; set; } = "";
    /// <summary>
    /// When true, RunStartupCheckAsync is called at launch -- installs if absent, starts if installed.
    /// Opt-in (false by default) so users who don't need a web server aren't affected.
    /// </summary>
    public bool   WebServerAutoStart { get; set; }

    // -- Database server --------------------------------------------------------

    public string WorldDatabase      { get; set; } = "wotlk_world";
    public string AuthDatabase       { get; set; } = "wotlk_auth";
    public string CharactersDatabase { get; set; } = "wotlk_characters";
    public string HotfixDatabase     { get; set; } = "";
    public string DBExeLoc           { get; set; } = "";
    public string DBWorkingDir       { get; set; } = "";
    public string DBLocation         { get; set; } = "";
    public string DBServerHost       { get; set; } = "localhost";
    public string DBServerUser       { get; set; } = "phoenix";
    public string DBServerPassword   { get; set; } = "phoenix";
    public string DBServerPort       { get; set; } = "3306";
    public string DBExeName          { get; set; } = "mysqld";
    public bool   DBInstalled        { get; set; }
    /// <summary>MySQL admin username used to CREATE DATABASE during repack install. Default: root.</summary>
    public string MySqlAdminUser     { get; set; } = "root";
    /// <summary>MySQL admin password. Empty = no password (default for a fresh local MySQL install).</summary>
    public string MySqlAdminPassword { get; set; } = "";
    /// <summary>
    /// Which auth DB schema to use.
    /// 0 = TrinityCore 3.3.5a (sha_pass_hash, SHA1)
    /// 1 = AzerothCore         (binary salt + verifier, LegacySHA1 SRP6)   [default]
    /// 2 = CMaNGOS / VMaNGOS   (hex v/s columns, CMaNGOS SRP6)
    /// 3 = TrinityCore 4.x / Latest (battlenet_accounts + account, BNet SHA256 hash)
    /// </summary>
    public int    SelectedDatabases  { get; set; } = 1;   // default: AzerothCore

    // -- Custom Core -----------------------------------------------------------

    public string CustomWorkingDirectory { get; set; } = "";
    public string CustomWorldExeLoc      { get; set; } = "";
    public string CustomLogonExeLoc      { get; set; } = "";
    public string CustomLogonExeName     { get; set; } = "worldserver";
    public string CustomWorldExeName     { get; set; } = "authserver";
    public string CustomLogonName        { get; set; } = "Custom Core";
    public string CustomWorldName        { get; set; } = "Custom Core";
    public bool   CustomNames            { get; set; }
    public bool   LaunchCustomCore       { get; set; }
    public bool   CustomInstalled        { get; set; }

    // -- Dynamic repack state (catalog-driven, keyed by slug) -----------------

    /// <summary>Tracks which catalog repacks are currently installed, keyed by slug.</summary>
    public Dictionary<string, bool>   RepackInstalled    { get; set; } = [];
    /// <summary>Install directory for each catalog repack, keyed by slug. Empty = use default.</summary>
    public Dictionary<string, string> RepackInstallPaths { get; set; } = [];
    /// <summary>Which repack is selected/enabled for server control, keyed by slug.</summary>
    public Dictionary<string, bool>   RepackEnabled      { get; set; } = [];
    /// <summary>Installed version string per repack slug.</summary>
    public Dictionary<string, string> RepackVersion      { get; set; } = [];
    /// <summary>Full path to the world-server exe per repack slug.</summary>
    public Dictionary<string, string> RepackWorldExePath { get; set; } = [];
    /// <summary>Full path to the auth-server exe per repack slug.</summary>
    public Dictionary<string, string> RepackAuthExePath  { get; set; } = [];
    /// <summary>World-server exe filename per repack slug (e.g. "worldserver.exe").</summary>
    public Dictionary<string, string> RepackWorldExeName { get; set; } = [];
    /// <summary>Auth-server exe filename per repack slug (e.g. "authserver.exe").</summary>
    public Dictionary<string, string> RepackAuthExeName  { get; set; } = [];

    // -- Game client data (shared per expansion) --------------------------------

    /// <summary>WoW client folder per expansion slug (e.g. "wotlk" -> "C:\Games\WoW 3.3.5"). Set once, shared across all repacks of that expansion.</summary>
    public Dictionary<string, string> GameClientPaths { get; set; } = [];
    /// <summary>Extracted data output dir per expansion slug. Empty = %APPDATA%\Trion\GameData\{expansion}.</summary>
    public Dictionary<string, string> GameDataPaths   { get; set; } = [];

    // -- Active selection ------------------------------------------------------

    /// <summary>Which expansion tab is active in Single Player. 0=Custom 1=Classic 2=TBC 3=WotLK 4=Cata 5=MoP</summary>
    public int SelectedCore { get; set; }
    /// <summary>Which SPP emulator build is selected. Maps to the SPP enum.</summary>
    public int SelectedSPP  { get; set; } = 3;   // 3 = WotLK

    // -- DDNS ------------------------------------------------------------------

    public string DDNSDomain       { get; set; } = "";
    public string DDNSUsername     { get; set; } = "";
    public string DDNSPassword     { get; set; } = "";
    public int    DDNSInterval     { get; set; } = 300;  // seconds
    public string IPAddress        { get; set; } = "";
    public bool   DDNSRunOnStartup { get; set; }
    /// <summary>DDNS provider. 0=None 1=NoIP 2=DuckDNS 3=Cloudflare</summary>
    public int    DDNSService      { get; set; }
    /// <summary>DDNS provider name as displayed in the WPF UI (e.g. "DuckDNS", "Cloudflare").</summary>
    public string DDNSServiceName  { get; set; } = "DuckDNS";

    // -- API connection --------------------------------------------------------

    /// <summary>
    /// Override for the Phoenix API base URL (e.g. "https://my-staging-server.dev").
    /// Empty string = use the built-in default (ExternalLinks.ApiDefaultHost).
    /// Set this in Settings to point at a staging instance or a local development server
    /// without recompiling.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "";

    // -- Trion preferences -----------------------------------------------------

    /// <summary>How often the system metrics sampler fires, in seconds (1-10). Default 2.</summary>
    public int    MetricsIntervalSeconds  { get; set; } = 2;
    /// <summary>Active theme: e.g. "TrionBlue Dark", "Green Light".</summary>
    public string Theme              { get; set; } = "TrionBlue Dark";
    /// <summary>Locale code: "en-US", "pl-PL".</summary>
    public string Language           { get; set; } = "en-US";
    // -- Account / Session -----------------------------------------------------

    public int    AccountId        { get; set; }
    public string AccountUsername  { get; set; } = "";
    public string AccountEmail     { get; set; } = "";
    public string AccountToken     { get; set; } = "";
    public string AccountRole      { get; set; } = "user";
    public string AccountApiKey    { get; set; } = "";
    public string AccountApiTier   { get; set; } = "free";
    public bool   AccountIsActive  { get; set; }
    public bool   AccountIsBanned  { get; set; }
    public string AccountLastLogin { get; set; } = "";
    public string AccountCreatedAt { get; set; } = "";
    /// <summary>True when the user explicitly chose "Continue as Guest" at the login prompt.</summary>
    public bool   LoginSkipped     { get; set; }
    /// <summary>When false the account token is wiped on exit so the user must sign in again next launch.</summary>
    public bool   RememberMe       { get; set; } = false;

    // -- Guest key system ------------------------------------------------------

    /// <summary>Stable per-installation UUID used to register a guest key. Generated once, never regenerated.</summary>
    public string InstallUuid { get; set; } = "";
    /// <summary>Cached guest API key returned by POST /Trion/guest-key. Empty until first successful registration.</summary>
    public string GuestKey    { get; set; } = "";
    public bool   AutoUpdateCore     { get; set; } = true;
    public bool   AutoUpdateTrion    { get; set; } = true;
    public bool   AutoUpdateDatabase { get; set; } = true;
    public bool   NotificationSound  { get; set; }
    public bool   ConsoleHide        { get; set; } = true;
    public bool   StayInTray         { get; set; }
    public bool   RunWithWindows     { get; set; }
    public bool   RunServerWithWindows  { get; set; }
    public bool   ServerCrashDetection  { get; set; }
    public bool   FirstRun           { get; set; } = true;
    public bool   CreateBnetAccount  { get; set; }
    /// <summary>Opt-in: send OS, CPU count, RAM, and app version to Phoenix on startup.</summary>
    public bool   HardwareInfoOptIn  { get; set; } = false;

    // -- Window state (WPF only) -----------------------------------------------

    public double WindowLeft   { get; set; } = double.NaN;
    public double WindowTop    { get; set; } = double.NaN;
    public double WindowWidth  { get; set; } = 1180;
    public double WindowHeight { get; set; } = 740;
}

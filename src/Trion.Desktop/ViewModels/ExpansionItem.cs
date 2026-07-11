using CommunityToolkit.Mvvm.ComponentModel;

namespace Trion.Desktop.ViewModels;

/// <summary>
/// Represents a single repack row in the Installer view.
/// Populated dynamically from the RepackService catalog.
/// </summary>
public partial class ExpansionItem : ObservableObject
{
    /// <summary>User-visible name from the catalog, e.g. "WotLK AzerothCore".</summary>
    public string DisplayName { get; }

    /// <summary>Repack slug used as the API identifier, e.g. "wotlk", "azerothcore-supporter".</summary>
    public string ApiName { get; }

    /// <summary>Required access tier from the catalog: "free" | "support" | "guardian" | "champion" | "legend".</summary>
    public string AccessType { get; }

    /// <summary>True when the user's tier is below the required access type for this repack.</summary>
    public bool IsLocked { get; }

    /// <summary>Server executable relative path within the install folder, e.g. "worldserver.exe".</summary>
    public string ServerExecutable { get; }

    /// <summary>Expansion slug from the catalog: "classic" | "tbc" | "wotlk" | "cata" | "mop".</summary>
    public string Expansion { get; }

    /// <summary>Formatted tier name shown in the lock badge, e.g. "Supporter+", "Legend+".</summary>
    public string RequiredTierLabel => AccessType.ToLowerInvariant() switch
    {
        "free"     => "Free+",
        "support"  => "Supporter+",
        "guardian" => "Guardian+",
        "champion" => "Champion+",
        "legend"   => "Legend",
        _          => AccessType,
    };

    /// <summary>Direct download URL from the latest RepackService build. Null means no build exists yet.</summary>
    public string? DownloadUrl { get; set; }

    /// <summary>Local folder where the emulator lives (or will be installed).</summary>
    public string InstallPath { get; set; } = "";

    /// <summary>Full path to the world-server exe -- used to read the installed version string.</summary>
    public string ExePath { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotIsInstalled))]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private string _localVersion  = "--";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private string _latestVersion = "--";

    /// <summary>True when an update is available (installed version differs from latest).</summary>
    public bool HasUpdate =>
        IsInstalled
        && LocalVersion  is not ("--" or "")
        && LatestVersion is not ("--" or "")
        && LocalVersion  != LatestVersion;

    /// <summary>Inverse of <see cref="IsInstalled"/> -- used by the Install button's Visibility binding.</summary>
    public bool NotIsInstalled => !IsInstalled;

    public ExpansionItem(string displayName, string apiName,
        string accessType = "free", bool isLocked = false,
        string serverExecutable = "worldserver.exe",
        string expansion = "")
    {
        DisplayName      = displayName;
        ApiName          = apiName;
        AccessType       = accessType;
        IsLocked         = isLocked;
        ServerExecutable = serverExecutable;
        Expansion        = expansion;
    }
}

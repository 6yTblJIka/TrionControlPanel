using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using Trion.Desktop.Models;
using Trion.Desktop.Services;

namespace Trion.Desktop.ViewModels;

public partial class SinglePlayerViewModel : ObservableObject
{
    private readonly ILocalizationService        _loc;
    private readonly ISettingsService            _settings;
    private readonly IEmulatorInstallerService   _installer;

    public ILocalizationService Loc => _loc;
    public ObservableCollection<EmulatorCardViewModel> Emulators { get; }

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _hasAnyAvailable = false;

    public SinglePlayerViewModel(ISettingsService settings, ILocalizationService loc,
                                 IEmulatorInstallerService installer,
                                 INotificationService notifications)
    {
        _loc       = loc;
        _settings  = settings;
        _installer = installer;

        Emulators = BuildCards(settings, installer, notifications);

        foreach (var card in Emulators)
            card.PropertyChanged += OnCardPropertyChanged;

        _ = LoadVersionsAsync();
    }

    // ── Mutual-exclusion: only one expansion active at a time ─────────────────

    private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EmulatorCardViewModel.IsEnabled)) return;
        if (sender is not EmulatorCardViewModel changed || !changed.IsEnabled) return;

        foreach (var card in Emulators)
        {
            if (card != changed)
                card.DisableExternally();
        }
    }

    // ── Version loading ───────────────────────────────────────────────────────

    private async Task LoadVersionsAsync()
    {
        // Run both requests concurrently
        var versionsTask  = _installer.FetchLatestVersionsAsync(CancellationToken.None);
        var packagesTask  = _installer.FetchAvailablePackagesAsync(CancellationToken.None);

        await Task.WhenAll(versionsTask, packagesTask);

        var versions  = versionsTask.Result;
        var available = packagesTask.Result;

        // Build a quick lookup: emulator name → available
        var lookup = available.Packages
            .ToDictionary(p => p.Emulator, p => p.Available, StringComparer.OrdinalIgnoreCase);

        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            // Index matches BuildCards order: 0=Classic,1=TBC,2=WotLK,3=Cata,4=MoP
            if (Emulators.Count >= 5)
            {
                Emulators[0].SetLatestVersion(versions.Classic);
                Emulators[1].SetLatestVersion(versions.Tbc);
                Emulators[2].SetLatestVersion(versions.Wotlk);
                Emulators[3].SetLatestVersion(versions.Cata);
                Emulators[4].SetLatestVersion(versions.Mop);

                Emulators[0].IsAvailable = lookup.GetValueOrDefault("classic", false);
                Emulators[1].IsAvailable = lookup.GetValueOrDefault("tbc",     false);
                Emulators[2].IsAvailable = lookup.GetValueOrDefault("wotlk",   false);
                Emulators[3].IsAvailable = lookup.GetValueOrDefault("cata",    false);
                Emulators[4].IsAvailable = lookup.GetValueOrDefault("mop",     false);

                HasAnyAvailable = Emulators.Any(e => e.IsAvailable);
                IsLoading       = false;
            }
        });
    }

    // ── Card factory ──────────────────────────────────────────────────────────

    private static Brush Hex(string hex) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    private static ObservableCollection<EmulatorCardViewModel> BuildCards(
        ISettingsService s, IEmulatorInstallerService installer, INotificationService n) =>
    [
        new(s, installer, n,
            name: "World of Warcraft",      shortName: "Classic", apiEmulatorName: "classic",
            accentBrush:     Hex("#795548"),
            profile:         EmulatorProfiles.Classic,
            getInstalled:    c => c.ClassicInstalled,          setInstalled:   (c, v) => c.ClassicInstalled        = v,
            getEnabled:      c => c.LaunchClassicCore,         setEnabled:     (c, v) => c.LaunchClassicCore       = v,
            getVersion:      c => c.ClassicInstalledVersion,   setVersion:     (c, v) => c.ClassicInstalledVersion = v,
            getInstallPath:  c => c.ClassicWorkingDirectory,   setInstallPath: (c, v) => c.ClassicWorkingDirectory = v,
            getExePath:      c => c.ClassicWorldExeLoc,
            getWorldExeName: c => c.ClassicWorldExeName,       setWorldExeName: (c, v) => c.ClassicWorldExeName    = v,
            getLogonExeName: c => c.ClassicLogonExeName,       setLogonExeName: (c, v) => c.ClassicLogonExeName    = v),

        new(s, installer, n,
            name: "The Burning Crusade",    shortName: "TBC",     apiEmulatorName: "tbc",
            accentBrush:     Hex("#1565C0"),
            profile:         EmulatorProfiles.Tbc,
            getInstalled:    c => c.TBCInstalled,              setInstalled:   (c, v) => c.TBCInstalled            = v,
            getEnabled:      c => c.LaunchTBCCore,             setEnabled:     (c, v) => c.LaunchTBCCore           = v,
            getVersion:      c => c.TBCInstalledVersion,       setVersion:     (c, v) => c.TBCInstalledVersion     = v,
            getInstallPath:  c => c.TBCWorkingDirectory,       setInstallPath: (c, v) => c.TBCWorkingDirectory     = v,
            getExePath:      c => c.TBCWorldExeLoc,
            getWorldExeName: c => c.TBCWorldExeName,           setWorldExeName: (c, v) => c.TBCWorldExeName        = v,
            getLogonExeName: c => c.TBCLogonExeName,           setLogonExeName: (c, v) => c.TBCLogonExeName        = v),

        new(s, installer, n,
            name: "Wrath of the Lich King", shortName: "WotLK",   apiEmulatorName: "wotlk",
            accentBrush:     Hex("#0277BD"),
            profile:         EmulatorProfiles.Wotlk,
            getInstalled:    c => c.WotLKInstalled,            setInstalled:   (c, v) => c.WotLKInstalled          = v,
            getEnabled:      c => c.LaunchWotLKCore,           setEnabled:     (c, v) => c.LaunchWotLKCore         = v,
            getVersion:      c => c.WotLKInstalledVersion,     setVersion:     (c, v) => c.WotLKInstalledVersion   = v,
            getInstallPath:  c => c.WotLKWorkingDirectory,     setInstallPath: (c, v) => c.WotLKWorkingDirectory   = v,
            getExePath:      c => c.WotLKWorldExeLoc,
            getWorldExeName: c => c.WotLKWorldExeName,         setWorldExeName: (c, v) => c.WotLKWorldExeName      = v,
            getLogonExeName: c => c.WotLKLogonExeName,         setLogonExeName: (c, v) => c.WotLKLogonExeName      = v),

        new(s, installer, n,
            name: "Cataclysm",              shortName: "Cata",    apiEmulatorName: "cata",
            accentBrush:     Hex("#BF360C"),
            profile:         EmulatorProfiles.Cata,
            getInstalled:    c => c.CataInstalled,             setInstalled:   (c, v) => c.CataInstalled           = v,
            getEnabled:      c => c.LaunchCataCore,            setEnabled:     (c, v) => c.LaunchCataCore          = v,
            getVersion:      c => c.CataInstalledVersion,      setVersion:     (c, v) => c.CataInstalledVersion    = v,
            getInstallPath:  c => c.CataWorkingDirectory,      setInstallPath: (c, v) => c.CataWorkingDirectory    = v,
            getExePath:      c => c.CataWorldExeLoc,
            getWorldExeName: c => c.CataWorldExeName,          setWorldExeName: (c, v) => c.CataWorldExeName       = v,
            getLogonExeName: c => c.CataLogonExeName,          setLogonExeName: (c, v) => c.CataLogonExeName       = v),

        new(s, installer, n,
            name: "Mists of Pandaria",      shortName: "MoP",     apiEmulatorName: "mop",
            accentBrush:     Hex("#2E7D32"),
            profile:         EmulatorProfiles.Mop,
            getInstalled:    c => c.MOPInstalled,              setInstalled:   (c, v) => c.MOPInstalled            = v,
            getEnabled:      c => c.LaunchMoPCore,             setEnabled:     (c, v) => c.LaunchMoPCore           = v,
            getVersion:      c => c.MopInstalledVersion,       setVersion:     (c, v) => c.MopInstalledVersion     = v,
            getInstallPath:  c => c.MopWorkingDirectory,       setInstallPath: (c, v) => c.MopWorkingDirectory     = v,
            getExePath:      c => c.MopWorldExeLoc,
            getWorldExeName: c => c.MopWorldExeName,           setWorldExeName: (c, v) => c.MopWorldExeName        = v,
            getLogonExeName: c => c.MopLogonExeName,           setLogonExeName: (c, v) => c.MopLogonExeName        = v),
    ];
}

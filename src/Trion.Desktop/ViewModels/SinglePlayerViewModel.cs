using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using Trion.Core.Services.Emulator;
using Trion.Desktop.Models;
using Trion.Desktop.Services;

namespace Trion.Desktop.ViewModels;

public partial class SinglePlayerViewModel : ObservableObject
{
    private readonly ILocalizationService      _loc;
    private readonly ISettingsService          _settings;
    private readonly IEmulatorInstallerService _installer;
    private readonly INotificationService      _notifications;
    private readonly IGameClientService        _gameClient;
    private readonly ClientDataExtractor       _extractor;

    public ILocalizationService Loc => _loc;
    public ObservableCollection<EmulatorCardViewModel> Emulators { get; } = [];

    [ObservableProperty] private bool   _isLoading       = true;
    [ObservableProperty] private bool   _hasAnyAvailable = false;
    [ObservableProperty] private bool   _hasCatalogError = false;
    [ObservableProperty] private string _catalogError    = "";

    // -- Tier ranking ----------------------------------------------------------

    private static readonly Dictionary<string, int> TierRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["guest"]    = -1,
        ["free"]     = 0,
        ["support"]  = 1,
        ["guardian"] = 2,
        ["champion"] = 3,
        ["legend"]   = 4,
    };

    private static bool MeetsTier(string userTier, string required) =>
        TierRank.GetValueOrDefault(userTier, 0) >= TierRank.GetValueOrDefault(required, 0);

    // -- Constructor -----------------------------------------------------------

    public SinglePlayerViewModel(ISettingsService settings, ILocalizationService loc,
                                 IEmulatorInstallerService installer,
                                 INotificationService notifications,
                                 IGameClientService gameClient,
                                 ClientDataExtractor extractor)
    {
        _loc           = loc;
        _settings      = settings;
        _installer     = installer;
        _notifications = notifications;
        _gameClient    = gameClient;
        _extractor     = extractor;

        _ = LoadCatalogAsync();
    }

    private static string NormalizeExpansion(string expansion) =>
        expansion.ToLowerInvariant() switch
        {
            "vanilla" => "classic",
            var e     => e,
        };

    private static string ExpansionFromEmulatorId(string emulatorId) =>
        emulatorId.ToLowerInvariant() switch
        {
            "azerothcore"      => "wotlk",
            "trinitycore"      => "wotlk",
            "trinitycore-cata" => "cata",
            "trinitycore-mop"  => "mop",
            "arguscore"        => "legion",
            "cmangos-wotlk"    => "wotlk",
            "cmangos-tbc"      => "tbc",
            "cmangos-classic"  => "classic",
            "vmangos"          => "classic",
            _                  => ""
        };

    // -- Catalog loading -------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshCatalogAsync() => await LoadCatalogAsync();
    private bool CanRefresh() => !IsLoading;

    private async Task LoadCatalogAsync()
    {
        IsLoading       = true;
        HasCatalogError = false;
        CatalogError    = "";

        foreach (var card in Emulators)
            card.PropertyChanged -= OnCardPropertyChanged;
        Emulators.Clear();

        var catalog = await _installer.FetchCatalogAsync(CancellationToken.None);

        if (catalog.Count == 0)
        {
            CatalogError    = "No repacks available -- the service may be offline.";
            HasCatalogError = true;
            IsLoading       = false;
            return;
        }

        var cfg      = _settings.Current;
        var userTier = string.IsNullOrEmpty(cfg.AccountToken)
            ? "guest"
            : (cfg.AccountApiTier ?? "free");
        var baseDir  = AppDomain.CurrentDomain.BaseDirectory;

        App.Current?.Dispatcher.Invoke(() =>
        {
            foreach (var entry in catalog)
            {
                var slug     = entry.Slug;
                var exeName  = entry.ServerExecutable;
                var authName = entry.AuthExecutable;
                var locked   = !MeetsTier(userTier, entry.AccessType);

                // Resolve install path: saved -> default servers/<slug>/
                var installPath = cfg.RepackInstallPaths.TryGetValue(slug, out var saved) && !string.IsNullOrWhiteSpace(saved)
                    ? saved
                    : Path.Combine(baseDir, "servers", slug);

                var card = new EmulatorCardViewModel(
                    _settings, _installer, _notifications,
                    name:            entry.Name,
                    shortName:       slug,
                    apiEmulatorName: slug,
                    accentBrush:     ExpansionBrush(entry.Expansion),
                    profile:         null,
                    getInstalled:    c => c.RepackInstalled.GetValueOrDefault(slug, false),
                    setInstalled:    (c, v) => c.RepackInstalled[slug] = v,
                    getEnabled:      c => c.RepackEnabled.GetValueOrDefault(slug, false),
                    setEnabled:      (c, v) => c.RepackEnabled[slug] = v,
                    getVersion:      c => c.RepackVersion.GetValueOrDefault(slug, ""),
                    setVersion:      (c, v) => c.RepackVersion[slug] = v,
                    getInstallPath:  c => c.RepackInstallPaths.GetValueOrDefault(slug, installPath),
                    setInstallPath:  (c, v) => c.RepackInstallPaths[slug] = v,
                    getExePath:      c => c.RepackWorldExePath.GetValueOrDefault(slug, ""),
                    setExePath:      (c, v) => c.RepackWorldExePath[slug] = v,
                    getLogonExePath: c => c.RepackAuthExePath.GetValueOrDefault(slug, ""),
                    setLogonExePath: (c, v) => c.RepackAuthExePath[slug] = v,
                    getWorldExeName: c => c.RepackWorldExeName.GetValueOrDefault(slug, exeName),
                    setWorldExeName: (c, v) => c.RepackWorldExeName[slug] = v,
                    getLogonExeName: c => c.RepackAuthExeName.GetValueOrDefault(slug, authName),
                    setLogonExeName: (c, v) => c.RepackAuthExeName[slug] = v);

                card.DownloadUrl   = entry.DownloadUrl;
                card.DownloadCount = entry.DownloadCount;
                card.IsAvailable   = !locked;
                card.SetLatestVersion(entry.Version);

                var expansion = NormalizeExpansion(
                    !string.IsNullOrEmpty(entry.Expansion)
                        ? entry.Expansion
                        : ExpansionFromEmulatorId(entry.EmulatorId ?? ""));
                card.SetGameClientService(_gameClient, expansion, entry.EmulatorId ?? "", _loc, _extractor);

                card.PropertyChanged += OnCardPropertyChanged;
                Emulators.Add(card);
            }

            HasAnyAvailable = Emulators.Any(c => c.IsAvailable);

            // Guard against corrupted settings: disable all but the first enabled card,
            // then re-sync the winner's DB/connection settings from its manifest.
            var firstEnabled = Emulators.FirstOrDefault(c => c.IsEnabled);
            if (firstEnabled is not null)
            {
                foreach (var card in Emulators)
                {
                    if (card != firstEnabled && card.IsEnabled)
                        card.DisableExternally();
                }
                // Re-stamp DB settings so they always match the enabled repack's manifest,
                // even when the settings file is stale (e.g. after a RepackService rebuild).
                firstEnabled.ReapplyEnabledDefaults();
            }

            IsLoading = false;
        });

        // Overwrite latest versions with DB-backed data from Phoenix.
        // This is more reliable than the live RepackService data in the catalog.
        var dbVersions = await _installer.FetchRepackLatestVersionsAsync(CancellationToken.None);
        if (dbVersions.Count > 0)
        {
            App.Current?.Dispatcher.Invoke(() =>
            {
                foreach (var card in Emulators)
                {
                    if (dbVersions.TryGetValue(card.ShortName, out var v) && v is not ("--" or ""))
                        card.SetLatestVersion(v);
                }
            });
        }
    }

    // -- Mutual exclusion: only one expansion active at a time -----------------

    private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EmulatorCardViewModel.IsEnabled)) return;
        if (sender is not EmulatorCardViewModel changed || !changed.IsEnabled) return;

        foreach (var card in Emulators)
        {
            if (card != changed)
                card.DisableExternally();
        }

        // Disable custom repack when any catalog card becomes active.
        var cfg = _settings.Current;
        if (cfg.LaunchCustomCore)
        {
            cfg.LaunchCustomCore = false;
            _settings.Save();
        }
    }

    partial void OnIsLoadingChanged(bool value) => RefreshCatalogCommand.NotifyCanExecuteChanged();

    // -- Expansion accent colours ----------------------------------------------

    // Colours match the website's expansion-btn__icon--{expansion} CSS gradients (end stop).
    private static readonly Dictionary<string, string> ExpansionColors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["classic"]                  = "#c4922e",
            ["tbc"]                      = "#00aa00",
            ["the burning crusade"]      = "#00aa00",
            ["wotlk"]                    = "#3b82c4",
            ["wrath of the lich king"]   = "#3b82c4",
            ["cata"]                     = "#ff4500",
            ["cataclysm"]                = "#ff4500",
            ["mop"]                      = "#00c874",
            ["mists of pandaria"]        = "#00c874",
            ["legion"]                   = "#7b2d8b",
        };

    private static Brush ExpansionBrush(string expansion)
    {
        var hex = ExpansionColors.TryGetValue(expansion.Trim(), out var c) ? c : "#546E7A";
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }
}

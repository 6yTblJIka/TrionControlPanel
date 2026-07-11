using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Channels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trion.Core.Services.Emulator;
using Trion.Desktop.Models;
using Trion.Desktop.Services;
using Trion.Desktop.Views;

namespace Trion.Desktop.ViewModels;

public partial class InstallerViewModel : ObservableObject
{
    private readonly ISettingsService          _settings;
    private readonly ILocalizationService      _loc;
    private readonly IEmulatorInstallerService _installer;
    private readonly IGameClientService        _gameClient;
    private readonly ClientDataExtractor       _extractor;

    public ILocalizationService Loc => _loc;

    // -- Expansion rows (populated from catalog) -------------------------------

    public ObservableCollection<ExpansionItem> Expansions { get; } = [];

    // -- UI state --------------------------------------------------------------

    [ObservableProperty] private bool   _isLoading     = true;
    [ObservableProperty] private bool   _isInstalling;
    [ObservableProperty] private bool   _hasCatalogError;
    [ObservableProperty] private string _catalogError  = "";
    [ObservableProperty] private bool   _isGuest;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _progressPhase  = string.Empty;
    [ObservableProperty] private string _progressStatus = string.Empty;
    [ObservableProperty] private double _speedMbps;
    [ObservableProperty] private string _pointsText     = string.Empty;
    [ObservableProperty] private string _pointsTier     = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstallResult))]
    private string _installResult  = string.Empty;
    [ObservableProperty] private bool   _installResultIsError;

    public bool HasInstallResult => !string.IsNullOrEmpty(InstallResult);

    /// <summary>Formatted download speed, e.g. "14.2 MB/s". Empty when idle.</summary>
    public string SpeedText => SpeedMbps > 0.05 ? $"{SpeedMbps:F1} MB/s" : string.Empty;

    private CancellationTokenSource? _cts;

    // -- Tier ranking ----------------------------------------------------------

    private static readonly Dictionary<string, int> TierRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["guest"]    = -1,  // not logged in -- can see but not install anything
        ["free"]     = 0,
        ["support"]  = 1,
        ["guardian"] = 2,
        ["champion"] = 3,
        ["legend"]   = 4,
    };

    private static bool MeetsTier(string userTier, string required) =>
        TierRank.GetValueOrDefault(userTier, 0) >= TierRank.GetValueOrDefault(required, 0);

    private static string NormalizeExpansion(string expansion) =>
        expansion.ToLowerInvariant() switch
        {
            "vanilla" => "classic",   // RepackService uses "vanilla"; we use "classic"
            var e     => e,
        };

    private static string ExpansionFromEmulatorId(string emulatorId) =>
        emulatorId.ToLowerInvariant() switch
        {
            "azerothcore"     => "wotlk",
            "trinitycore"     => "wotlk",
            "trinitycore-cata"=> "cata",
            "trinitycore-mop" => "mop",
            "arguscore"       => "legion",
            "cmangos-wotlk"   => "wotlk",
            "cmangos-tbc"     => "tbc",
            "cmangos-classic" => "classic",
            "vmangos"         => "classic",
            _                 => ""
        };

    // -- Constructor -----------------------------------------------------------

    public InstallerViewModel(
        ISettingsService          settings,
        ILocalizationService      loc,
        IEmulatorInstallerService installer,
        IGameClientService        gameClient,
        ClientDataExtractor       extractor)
    {
        _settings   = settings;
        _loc        = loc;
        _installer  = installer;
        _gameClient = gameClient;
        _extractor  = extractor;

        _installer.PointsUpdated += (balance, tier) =>
        {
            PointsText = balance == -1 ? "Unlimited pts" : $"{balance:N0} pts";
            PointsTier = tier;
        };

        _ = LoadCatalogAsync();
    }

    // -- Load catalog dynamically from RepackService (via Trion.API) -----------

    private async Task LoadCatalogAsync()
    {
        IsLoading    = true;
        CatalogError = "";
        Expansions.Clear();

        var catalog = await _installer.FetchCatalogAsync(CancellationToken.None);

        if (catalog.Count == 0)
        {
            CatalogError    = "No repacks are available right now. The RepackService may be offline.";
            HasCatalogError = true;
            IsLoading       = false;
            return;
        }

        HasCatalogError = false;

        var cfg      = _settings.Current;
        // Use "guest" when not logged in so free-tier repacks correctly show as locked
        var userTier = string.IsNullOrEmpty(cfg.AccountToken)
            ? "guest"
            : (cfg.AccountApiTier ?? "free");
        IsGuest      = userTier == "guest";
        var baseDir  = AppDomain.CurrentDomain.BaseDirectory;

        foreach (var entry in catalog)
        {
            var locked      = !MeetsTier(userTier, entry.AccessType);
            var expansion   = NormalizeExpansion(
                                  !string.IsNullOrEmpty(entry.Expansion)
                                      ? entry.Expansion
                                      : ExpansionFromEmulatorId(entry.EmulatorId));
            var item        = new ExpansionItem(entry.Name, entry.Slug,
                                  entry.AccessType, locked, entry.ServerExecutable, expansion)
            {
                DownloadUrl = entry.DownloadUrl,
            };

            // Resolve install path: saved path -> default servers/<slug>/
            if (cfg.RepackInstallPaths.TryGetValue(entry.Slug, out var savedPath) &&
                !string.IsNullOrWhiteSpace(savedPath))
            {
                item.InstallPath = savedPath;
            }
            else
            {
                item.InstallPath = Path.Combine(baseDir, "servers", entry.Slug);
            }

            item.ExePath        = Path.Combine(item.InstallPath, entry.ServerExecutable);
            item.IsInstalled    = cfg.RepackInstalled.GetValueOrDefault(entry.Slug, false);
            item.LocalVersion   = _installer.GetInstalledRepackVersion(item.InstallPath);
            item.LatestVersion  = entry.Version;

            Expansions.Add(item);
        }

        IsLoading = false;
    }

    // -- Commands --------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync(ExpansionItem item)
    {
        // Show the game client dialog whenever the client path isn't configured OR
        // extraction hasn't succeeded yet — so the dialog keeps appearing until the
        // full setup is confirmed complete, regardless of stale saved paths.
        if (!string.IsNullOrEmpty(item.Expansion) && !_gameClient.IsClientConfigured(item.Expansion))
        {
            var expName = item.Expansion.ToLowerInvariant() switch
            {
                "classic" => _loc["GameClientExpClassic"],
                "tbc"     => _loc["GameClientExpTBC"],
                "wotlk"   => _loc["GameClientExpWotLK"],
                "cata"    => _loc["GameClientExpCata"],
                "mop"     => _loc["GameClientExpMoP"],
                _         => _gameClient.GetExpansionDisplayName(item.Expansion),
            };
            TrionMessageBox.Show(
                string.Format(_loc["MsgGameClientRequiredBody"], expName),
                _loc["MsgGameClientRequiredTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _cts = new CancellationTokenSource();
        IsInstalling          = true;
        InstallResult         = string.Empty;
        InstallResultIsError  = false;
        ProgressValue         = 0;
        ProgressPhase         = string.Empty;
        ProgressStatus        = string.Empty;
        SpeedMbps             = 0;

        var progress = new Progress<InstallProgress>(p =>
        {
            ProgressValue  = p.Percent;
            ProgressPhase  = p.Phase;
            ProgressStatus = p.StatusText;
            SpeedMbps      = p.SpeedMbps;
        });

        try
        {
            await _installer.InstallAsync(item.ApiName, item.InstallPath, progress, _cts.Token, item.DownloadUrl);
            item.IsInstalled  = true;
            item.LocalVersion = _installer.GetInstalledRepackVersion(item.InstallPath);
            SaveState(item);

            // Run extraction now that the tools are present in the install folder.
            if (!string.IsNullOrEmpty(item.Expansion)
                && !_gameClient.IsDataExtracted(item.Expansion)
                && _gameClient.IsClientConfigured(item.Expansion))
            {
                ProgressPhase  = "Extracting game data";
                ProgressStatus = "Running extraction tools…";
                await RunExtractionAsync(item, _cts.Token);
                InstallResult = "Repack installed and game data extracted successfully.";
            }
            else
            {
                InstallResult = "Repack installed successfully.";
            }

            // Ensure worldserver.conf DataDir points at the shared data directory.
            if (!string.IsNullOrEmpty(item.Expansion))
                _gameClient.PatchDataDir(item.InstallPath, item.Expansion);
        }
        catch (OperationCanceledException)
        {
            InstallResult        = "Installation cancelled.";
            InstallResultIsError = false;
        }
        catch (Exception ex)
        {
            InstallResult        = $"Error: {ex.Message}";
            InstallResultIsError = true;
        }
        finally
        {
            IsInstalling = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task RunExtractionAsync(ExpansionItem item, CancellationToken ct)
    {
        var channel = Channel.CreateBounded<ExtractProgressEvent>(100);
        var extractTask = _extractor.ExtractAsync(
            _gameClient.GetClientPath(item.Expansion)!,
            item.InstallPath,
            _gameClient.GetDataPath(item.Expansion),
            channel.Writer, ct);

        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
        {
            ProgressPhase  = evt.StepName;
            ProgressValue  = evt.PercentComplete;
            ProgressStatus = evt.Message ?? string.Empty;
        }

        await extractTask;
    }

    private bool CanInstall(ExpansionItem? e) =>
        !IsInstalling && e is { IsInstalled: false, IsLocked: false };

    [RelayCommand(CanExecute = nameof(CanRepair))]
    private async Task RepairAsync(ExpansionItem expansion) =>
        await RunAsync(expansion,
            (p, ct) => _installer.RepairAsync(expansion.ApiName, expansion.InstallPath, p, ct),
            installed: true);

    private bool CanRepair(ExpansionItem? e) =>
        !IsInstalling && e is { IsInstalled: true, IsLocked: false };

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private async Task UninstallAsync(ExpansionItem expansion) =>
        await RunAsync(expansion,
            (p, ct) => _installer.UninstallAsync(expansion.InstallPath, p, ct),
            installed: false);

    private bool CanUninstall(ExpansionItem? e) =>
        !IsInstalling && e is { IsInstalled: true };

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshCatalogAsync() => await LoadCatalogAsync();
    private bool CanRefresh() => !IsInstalling && !IsLoading;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();
    private bool CanCancel() => IsInstalling;

    // -- Central operation runner ----------------------------------------------

    private async Task RunAsync(
        ExpansionItem expansion,
        Func<IProgress<InstallProgress>, CancellationToken, Task> operation,
        bool installed)
    {
        _cts          = new CancellationTokenSource();
        IsInstalling  = true;
        ProgressValue = 0;
        ProgressPhase  = string.Empty;
        ProgressStatus = string.Empty;
        SpeedMbps      = 0;

        var progress = new Progress<InstallProgress>(p =>
        {
            ProgressValue  = p.Percent;
            ProgressPhase  = p.Phase;
            ProgressStatus = p.StatusText;
            SpeedMbps      = p.SpeedMbps;
        });

        try
        {
            await operation(progress, _cts.Token);

            expansion.IsInstalled  = installed;
            expansion.LocalVersion = installed
                ? _installer.GetInstalledRepackVersion(expansion.InstallPath)
                : "--";

            SaveState(expansion);
        }
        catch (OperationCanceledException)
        {
            ProgressStatus = "Cancelled.";
        }
        catch (Exception ex)
        {
            ProgressStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsInstalling = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    partial void OnIsInstallingChanged(bool value) => RefreshCommands();
    partial void OnIsLoadingChanged(bool value)    => RefreshCommands();
    partial void OnSpeedMbpsChanged(double value)  => OnPropertyChanged(nameof(SpeedText));

    private void RefreshCommands()
    {
        InstallCommand.NotifyCanExecuteChanged();
        RepairCommand.NotifyCanExecuteChanged();
        UninstallCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RefreshCatalogCommand.NotifyCanExecuteChanged();
    }

    // -- Settings persistence (keyed by slug) ----------------------------------

    private void SaveState(ExpansionItem item)
    {
        var c = _settings.Current;
        c.RepackInstalled[item.ApiName]    = item.IsInstalled;
        c.RepackInstallPaths[item.ApiName] = item.InstallPath;
        _settings.Save();
    }
}

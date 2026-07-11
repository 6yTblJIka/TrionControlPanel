using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Media;
using Trion.Core.Services.Emulator;
using Trion.Desktop.Models;
using Trion.Desktop.Services;
using Trion.Desktop.Views;

namespace Trion.Desktop.ViewModels;

public partial class EmulatorCardViewModel : ObservableObject
{
    private readonly ISettingsService             _settings;
    private readonly IEmulatorInstallerService    _installer;
    private readonly INotificationService         _notifications;
    private readonly string?                      _apiEmulatorName;   // null = Custom (no API)
    private readonly EmulatorProfile?             _profile;           // null = Custom (user-managed)
    private readonly Func<AppSettings, bool>      _getInstalled;
    private readonly Action<AppSettings, bool>    _setInstalled;
    private readonly Func<AppSettings, bool>      _getEnabled;
    private readonly Action<AppSettings, bool>    _setEnabled;
    private readonly Func<AppSettings, string>    _getVersion;
    private readonly Action<AppSettings, string>  _setVersion;
    private readonly Func<AppSettings, string>    _getInstallPath;
    private readonly Action<AppSettings, string>  _setInstallPath;
    private readonly Func<AppSettings, string>    _getExePath;
    private readonly Action<AppSettings, string>  _setExePath;
    private readonly Func<AppSettings, string>    _getLogonExePath;
    private readonly Action<AppSettings, string>  _setLogonExePath;
    private readonly Func<AppSettings, string>    _getWorldExeName;
    private readonly Action<AppSettings, string>  _setWorldExeName;
    private readonly Func<AppSettings, string>    _getLogonExeName;
    private readonly Action<AppSettings, string>  _setLogonExeName;

    private CancellationTokenSource? _cts;
    private Process?                  _worldProc;
    private Process?                  _logonProc;
    private volatile int              _worldCrashPid;
    private volatile int              _logonCrashPid;
    private CancellationTokenSource?  _worldMonitorCts;
    private CancellationTokenSource?  _logonMonitorCts;
    private const int                 MaxCrashRestarts = 3;

    private IGameClientService?   _gameClient;
    private ClientDataExtractor?  _extractor;
    private string                _expansion  = "";
    private string                _emulatorId = "";
    private ILocalizationService? _locService;

    // -- Identity --------------------------------------------------------------

    public string Name        { get; }
    public string ShortName   { get; }
    public Brush  AccentBrush { get; }
    public Color  AccentColor => AccentBrush is SolidColorBrush sb ? sb.Color : Colors.Transparent;
    public bool   HasApiInstall => !string.IsNullOrEmpty(_apiEmulatorName);

    /// <summary>
    /// Set by <see cref="SinglePlayerViewModel"/> after the available-packages API response
    /// is received. Hides this card when the current account tier has no access.
    /// Defaults to <c>true</c> so all cards are visible before the API responds.
    /// </summary>
    [ObservableProperty] private bool _isAvailable = false;

    // -- Installed / enabled / working -----------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RepairCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleEnabledCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartWorldCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartLogonCommand))]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private bool _isInstalled;

    [ObservableProperty] private bool _isEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RepairCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartWorldCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartLogonCommand))]
    private bool _isWorking;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartWorldCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopWorldCommand))]
    private bool _isWorldRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartLogonCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopLogonCommand))]
    private bool _isLogonRunning;

    // -- Progress display ------------------------------------------------------

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _phaseLabel  = string.Empty;   // "Downloading"
    [ObservableProperty] private string _speedText   = string.Empty;   // "12.3 MB/s"
    [ObservableProperty] private string _statusText  = string.Empty;   // "450 MB / 1.2 GB"

    // -- Version ---------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    private string _installedVersion = "--";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    private string _latestVersion = "--";

    public bool HasUpdate =>
        IsInstalled
        && InstalledVersion is not ("--" or "")
        && LatestVersion    is not ("--" or "")
        && InstalledVersion != LatestVersion;

    // -- Install path ----------------------------------------------------------

    [ObservableProperty] private string _installPath = string.Empty;

    // -- Catalog metadata (populated after version load) -----------------------

    /// <summary>Direct download URL from the RepackService catalog. Null = fall back to legacy endpoint.</summary>
    public string? DownloadUrl { get; set; }

    /// <summary>Total downloads reported by Phoenix. 0 when unknown or not an API repack.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDownloadCount))]
    [NotifyPropertyChangedFor(nameof(DownloadCountText))]
    private int _downloadCount;

    public bool   HasDownloadCount  => HasApiInstall && DownloadCount > 0;
    public string DownloadCountText => $"{DownloadCount:N0} downloads";

    /// <summary>
    /// Catalog entry received from GET /Trion/catalog. Null until the first
    /// successful fetch. Used to configure exe paths after install.
    /// </summary>
    private PackageCatalogEntry? _catalogEntry;

    /// <summary>
    /// When true, the world server will automatically import its SQL database
    /// on first startup (AzerothCore). When false, manual DB import is needed.
    /// </summary>
    public bool AutoDbImport => _catalogEntry?.AutoDbImport ?? false;

    /// <summary>Set from <see cref="SinglePlayerViewModel"/> after catalog is fetched.</summary>
    internal void SetCatalogEntry(PackageCatalogEntry entry)
    {
        _catalogEntry = entry;
        OnPropertyChanged(nameof(AutoDbImport));
    }

    // -- Construction ----------------------------------------------------------

    public EmulatorCardViewModel(
        ISettingsService            settings,
        IEmulatorInstallerService   installer,
        INotificationService        notifications,
        string                      name,
        string                      shortName,
        string?                     apiEmulatorName,
        Brush                       accentBrush,
        EmulatorProfile?            profile,
        Func<AppSettings, bool>     getInstalled,
        Action<AppSettings, bool>   setInstalled,
        Func<AppSettings, bool>     getEnabled,
        Action<AppSettings, bool>   setEnabled,
        Func<AppSettings, string>   getVersion,
        Action<AppSettings, string> setVersion,
        Func<AppSettings, string>   getInstallPath,
        Action<AppSettings, string> setInstallPath,
        Func<AppSettings, string>   getExePath,
        Action<AppSettings, string> setExePath,
        Func<AppSettings, string>   getLogonExePath,
        Action<AppSettings, string> setLogonExePath,
        Func<AppSettings, string>   getWorldExeName,
        Action<AppSettings, string> setWorldExeName,
        Func<AppSettings, string>   getLogonExeName,
        Action<AppSettings, string> setLogonExeName)
    {
        _settings        = settings;
        _installer       = installer;
        _notifications   = notifications;
        Name             = name;
        ShortName        = shortName;
        _apiEmulatorName = apiEmulatorName;
        _profile         = profile;
        AccentBrush      = accentBrush;
        _getInstalled    = getInstalled;
        _setInstalled    = setInstalled;
        _getEnabled      = getEnabled;
        _setEnabled      = setEnabled;
        _getVersion      = getVersion;
        _setVersion      = setVersion;
        _getInstallPath  = getInstallPath;
        _setInstallPath  = setInstallPath;
        _getExePath      = getExePath;
        _setExePath      = setExePath;
        _getLogonExePath = getLogonExePath;
        _setLogonExePath = setLogonExePath;
        _getWorldExeName = getWorldExeName;
        _setWorldExeName = setWorldExeName;
        _getLogonExeName = getLogonExeName;
        _setLogonExeName = setLogonExeName;

        var cfg = settings.Current;
        IsInstalled      = getInstalled(cfg);
        IsEnabled        = getEnabled(cfg);
        var v = getVersion(cfg);
        InstalledVersion = string.IsNullOrEmpty(v) ? "--" : v;
        InstallPath      = ResolveInstallPath(cfg);

        // Refresh local version from exe if we have a path
        RefreshLocalVersion();
    }

    internal void SetGameClientService(IGameClientService svc, string expansion, string emulatorId,
                                        ILocalizationService loc, ClientDataExtractor extractor)
    {
        _gameClient = svc;
        _extractor  = extractor;
        _expansion  = expansion;
        _emulatorId = emulatorId;
        _locService = loc;
    }

    // -- Install ---------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (!string.IsNullOrEmpty(_expansion) && _gameClient is not null
            && !_gameClient.IsClientConfigured(_expansion))
        {
            var expName = _expansion.ToLowerInvariant() switch
            {
                "classic" => _locService?["GameClientExpClassic"] ?? "Classic (Vanilla)",
                "tbc"     => _locService?["GameClientExpTBC"]     ?? "The Burning Crusade",
                "wotlk"   => _locService?["GameClientExpWotLK"]   ?? "Wrath of the Lich King",
                "cata"    => _locService?["GameClientExpCata"]     ?? "Cataclysm",
                "mop"     => _locService?["GameClientExpMoP"]     ?? "Mists of Pandaria",
                _         => _gameClient.GetExpansionDisplayName(_expansion),
            };
            TrionMessageBox.Show(
                string.Format(
                    _locService?["MsgGameClientRequiredBody"]
                        ?? "No WoW client linked for {0}.\n\nGo to Settings → Game Client.",
                    expName),
                _locService?["MsgGameClientRequiredTitle"] ?? "Game Client Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        await RunOperationAsync("Installing", async ct =>
        {
            try
            {
                await _installer.InstallAsync(_apiEmulatorName!, InstallPath,
                    MakeProgress(), ct, DownloadUrl);
                var catalog = await ResolveCatalogEntryAsync(ct);
                Apply(installed: true, catalog, forceEnable: true);

                string finalStatus;

                if (!string.IsNullOrEmpty(_expansion) && _gameClient is not null && _extractor is not null)
                {
                    if (_gameClient.IsDataExtracted(_expansion, _emulatorId))
                    {
                        // Data from a previous install — nothing to do.
                        finalStatus = _locService?["MsgInstallCompleteDataReady"]
                                      ?? "Repack installed — game data already ready.";
                    }
                    else if (_gameClient.IsClientConfigured(_expansion))
                    {
                        // Run extraction now; the progress bar drives all four steps.
                        var clientPath = _gameClient.GetClientPath(_expansion)!;
                        var dataPath   = _gameClient.GetDataPath(_expansion, _emulatorId);

                        var channel     = Channel.CreateBounded<ExtractProgressEvent>(100);
                        var extractTask = _extractor.ExtractAsync(
                            clientPath, InstallPath, dataPath, channel.Writer, ct);

                        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
                        {
                            App.Current?.Dispatcher.InvokeAsync(() =>
                            {
                                PhaseLabel = evt.StepName;
                                Progress   = evt.PercentComplete;
                                StatusText = evt.Message ?? string.Empty;
                            });
                        }

                        await extractTask;

                        finalStatus = _gameClient.IsDataExtracted(_expansion, _emulatorId)
                            ? (_locService?["MsgInstallCompleteDataExtracted"]
                               ?? "Repack installed — game data extracted and ready.")
                            : (_locService?["MsgInstallCompleteDataPartial"]
                               ?? "Repack installed — some extraction steps were skipped (tools not found).");
                    }
                    else
                    {
                        finalStatus = _locService?["MsgInstallCompleteNoClient"]
                                      ?? "Repack installed — link a WoW client in Settings → Game Client to extract game data.";
                    }

                    // Point worldserver.conf DataDir at the shared per-expansion data folder.
                    _gameClient.PatchDataDir(InstallPath, _expansion, _emulatorId);
                }
                else
                {
                    finalStatus = _locService?["MsgInstallComplete"]
                                  ?? "Repack installed successfully.";
                }

                App.Current?.Dispatcher.InvokeAsync(() =>
                {
                    Progress   = 100;
                    PhaseLabel = _locService?["PhaseComplete"] ?? "Complete";
                    StatusText = finalStatus;
                });
            }
            catch (OperationCanceledException)
            {
                App.Current?.Dispatcher.InvokeAsync(() =>
                {
                    PhaseLabel = _locService?["PhaseCancelCleanup"] ?? "Cancelling...";
                    StatusText = _locService?["MsgInstallCancelCleanup"] ?? "Removing partial installation...";
                    Progress   = 0;
                });

                Apply(installed: false);

                if (Directory.Exists(InstallPath))
                {
                    try { await Task.Run(() => Directory.Delete(InstallPath, recursive: true), CancellationToken.None); }
                    catch { /* best effort — files may be locked */ }
                }

                throw;
            }
        });
    }
    private bool CanInstall() => !IsInstalled && !IsWorking && HasApiInstall;

    // -- Update ----------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateAsync()
    {
        await RunOperationAsync("Updating", async ct =>
        {
            // Catalog repacks supply a direct DownloadUrl — re-download the full package.
            // Legacy SPP repacks have no DownloadUrl; use the diff-based repair endpoint instead.
            if (!string.IsNullOrEmpty(DownloadUrl))
                await _installer.InstallAsync(_apiEmulatorName!, InstallPath,
                    MakeProgress(), ct, DownloadUrl);
            else
                await _installer.RepairAsync(_apiEmulatorName!, InstallPath,
                    MakeProgress(), ct);

            var catalog = await ResolveCatalogEntryAsync(ct);
            Apply(installed: true, catalog);
        });
    }
    private bool CanUpdate() => HasUpdate && !IsWorking && HasApiInstall;

    // -- Repair ----------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanRepair))]
    private async Task RepairAsync()
    {
        await RunOperationAsync("Repairing", async ct =>
        {
            // Catalog repacks are distributed as a single ZIP on Phoenix storage —
            // individual files are not stored separately, so per-file diff repair
            // is not possible. Re-download the full package instead.
            if (!string.IsNullOrEmpty(DownloadUrl))
                await _installer.InstallAsync(_apiEmulatorName!, InstallPath,
                    MakeProgress(), ct, DownloadUrl);
            else
                await _installer.RepairAsync(_apiEmulatorName!, InstallPath,
                    MakeProgress(), ct);

            var catalog = await ResolveCatalogEntryAsync(ct);
            Apply(installed: true, catalog);
        });
    }
    private bool CanRepair() => IsInstalled && !IsWorking && HasApiInstall;

    // -- Clear Databases -------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanClearDatabases))]
    private async Task ClearDatabasesAsync()
    {
        await RunOperationAsync("Clearing databases", async ct =>
        {
            var (ok, msg) = await _installer.ClearDatabasesAsync(InstallPath, ct);
            if (!ok) throw new InvalidOperationException(msg);
        });
    }
    private bool CanClearDatabases() => IsInstalled && !IsWorking;

    // -- Uninstall -------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private async Task UninstallAsync()
    {
        await RunOperationAsync("Removing", async ct =>
        {
            // Cancel crash monitors first so they don't restart after we kill the processes.
            _worldCrashPid = 0; _worldMonitorCts?.Cancel(); _worldMonitorCts?.Dispose(); _worldMonitorCts = null;
            _logonCrashPid = 0; _logonMonitorCts?.Cancel(); _logonMonitorCts?.Dispose(); _logonMonitorCts = null;

            // Stop tracked processes. StopRepackProcessesAsync inside UninstallAsync
            // catches anything started externally.
            foreach (var proc in new[] { _worldProc, _logonProc })
            {
                if (proc is null || proc.HasExited) continue;
                try { proc.Kill(entireProcessTree: true); } catch { }
                try { await proc.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(10), ct); } catch { }
            }

            await _installer.UninstallAsync(InstallPath, MakeProgress(), ct);
            Apply(installed: false);
        });
    }
    private bool CanUninstall() => IsInstalled && !IsWorking;

    // -- Cancel ----------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        PhaseLabel = "Cancelling...";
    }
    private bool CanCancel() => IsWorking;

    // -- Start / Stop World Server ---------------------------------------------

    [RelayCommand(CanExecute = nameof(CanStartWorld))]
    private Task StartWorldAsync() => StartWorldCoreAsync(0);
    private bool CanStartWorld() => IsInstalled && !IsWorldRunning && !IsWorking;

    private async Task StartWorldCoreAsync(int crashCount)
    {
        var exePath = _getExePath(_settings.Current);
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            _notifications.Add($"[{Name}] World server exe not found — check Settings.");
            return;
        }

        var psi = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? InstallPath,
            UseShellExecute  = true,
        };

        _worldProc = Process.Start(psi);
        if (_worldProc is null) { _notifications.Add($"[{Name}] World server failed to start."); return; }

        _worldCrashPid = _worldProc.Id;
        IsWorldRunning = true;
        _notifications.Add(crashCount == 0
            ? $"[{Name}] World server started (PID {_worldProc.Id})."
            : $"[{Name}] World server restarted (PID {_worldProc.Id}, attempt {crashCount}/{MaxCrashRestarts}).");

        if (_settings.Current.ServerCrashDetection)
            BeginCrashMonitor(_worldProc, isWorld: true, crashCount);

        await Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanStopWorld))]
    private void StopWorld()
    {
        _worldCrashPid = 0;
        _worldMonitorCts?.Cancel();
        _worldMonitorCts?.Dispose();
        _worldMonitorCts = null;
        try { _worldProc?.Kill(entireProcessTree: true); } catch { }
        _worldProc = null;
        App.Current?.Dispatcher.BeginInvoke(() => { IsWorldRunning = false; StartWorldCommand.NotifyCanExecuteChanged(); StopWorldCommand.NotifyCanExecuteChanged(); });
    }
    private bool CanStopWorld() => IsWorldRunning;

    // -- Start / Stop Logon Server ---------------------------------------------

    [RelayCommand(CanExecute = nameof(CanStartLogon))]
    private Task StartLogonAsync() => StartLogonCoreAsync(0);
    private bool CanStartLogon() => IsInstalled && !IsLogonRunning && !IsWorking;

    private async Task StartLogonCoreAsync(int crashCount)
    {
        var exePath = _getLogonExePath(_settings.Current);
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            _notifications.Add($"[{Name}] Logon server exe not found — check Settings.");
            return;
        }

        var psi = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? InstallPath,
            UseShellExecute  = true,
        };

        _logonProc = Process.Start(psi);
        if (_logonProc is null) { _notifications.Add($"[{Name}] Logon server failed to start."); return; }

        _logonCrashPid = _logonProc.Id;
        IsLogonRunning = true;
        _notifications.Add(crashCount == 0
            ? $"[{Name}] Logon server started (PID {_logonProc.Id})."
            : $"[{Name}] Logon server restarted (PID {_logonProc.Id}, attempt {crashCount}/{MaxCrashRestarts}).");

        if (_settings.Current.ServerCrashDetection)
            BeginCrashMonitor(_logonProc, isWorld: false, crashCount);

        await Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanStopLogon))]
    private void StopLogon()
    {
        _logonCrashPid = 0;
        _logonMonitorCts?.Cancel();
        _logonMonitorCts?.Dispose();
        _logonMonitorCts = null;
        try { _logonProc?.Kill(entireProcessTree: true); } catch { }
        _logonProc = null;
        App.Current?.Dispatcher.BeginInvoke(() => { IsLogonRunning = false; StartLogonCommand.NotifyCanExecuteChanged(); StopLogonCommand.NotifyCanExecuteChanged(); });
    }
    private bool CanStopLogon() => IsLogonRunning;

    // -- Crash monitor ---------------------------------------------------------

    private void BeginCrashMonitor(Process proc, bool isWorld, int crashCount)
    {
        var cts = new CancellationTokenSource();
        if (isWorld)
        {
            _worldMonitorCts?.Cancel();
            _worldMonitorCts?.Dispose();
            _worldMonitorCts = cts;
        }
        else
        {
            _logonMonitorCts?.Cancel();
            _logonMonitorCts?.Dispose();
            _logonMonitorCts = cts;
        }
        _ = Task.Run(() => CrashMonitorLoopAsync(proc, isWorld, crashCount, cts.Token));
    }

    private async Task CrashMonitorLoopAsync(Process proc, bool isWorld, int crashCount, CancellationToken ct)
    {
        string name = isWorld ? "World" : "Logon";
        int    pid  = proc.Id;

        // Poll every 2 s. WaitForExitAsync throws when UseShellExecute = true (no handle access).
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(2_000, ct); }
            catch (OperationCanceledException) { return; }

            if ((isWorld ? _worldCrashPid : _logonCrashPid) == 0) return; // intentional stop

            bool alive;
            try   { alive = !Process.GetProcessById(pid).HasExited; }
            catch (ArgumentException) { alive = false; }

            if (!alive) break;
        }

        if (ct.IsCancellationRequested) return;
        if ((isWorld ? _worldCrashPid : _logonCrashPid) == 0) return;

        _notifications.Add($"[{Name}] {name} server crashed (PID {pid}).");

        await App.Current.Dispatcher.InvokeAsync(() =>
        {
            if (isWorld) { _worldProc = null; IsWorldRunning = false; StartWorldCommand.NotifyCanExecuteChanged(); }
            else         { _logonProc = null; IsLogonRunning = false; StartLogonCommand.NotifyCanExecuteChanged(); }
        });

        if (!_settings.Current.ServerCrashDetection) return;

        if (crashCount >= MaxCrashRestarts)
        {
            var giveUp = $"[{Name}] {name} server crashed {MaxCrashRestarts} times in a row — auto-restart stopped.";
            _notifications.Add(giveUp);
            await App.Current.Dispatcher.InvokeAsync(() =>
            {
                if (isWorld) { _worldCrashPid = 0; StartWorldCommand.NotifyCanExecuteChanged(); StopWorldCommand.NotifyCanExecuteChanged(); }
                else         { _logonCrashPid = 0; StartLogonCommand.NotifyCanExecuteChanged(); StopLogonCommand.NotifyCanExecuteChanged(); }
            });
            return;
        }

        int attempt = crashCount + 1;
        _notifications.Add($"[{Name}] {name} server: restart attempt {attempt}/{MaxCrashRestarts}...");

        await Task.Delay(2_000, ct).ConfigureAwait(false);

        await App.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (isWorld) await StartWorldCoreAsync(attempt);
            else         await StartLogonCoreAsync(attempt);
        });
    }

    // -- Enable / Disable ------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanToggleEnabled))]
    private void ToggleEnabled()
    {
        IsEnabled = !IsEnabled;
        var cfg = _settings.Current;

        if (IsEnabled)
        {
            // Atomically clear every other catalog repack's enabled flag in settings
            // before enabling this one — prevents multiple-enabled drift regardless of
            // whether the PropertyChanged cascade reaches all cards (e.g. cross-thread edge cases).
            foreach (var key in cfg.RepackEnabled.Keys.ToList())
                if (key != ShortName)
                    cfg.RepackEnabled[key] = false;
            _setEnabled(cfg, true);
            ApplyProfileDefaults(cfg);
        }
        else
        {
            _setEnabled(cfg, false);
        }

        _settings.Save();
    }
    private bool CanToggleEnabled() => IsInstalled;

    internal void DisableExternally()
    {
        if (!IsEnabled) return;
        IsEnabled = false;
        _setEnabled(_settings.Current, false);
        _settings.Save();
    }

    /// <summary>
    /// Re-reads the repack manifest and re-stamps all DB/connection settings for this card.
    /// Called by <see cref="SinglePlayerViewModel"/> at startup when a repack is already enabled
    /// so that the global DB settings stay in sync with the installed manifest even if the
    /// settings file is stale or was modified externally.
    /// </summary>
    internal void ReapplyEnabledDefaults()
    {
        if (!IsEnabled) return;
        var cfg = _settings.Current;
        ApplyProfileDefaults(cfg);
        _settings.Save();
    }

    // Schema index values must match AppSettings.SelectedDatabases / the DB-query switch:
    //   0 = TrinityCore 3.3.5a      (sha_pass_hash)
    //   1 = AzerothCore              (LegacySHA1 SRP6, binary salt+verifier)
    //   2 = CMaNGOS / VMaNGOS        (hex v+s columns, realmbuilds)
    //   3 = ArgusCore / TC-master    (BNet SRP6v2, battlenet_accounts table)
    //   4 = TC-Cata / SkyFire MoP    (GruntSRP6, binary salt+verifier, no BNet layer)
    private static (int Schema, string World, string Auth, string Chars) DefaultsForEmulator(string emulatorId) =>
        emulatorId.ToLowerInvariant() switch
        {
            "azerothcore"      => (1, "acore_world",  "acore_auth",       "acore_characters"),
            "trinitycore"      => (0, "world",         "auth",             "characters"),
            "trinitycore-cata" => (4, "world",         "auth",             "characters"),
            "trinitycore-mop"  => (4, "world",         "auth",             "characters"),
            "arguscore"        => (3, "world",         "auth",             "characters"),
            "cmangos-wotlk"    => (2, "mangos2",       "realmd",           "characters2"),
            "cmangos-tbc"      => (2, "mangos1",       "realmd",           "characters1"),
            "cmangos-classic"  => (2, "mangos0",       "realmd",           "characters0"),
            "vmangos"          => (2, "mangos",        "realmd",           "characters"),
            _                  => (-1, "",             "",                 ""),
        };

    /// <summary>
    /// Stamps schema + DB names + connection details into <paramref name="cfg"/> when a repack is enabled.
    /// Priority: repack-manifest.json (authoritative) → EmulatorProfile → emulatorId fallback.
    /// DB names and connection settings are always overwritten so switching repacks fully reconfigures the app.
    /// Exe names are only written when blank so user-customised paths are preserved.
    /// </summary>
    private void ApplyProfileDefaults(AppSettings cfg)
    {
        int    schema;
        string worldDb, authDb, charsDb;

        var manifest = RepackManifest.Load(_getInstallPath(cfg));
        if (manifest is not null)
        {
            // Schema index derived from baseType (preferred) or emulatorId — drives query flavour
            var baseId = string.IsNullOrEmpty(manifest.BaseType) ? manifest.EmulatorId : manifest.BaseType;
            var d = DefaultsForEmulator(baseId);
            schema = d.Schema >= 0 ? d.Schema : 1;   // default AzerothCore if baseType unrecognised

            worldDb = manifest.WorldDatabase;
            authDb  = manifest.AuthDatabase;
            charsDb = manifest.CharDatabase;

            // Apply the exact connection settings the repack was built with
            cfg.DBServerHost     = manifest.DbHost;
            cfg.DBServerPort     = manifest.DbPort;
            cfg.DBServerUser     = manifest.DbUser;
            cfg.DBServerPassword = manifest.DbPass;

            if (!string.IsNullOrEmpty(manifest.HotfixDatabase))
                cfg.HotfixDatabase = manifest.HotfixDatabase;

            // Exe names from manifest; strip extension to match stored format
            if (string.IsNullOrEmpty(_getWorldExeName(cfg)))
                _setWorldExeName(cfg, Path.GetFileNameWithoutExtension(manifest.ServerExecutable));
            if (string.IsNullOrEmpty(_getLogonExeName(cfg)))
                _setLogonExeName(cfg, Path.GetFileNameWithoutExtension(manifest.AuthExecutable));
        }
        else if (_profile is not null)
        {
            schema  = _profile.DatabaseSchema;
            worldDb = _profile.DefaultWorldDb;
            authDb  = _profile.DefaultAuthDb;
            charsDb = _profile.DefaultCharactersDb;

            if (string.IsNullOrEmpty(_getWorldExeName(cfg)))
                _setWorldExeName(cfg, _profile.WorldExeName);
            if (string.IsNullOrEmpty(_getLogonExeName(cfg)))
                _setLogonExeName(cfg, _profile.LogonExeName);
        }
        else if (!string.IsNullOrEmpty(_emulatorId))
        {
            var d = DefaultsForEmulator(_emulatorId);
            if (d.Schema < 0) return;   // unknown emulator — don't touch settings
            schema  = d.Schema;
            worldDb = d.World;
            authDb  = d.Auth;
            charsDb = d.Chars;
        }
        else return;

        // Schema — always update; drives password hashing algorithm and query flavour
        cfg.SelectedDatabases = schema;

        // DB names — always overwrite so switching repacks fully reconfigures
        cfg.WorldDatabase      = worldDb;
        cfg.AuthDatabase       = authDb;
        cfg.CharactersDatabase = charsDb;
    }

    // -- Browse install path ---------------------------------------------------

    [RelayCommand]
    private void BrowseInstallPath()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = $"Select installation folder for {Name}"
        };
        if (dlg.ShowDialog() == true)
        {
            InstallPath = dlg.FolderName;
            _setInstallPath(_settings.Current, dlg.FolderName);
            _settings.Save();
        }
    }

    // -- Version helpers (called externally by SinglePlayerViewModel) ----------

    internal void SetLatestVersion(string version) => LatestVersion = version;

    internal void RefreshLocalVersion()
    {
        // Try manifest first (AzerothCore repacks embed builtAt in repack-manifest.json).
        // Fall back to PE FileVersionInfo for legacy SPP builds.
        var fromManifest = _installer.GetInstalledRepackVersion(InstallPath);
        if (fromManifest != "--")
        {
            InstalledVersion = fromManifest;
            _setVersion(_settings.Current, fromManifest);
            return;
        }

        var exePath = _getExePath(_settings.Current);
        if (!string.IsNullOrEmpty(exePath))
        {
            var local = _installer.GetLocalVersion(exePath);
            InstalledVersion = local;
            _setVersion(_settings.Current, local == "--" ? "" : local);
        }
    }

    // -- Catalog helpers -------------------------------------------------------

    /// <summary>
    /// Fetches the package catalog and returns the entry matching this card's emulator name.
    /// Returns null on failure or if the emulator is not found. The result is also cached
    /// in <see cref="_catalogEntry"/> for UI binding.
    /// </summary>
    private async Task<PackageCatalogEntry?> ResolveCatalogEntryAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_apiEmulatorName)) return null;
        try
        {
            var response = await _installer.FetchPackageCatalogAsync(ct);
            var entry    = response.Catalog.FirstOrDefault(
                e => string.Equals(e.Emulator, _apiEmulatorName, StringComparison.OrdinalIgnoreCase));

            if (entry is not null)
                SetCatalogEntry(entry);

            return entry;
        }
        catch
        {
            return null;
        }
    }

    // -- Helpers ---------------------------------------------------------------

    private async Task RunOperationAsync(string label, Func<CancellationToken, Task> body)
    {
        _cts      = new CancellationTokenSource();
        IsWorking = true;
        Progress  = 0;
        PhaseLabel = $"{label}...";
        SpeedText  = string.Empty;
        StatusText = string.Empty;

        CancelCommand.NotifyCanExecuteChanged();

        try
        {
            await body(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            PhaseLabel = "Cancelled.";
            _notifications.Add($"[{Name}] Operation cancelled.");
        }
        catch (Exception ex)
        {
            PhaseLabel = $"Error: {ex.Message}";
            _notifications.Add($"[{Name}] Error: {ex.Message}");
        }
        finally
        {
            await Task.Delay(1500); // let user read final status
            IsWorking  = false;
            Progress   = 0;
            PhaseLabel = string.Empty;
            SpeedText  = string.Empty;
            StatusText = string.Empty;
            _cts?.Dispose();
            _cts = null;
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    private IProgress<InstallProgress> MakeProgress() =>
        new Progress<InstallProgress>(p =>
        {
            App.Current?.Dispatcher.InvokeAsync(() =>
            {
                Progress   = p.Percent;
                PhaseLabel = p.Phase;
                SpeedText  = p.SpeedMbps > 0.01 ? $"{p.SpeedMbps:F1} MB/s" : string.Empty;
                StatusText = p.StatusText;
            });
        });

    private void Apply(bool installed, PackageCatalogEntry? catalog = null, bool forceEnable = false)
    {
        IsInstalled = installed;
        IsEnabled   = installed && (forceEnable || IsEnabled);
        if (!installed) InstalledVersion = "--";

        var cfg = _settings.Current;
        _setInstalled(cfg, installed);

        if (!installed)
        {
            _setEnabled(cfg, false);
            _setVersion(cfg, "");
        }
        else
        {
            if (forceEnable)
            {
                // A fresh install becomes the active repack -- only one can run at a time.
                foreach (var key in cfg.RepackEnabled.Keys.ToList())
                    if (key != ShortName)
                        cfg.RepackEnabled[key] = false;
                _setEnabled(cfg, true);
                ApplyProfileDefaults(cfg);
            }

            // Scan the install directory for the actual executables.
            // The catalog hint (when available) is tried first; known names are the fallback.
            // Always overwrites so repairs and re-installs pick up the correct paths.
            var catalogWorldHint = catalog?.ServerExecutable ?? _profile?.WorldExeName + ".exe";
            var catalogLogonHint = catalog?.AuthExecutable   ?? _profile?.LogonExeName + ".exe";

            var worldExe = FindExe(InstallPath, catalogWorldHint,
                ["worldserver.exe", "world.exe", "mangosd.exe"]);
            var logonExe = FindExe(InstallPath, catalogLogonHint,
                ["authserver.exe", "realmd.exe", "bnetserver.exe", "logonserver.exe"]);

            if (!string.IsNullOrEmpty(worldExe))
            {
                _setExePath(cfg, worldExe);
                _setWorldExeName(cfg, Path.GetFileNameWithoutExtension(worldExe));
            }
            if (!string.IsNullOrEmpty(logonExe))
            {
                _setLogonExePath(cfg, logonExe);
                _setLogonExeName(cfg, Path.GetFileNameWithoutExtension(logonExe));
            }

            RefreshLocalVersion();
        }
        _settings.Save();
    }

    /// <summary>
    /// Looks for a server executable inside <paramref name="installPath"/> and its
    /// immediate subdirectories. The catalog-relative hint is tried first; then each
    /// name in <paramref name="fallbackNames"/> is probed in order.
    /// Returns the full path of the first match, or null when nothing is found.
    /// </summary>
    private static string? FindExe(string installPath, string? catalogHint, string[] fallbackNames)
    {
        if (!Directory.Exists(installPath)) return null;

        // Catalog hint may be a relative path like "bin/worldserver.exe"
        if (!string.IsNullOrEmpty(catalogHint))
        {
            var full = Path.GetFullPath(Path.Combine(installPath, catalogHint));
            if (File.Exists(full)) return full;
        }

        // Scan root + one level of subdirectories (bin/, tools/, etc.)
        IEnumerable<string> dirs = [installPath, ..Directory.GetDirectories(installPath)];
        foreach (var dir in dirs)
            foreach (var name in fallbackNames)
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path)) return path;
            }

        return null;
    }

    private string ResolveInstallPath(AppSettings cfg)
    {
        var path = _getInstallPath(cfg);
        if (!string.IsNullOrEmpty(path)) return path;

        // Default: <AppDir>/servers/<shortName>
        var fallback = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "servers",
            ShortName.ToLowerInvariant());
        _setInstallPath(cfg, fallback);
        return fallback;
    }
}

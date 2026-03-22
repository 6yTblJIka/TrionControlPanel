using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Windows.Media;
using Trion.Desktop.Models;
using Trion.Desktop.Services;

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
    private readonly Func<AppSettings, string>    _getWorldExeName;
    private readonly Action<AppSettings, string>  _setWorldExeName;
    private readonly Func<AppSettings, string>    _getLogonExeName;
    private readonly Action<AppSettings, string>  _setLogonExeName;

    private CancellationTokenSource? _cts;

    // ── Identity ──────────────────────────────────────────────────────────────

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

    // ── Installed / enabled / working ─────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RepairCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleEnabledCommand))]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private bool _isInstalled;

    [ObservableProperty] private bool _isEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RepairCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isWorking;

    // ── Progress display ──────────────────────────────────────────────────────

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _phaseLabel  = string.Empty;   // "Downloading"
    [ObservableProperty] private string _speedText   = string.Empty;   // "12.3 MB/s"
    [ObservableProperty] private string _statusText  = string.Empty;   // "450 MB / 1.2 GB"

    // ── Version ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private string _installedVersion = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private string _latestVersion = "—";

    public bool HasUpdate =>
        IsInstalled
        && InstalledVersion is not ("—" or "")
        && LatestVersion    is not ("—" or "")
        && InstalledVersion != LatestVersion;

    // ── Install path ──────────────────────────────────────────────────────────

    [ObservableProperty] private string _installPath = string.Empty;

    // ── Construction ──────────────────────────────────────────────────────────

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
        _getWorldExeName = getWorldExeName;
        _setWorldExeName = setWorldExeName;
        _getLogonExeName = getLogonExeName;
        _setLogonExeName = setLogonExeName;

        var cfg = settings.Current;
        IsInstalled      = getInstalled(cfg);
        IsEnabled        = getEnabled(cfg);
        var v = getVersion(cfg);
        InstalledVersion = string.IsNullOrEmpty(v) ? "—" : v;
        InstallPath      = ResolveInstallPath(cfg);

        // Refresh local version from exe if we have a path
        RefreshLocalVersion();
    }

    // ── Install ───────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        await RunOperationAsync("Installing", async ct =>
        {
            await _installer.InstallAsync(_apiEmulatorName!, InstallPath,
                MakeProgress(), ct);
            Apply(installed: true);
        });
    }
    private bool CanInstall() => !IsInstalled && !IsWorking && HasApiInstall;

    // ── Update ────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateAsync()
    {
        await RunOperationAsync("Updating", async ct =>
        {
            await _installer.RepairAsync(_apiEmulatorName!, InstallPath,
                MakeProgress(), ct);
            Apply(installed: true);
        });
    }
    private bool CanUpdate() => HasUpdate && !IsWorking && HasApiInstall;

    // ── Repair ────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanRepair))]
    private async Task RepairAsync()
    {
        await RunOperationAsync("Repairing", async ct =>
        {
            await _installer.RepairAsync(_apiEmulatorName!, InstallPath,
                MakeProgress(), ct);
        });
    }
    private bool CanRepair() => IsInstalled && !IsWorking && HasApiInstall;

    // ── Uninstall ─────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private async Task UninstallAsync()
    {
        await RunOperationAsync("Removing", async ct =>
        {
            await _installer.UninstallAsync(InstallPath, MakeProgress(), ct);
            Apply(installed: false);
        });
    }
    private bool CanUninstall() => IsInstalled && !IsWorking;

    // ── Cancel ────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        PhaseLabel = "Cancelling…";
    }
    private bool CanCancel() => IsWorking;

    // ── Enable / Disable ──────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanToggleEnabled))]
    private void ToggleEnabled()
    {
        IsEnabled = !IsEnabled;
        var cfg = _settings.Current;
        _setEnabled(cfg, IsEnabled);

        // When enabling, stamp the correct exe names, DB schema, and default DB names
        // so that the server control page and database editor work without manual setup.
        if (IsEnabled)
            ApplyProfileDefaults(cfg);

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
    /// Stamps the emulator profile's defaults into <paramref name="cfg"/> when the user
    /// enables this expansion for the first time (or after a reset).
    /// Exe names are only written when still blank so existing paths are never clobbered.
    /// The DB schema is always updated because it must match the active emulator.
    /// DB names are set only when they hold their factory default value so user
    /// customisations survive switching between expansions.
    /// </summary>
    private void ApplyProfileDefaults(AppSettings cfg)
    {
        if (_profile is null) return;

        // Exe names — only set if the user hasn't already configured them
        if (string.IsNullOrEmpty(_getWorldExeName(cfg)))
            _setWorldExeName(cfg, _profile.WorldExeName);

        if (string.IsNullOrEmpty(_getLogonExeName(cfg)))
            _setLogonExeName(cfg, _profile.LogonExeName);

        // DB schema — always update; determines password hashing and query flavour
        cfg.SelectedDatabases = _profile.DatabaseSchema;

        // DB names — set only when the field is blank (never populated by user)
        if (string.IsNullOrEmpty(cfg.WorldDatabase))
            cfg.WorldDatabase = _profile.DefaultWorldDb;

        if (string.IsNullOrEmpty(cfg.AuthDatabase))
            cfg.AuthDatabase = _profile.DefaultAuthDb;

        if (string.IsNullOrEmpty(cfg.CharactersDatabase))
            cfg.CharactersDatabase = _profile.DefaultCharactersDb;
    }

    // ── Browse install path ───────────────────────────────────────────────────

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

    // ── Version helpers (called externally by SinglePlayerViewModel) ──────────

    internal void SetLatestVersion(string version) => LatestVersion = version;

    internal void RefreshLocalVersion()
    {
        var exePath = _getExePath(_settings.Current);
        if (!string.IsNullOrEmpty(exePath))
        {
            var local = _installer.GetLocalVersion(exePath);
            InstalledVersion = local;
            _setVersion(_settings.Current, local == "—" ? "" : local);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task RunOperationAsync(string label, Func<CancellationToken, Task> body)
    {
        _cts      = new CancellationTokenSource();
        IsWorking = true;
        Progress  = 0;
        PhaseLabel = $"{label}…";
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

    private void Apply(bool installed)
    {
        IsInstalled = installed;
        IsEnabled   = installed && IsEnabled;
        if (!installed) InstalledVersion = "—";

        var cfg = _settings.Current;
        _setInstalled(cfg, installed);
        if (!installed)
        {
            _setEnabled(cfg, false);
            _setVersion(cfg, "");
        }
        else
        {
            RefreshLocalVersion();
        }
        _settings.Save();
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

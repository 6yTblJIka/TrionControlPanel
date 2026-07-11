using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Trion.Core.Database;
using Trion.Core.Monitoring;
using Trion.Core.WebServer;
using Trion.Desktop.Infrastructure.Constants;
using Trion.Desktop.Models;
using Trion.Desktop.Services;

namespace Trion.Desktop.ViewModels;

public enum SettingsTab { Trion, Custom, Database, WebServer, GameClient }

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService                      _settings;
    private readonly IThemeService                         _theme;
    private readonly ILocalizationService                  _loc;
    private readonly IMySqlSetupService                    _mysql;
    private readonly INginxPhpSetupService                 _webServer;
    private readonly IDatabaseService                      _db;
    private readonly IOptionsMonitor<ProcessMonitorOptions> _processOpts;
    private readonly IGameClientService                    _gameClient;

    // Auto-save support
    private bool _isLoading;
    private CancellationTokenSource? _saveCts;

    // Properties that reflect UI/service state — changes must not trigger a settings save.
    private static readonly HashSet<string> NonSettingsProperties = new(StringComparer.Ordinal)
    {
        nameof(StatusMessage),   nameof(ActiveTab),
        nameof(IsMySqlInstalled),    nameof(IsMySqlRunning),    nameof(IsMySqlBusy),
        nameof(MySqlStatusMessage),  nameof(MySqlVersionInfo),  nameof(MySqlInstallPath),  nameof(MySqlProcessInfo),
        nameof(IsWebServerInstalled), nameof(IsWebServerRunning), nameof(IsWebServerBusy),
        nameof(WebServerStatusMessage), nameof(WebServerVersionInfo), nameof(WebServerInstallPath), nameof(WebServerProcessInfo),
    };

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_isLoading && e.PropertyName is not null && !NonSettingsProperties.Contains(e.PropertyName))
            ScheduleAutoSave();
    }

    private void ScheduleAutoSave()
    {
        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;
        Task.Delay(500, token).ContinueWith(_ =>
        {
            if (!token.IsCancellationRequested)
                App.Current?.Dispatcher.Invoke(Save);
        }, TaskScheduler.Default);
    }

    [ObservableProperty] private SettingsTab _activeTab = SettingsTab.Trion;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // -- Trion tab -------------------------------------------------------------

    [ObservableProperty] private bool   _serverCrashDetection;
    [ObservableProperty] private bool   _runServerWithWindows;
    [ObservableProperty] private bool   _runTrionWithWindows;
    [ObservableProperty] private bool   _hideConsole;
    [ObservableProperty] private bool   _notificationSound;
    [ObservableProperty] private bool   _stayInTray;
    [ObservableProperty] private bool   _hardwareInfoOptIn;
    [ObservableProperty] private bool   _autoUpdateTrion;
    [ObservableProperty] private bool   _autoUpdateCore;
    [ObservableProperty] private bool   _autoUpdateDatabase;
    [ObservableProperty] private string _selectedLanguage  = "en-US";
    [ObservableProperty] private int    _metricsInterval   = 2;

    // -- Custom tab ------------------------------------------------------------

    [ObservableProperty] private bool   _useCustomServer;
    [ObservableProperty] private int    _customCoreType          = 1;   // index into CustomCoreTypes
    [ObservableProperty] private string _customEmulatorLocation  = string.Empty;
    [ObservableProperty] private string _customDatabaseLocation  = string.Empty;
    [ObservableProperty] private string _customWorldExeName      = string.Empty;
    [ObservableProperty] private string _customLogonExeName      = string.Empty;
    [ObservableProperty] private string _customDatabaseExeName   = string.Empty;

    /// <summary>Display names for the custom core type selector. Index matches AppSettings.SelectedDatabases.</summary>
    public static IReadOnlyList<string> CustomCoreTypes { get; } =
    [
        "TrinityCore 3.3.5a",
        "AzerothCore",
        "CMaNGOS / VMaNGOS",
    ];

    // -- Web server settings (Web Server tab) ----------------------------------

    [ObservableProperty] private string _webServerPort      = "80";
    [ObservableProperty] private bool   _enableHttps;
    [ObservableProperty] private string _httpsPort          = "443";
    [ObservableProperty] private string _sslCertPath        = string.Empty;
    [ObservableProperty] private string _sslKeyPath         = string.Empty;
    [ObservableProperty] private bool   _webServerAutoStart;

    [ObservableProperty] private bool   _isWebServerInstalled;
    [ObservableProperty] private bool   _isWebServerRunning;
    [ObservableProperty] private bool   _isWebServerBusy;
    [ObservableProperty] private string _webServerStatusMessage = string.Empty;
    [ObservableProperty] private string _webServerVersionInfo   = string.Empty;
    [ObservableProperty] private string _webServerInstallPath   = string.Empty;
    [ObservableProperty] private string _webServerProcessInfo   = string.Empty;

    // -- MySQL management (Database tab) ---------------------------------------

    [ObservableProperty] private bool   _isMySqlInstalled;
    [ObservableProperty] private bool   _isMySqlRunning;
    [ObservableProperty] private bool   _isMySqlBusy;
    [ObservableProperty] private string _mySqlStatusMessage = string.Empty;
    [ObservableProperty] private string _mySqlVersionInfo   = string.Empty;
    [ObservableProperty] private string _mySqlInstallPath   = string.Empty;
    [ObservableProperty] private string _mySqlProcessInfo   = string.Empty;

    // -- Game client tab -------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClassicClientConfigured))]
    private string _classicClientPath = string.Empty;
    public bool IsClassicClientConfigured => !string.IsNullOrEmpty(ClassicClientPath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTbcClientConfigured))]
    private string _tbcClientPath = string.Empty;
    public bool IsTbcClientConfigured => !string.IsNullOrEmpty(TbcClientPath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWotlkClientConfigured))]
    private string _wotlkClientPath = string.Empty;
    public bool IsWotlkClientConfigured => !string.IsNullOrEmpty(WotlkClientPath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCataClientConfigured))]
    private string _cataClientPath = string.Empty;
    public bool IsCataClientConfigured => !string.IsNullOrEmpty(CataClientPath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMopClientConfigured))]
    private string _mopClientPath = string.Empty;
    public bool IsMopClientConfigured => !string.IsNullOrEmpty(MopClientPath);

    // -- Database tab ----------------------------------------------------------

    [ObservableProperty] private string _dbHost       = "localhost";
    [ObservableProperty] private string _dbPort       = "3306";
    [ObservableProperty] private string _dbUser       = "phoenix";
    [ObservableProperty] private string _dbPassword   = "phoenix";
    [ObservableProperty] private string _worldDb      = "wotlk_world";
    [ObservableProperty] private string _authDb       = "wotlk_auth";
    [ObservableProperty] private string _charDb       = "wotlk_characters";

    public ILocalizationService         Loc              => _loc;
    public IReadOnlyList<string>        Languages        => _loc.AvailableLanguages;
    public static IReadOnlyList<int>    MetricsIntervals { get; } = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    public SettingsViewModel(ISettingsService settings, IThemeService theme,
                             ILocalizationService loc, IMySqlSetupService mysql,
                             INginxPhpSetupService webServer, IDatabaseService db,
                             IOptionsMonitor<ProcessMonitorOptions> processOpts,
                             IGameClientService gameClient)
    {
        _settings    = settings;
        _theme       = theme;
        _loc         = loc;
        _mysql       = mysql;
        _webServer   = webServer;
        _db          = db;
        _processOpts = processOpts;
        _gameClient  = gameClient;

        _mysql.ProgressChanged     += OnMySqlProgress;
        _mysql.StatusChanged       += OnMySqlStatus;
        _webServer.ProgressChanged += OnWebServerProgress;
        _webServer.StatusChanged   += OnWebServerStatus;

        LoadFromSettings();
        RefreshMySqlState();
        RefreshWebServerState();
    }

    private void LoadFromSettings()
    {
        _isLoading = true;
        try { LoadFromSettingsCore(); }
        finally { _isLoading = false; }
    }

    private void LoadFromSettingsCore()
    {
        var cfg = _settings.Current;

        ServerCrashDetection = cfg.ServerCrashDetection;
        RunServerWithWindows = cfg.RunServerWithWindows;
        RunTrionWithWindows  = cfg.RunWithWindows;
        HideConsole          = cfg.ConsoleHide;
        NotificationSound    = cfg.NotificationSound;
        StayInTray           = cfg.StayInTray;
        HardwareInfoOptIn    = cfg.HardwareInfoOptIn;
        AutoUpdateTrion      = cfg.AutoUpdateTrion;
        AutoUpdateCore       = cfg.AutoUpdateCore;
        AutoUpdateDatabase   = cfg.AutoUpdateDatabase;
        SelectedLanguage     = cfg.Language;
        MetricsInterval      = Math.Clamp(cfg.MetricsIntervalSeconds, 1, 10);

        UseCustomServer         = cfg.LaunchCustomCore;
        CustomCoreType          = Math.Clamp(cfg.SelectedDatabases, 0, CustomCoreTypes.Count - 1);
        CustomEmulatorLocation  = cfg.CustomWorkingDirectory;
        CustomWorldExeName      = cfg.CustomWorldExeName;
        CustomLogonExeName      = cfg.CustomLogonExeName;
        CustomDatabaseExeName   = cfg.DBExeName;

        DbHost     = cfg.DBServerHost;
        DbPort     = cfg.DBServerPort;
        DbUser     = cfg.DBServerUser;
        DbPassword = cfg.DBServerPassword;
        WorldDb    = cfg.WorldDatabase;
        AuthDb     = cfg.AuthDatabase;
        CharDb     = cfg.CharactersDatabase;

        WebServerPort      = cfg.WebServerPort.ToString();
        EnableHttps        = cfg.EnableHttps;
        HttpsPort          = cfg.HttpsPort.ToString();
        SslCertPath        = cfg.SslCertPath;
        SslKeyPath         = cfg.SslKeyPath;
        WebServerAutoStart = cfg.WebServerAutoStart;

        var gcp = cfg.GameClientPaths;
        ClassicClientPath = gcp.GetValueOrDefault("classic", "");
        TbcClientPath     = gcp.GetValueOrDefault("tbc",     "");
        WotlkClientPath   = gcp.GetValueOrDefault("wotlk",   "");
        CataClientPath    = gcp.GetValueOrDefault("cata",    "");
        MopClientPath     = gcp.GetValueOrDefault("mop",     "");
    }

    [RelayCommand]
    private void SwitchTab(SettingsTab tab) => ActiveTab = tab;

    // Re-check live installation state whenever the relevant tab becomes visible.
    // This picks up manually placed/removed files without requiring an app restart.
    partial void OnActiveTabChanged(SettingsTab value)
    {
        if (value == SettingsTab.WebServer)
        {
            RefreshWebServerState();
            // Sync the AppSettings flag to reality in case files were placed manually
            var installed = _webServer.IsInstalled;
            if (_settings.Current.WebServerInstalled != installed)
            {
                _settings.Current.WebServerInstalled = installed;
                _settings.Save();
            }
        }
        else if (value == SettingsTab.Database)
        {
            RefreshMySqlState();
        }
    }

    private void Save()
    {
        var cfg = _settings.Current;

        cfg.ServerCrashDetection = ServerCrashDetection;
        cfg.RunServerWithWindows = RunServerWithWindows;
        cfg.RunWithWindows       = RunTrionWithWindows;
        cfg.ConsoleHide          = HideConsole;
        cfg.NotificationSound    = NotificationSound;
        cfg.StayInTray           = StayInTray;
        cfg.HardwareInfoOptIn    = HardwareInfoOptIn;
        cfg.AutoUpdateTrion      = AutoUpdateTrion;
        cfg.AutoUpdateCore       = AutoUpdateCore;
        cfg.AutoUpdateDatabase   = AutoUpdateDatabase;
        cfg.Theme                = _theme.CurrentTheme;   // persists whatever is active from the title-bar picker
        cfg.Language                = SelectedLanguage;
        cfg.MetricsIntervalSeconds  = Math.Clamp(MetricsInterval, 1, 10);

        // Apply the new interval immediately to the live monitoring workers --
        // they read CurrentValue.RefreshInterval on every loop tick, so this
        // takes effect without requiring a restart.
        _processOpts.CurrentValue.RefreshInterval = TimeSpan.FromSeconds(cfg.MetricsIntervalSeconds);

        cfg.LaunchCustomCore       = UseCustomServer;
        cfg.SelectedDatabases      = CustomCoreType;
        cfg.CustomWorkingDirectory = CustomEmulatorLocation;
        cfg.CustomWorldExeName     = CustomWorldExeName;
        cfg.CustomLogonExeName     = CustomLogonExeName;
        cfg.DBExeName              = CustomDatabaseExeName;

        // Mutual exclusion: enabling custom emulator disables all SPP catalog repacks.
        if (UseCustomServer)
        {
            foreach (var key in cfg.RepackEnabled.Keys.ToList())
                cfg.RepackEnabled[key] = false;
        }

        cfg.DBServerHost     = DbHost;
        cfg.DBServerPort     = DbPort;
        cfg.DBServerUser     = DbUser;
        cfg.DBServerPassword = DbPassword;
        cfg.WorldDatabase    = WorldDb;
        cfg.AuthDatabase     = AuthDb;
        cfg.CharactersDatabase = CharDb;

        cfg.WebServerPort      = int.TryParse(WebServerPort, out int httpPort)
                                 ? Math.Clamp(httpPort, 1, 65535) : 80;
        cfg.EnableHttps        = EnableHttps;
        cfg.HttpsPort          = int.TryParse(HttpsPort, out int httpsPort)
                                 ? Math.Clamp(httpsPort, 1, 65535) : 443;
        cfg.SslCertPath        = SslCertPath;
        cfg.SslKeyPath         = SslKeyPath;
        cfg.WebServerAutoStart = WebServerAutoStart;

        cfg.GameClientPaths["classic"] = ClassicClientPath;
        cfg.GameClientPaths["tbc"]     = TbcClientPath;
        cfg.GameClientPaths["wotlk"]   = WotlkClientPath;
        cfg.GameClientPaths["cata"]    = CataClientPath;
        cfg.GameClientPaths["mop"]     = MopClientPath;

        _settings.Save();

        ApplyWindowsStartupEntry(RunTrionWithWindows);

        _loc.LoadLanguage(SelectedLanguage);
    }

    private static void ApplyWindowsStartupEntry(bool enable)
    {
        const string valueName = "TrionControlPanel";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return;
            if (enable)
            {
                var exe = Environment.ProcessPath
                       ?? Process.GetCurrentProcess().MainModule!.FileName;
                key.SetValue(valueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        StatusMessage = "Testing connection...";
        var (success, message, _) = await _db.TestConnectionAsync(DbHost, DbPort, DbUser, DbPassword);
        StatusMessage = success
            ? $"Connection to {DbHost}:{DbPort} -- {message}"
            : $"Connection failed: {message}";
    }

    [RelayCommand]
    private void BrowseEmulatorLocation()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select Emulator Directory" };
        if (dlg.ShowDialog() == true)
            CustomEmulatorLocation = dlg.FolderName;
    }

    [RelayCommand]
    private void BrowseClientPath(string expansion)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select WoW client folder" };
        if (dlg.ShowDialog() != true) return;
        switch (expansion)
        {
            case "classic": ClassicClientPath = dlg.FolderName; break;
            case "tbc":     TbcClientPath     = dlg.FolderName; break;
            case "wotlk":   WotlkClientPath   = dlg.FolderName; break;
            case "cata":    CataClientPath     = dlg.FolderName; break;
            case "mop":     MopClientPath      = dlg.FolderName; break;
        }
    }

    [RelayCommand]
    private void BrowseDatabaseLocation()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select Database Directory" };
        if (dlg.ShowDialog() == true)
            CustomDatabaseLocation = dlg.FolderName;
    }

    // -- MySQL management commands ---------------------------------------------

    [RelayCommand(CanExecute = nameof(CanMySqlAction))]
    private async Task RepairMySqlAsync()
    {
        IsMySqlBusy = true;
        NotifyMySqlCommandsChanged();
        try   { await _mysql.RepairAsync(); }
        finally { IsMySqlBusy = false; RefreshMySqlState(); NotifyMySqlCommandsChanged(); }
    }

    [RelayCommand(CanExecute = nameof(CanMySqlAction))]
    private async Task ReinstallMySqlAsync()
    {
        IsMySqlBusy = true;
        NotifyMySqlCommandsChanged();
        try   { await _mysql.ReinstallAsync(); }
        finally { IsMySqlBusy = false; RefreshMySqlState(); NotifyMySqlCommandsChanged(); }
    }

    [RelayCommand(CanExecute = nameof(CanMySqlUninstall))]
    private async Task UninstallMySqlAsync()
    {
        IsMySqlBusy = true;
        NotifyMySqlCommandsChanged();
        try   { await _mysql.UninstallAsync(); }
        finally { IsMySqlBusy = false; RefreshMySqlState(); NotifyMySqlCommandsChanged(); }
    }

    private bool CanMySqlAction()   => !IsMySqlBusy && !_mysql.IsRunning;
    private bool CanMySqlUninstall() => IsMySqlInstalled && !IsMySqlBusy && !_mysql.IsRunning;

    private void NotifyMySqlCommandsChanged()
    {
        RepairMySqlCommand.NotifyCanExecuteChanged();
        ReinstallMySqlCommand.NotifyCanExecuteChanged();
        UninstallMySqlCommand.NotifyCanExecuteChanged();
    }

    private void RefreshMySqlState()
    {
        IsMySqlInstalled  = _mysql.IsInstalled;
        IsMySqlRunning    = _mysql.IsProcessRunning;
        MySqlVersionInfo  = $"MySQL {ExternalLinks.MySqlVersion}";
        MySqlInstallPath  = IsMySqlInstalled
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "database")
            : "Not installed";
        MySqlProcessInfo  = IsMySqlRunning && _mysql.ProcessId.HasValue
            ? $"PID {_mysql.ProcessId}  .  {_mysql.Uptime:hh\\:mm\\:ss}"
            : "--";
    }

    private void OnMySqlProgress(string msg)
    {
        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            MySqlStatusMessage = msg;
            IsMySqlBusy        = _mysql.IsRunning;
            NotifyMySqlCommandsChanged();
        });
    }

    private void OnMySqlStatus(bool running)
    {
        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            IsMySqlRunning   = running;
            MySqlProcessInfo = running && _mysql.ProcessId.HasValue
                ? $"PID {_mysql.ProcessId}  .  {_mysql.Uptime:hh\\:mm\\:ss}"
                : "--";
            NotifyMySqlCommandsChanged();
        });
    }

    // -- Web server management commands ----------------------------------------

    [RelayCommand(CanExecute = nameof(CanInstallWebServer))]
    private async Task InstallWebServerAsync()
    {
        IsWebServerBusy = true;
        NotifyWebServerCommandsChanged();
        try   { await _webServer.RunStartupCheckAsync(); }
        finally { IsWebServerBusy = false; RefreshWebServerState(); NotifyWebServerCommandsChanged(); }
    }

    [RelayCommand(CanExecute = nameof(CanWebServerAction))]
    private async Task RepairWebServerAsync()
    {
        IsWebServerBusy = true;
        NotifyWebServerCommandsChanged();
        try   { await _webServer.RepairAsync(); }
        finally { IsWebServerBusy = false; RefreshWebServerState(); NotifyWebServerCommandsChanged(); }
    }

    [RelayCommand(CanExecute = nameof(CanWebServerAction))]
    private async Task ReinstallWebServerAsync()
    {
        IsWebServerBusy = true;
        NotifyWebServerCommandsChanged();
        try   { await _webServer.ReinstallAsync(); }
        finally { IsWebServerBusy = false; RefreshWebServerState(); NotifyWebServerCommandsChanged(); }
    }

    [RelayCommand(CanExecute = nameof(CanWebServerUninstall))]
    private async Task UninstallWebServerAsync()
    {
        IsWebServerBusy = true;
        NotifyWebServerCommandsChanged();
        try   { await _webServer.UninstallAsync(); }
        finally { IsWebServerBusy = false; RefreshWebServerState(); NotifyWebServerCommandsChanged(); }
    }

    [RelayCommand]
    private void GenerateCert()
    {
        _webServer.GenerateSelfSignedCert();
        SslCertPath = _settings.Current.SslCertPath;
        SslKeyPath  = _settings.Current.SslKeyPath;
        StatusMessage = "Self-signed certificate generated.";
    }

    [RelayCommand]
    private void BrowseCert()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Select SSL Certificate (PEM)",
            Filter = "PEM files (*.pem)|*.pem|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            SslCertPath = dlg.FileName;
    }

    [RelayCommand]
    private void BrowseKey()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Select SSL Private Key (PEM)",
            Filter = "PEM files (*.pem)|*.pem|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            SslKeyPath = dlg.FileName;
    }

    private bool CanInstallWebServer()   => !IsWebServerInstalled && !IsWebServerBusy && !_webServer.IsRunning;
    private bool CanWebServerAction()    => IsWebServerInstalled  && !IsWebServerBusy && !_webServer.IsRunning;
    private bool CanWebServerUninstall() => IsWebServerInstalled  && !IsWebServerBusy && !_webServer.IsRunning;

    private void NotifyWebServerCommandsChanged()
    {
        InstallWebServerCommand.NotifyCanExecuteChanged();
        RepairWebServerCommand.NotifyCanExecuteChanged();
        ReinstallWebServerCommand.NotifyCanExecuteChanged();
        UninstallWebServerCommand.NotifyCanExecuteChanged();
    }

    private void RefreshWebServerState()
    {
        IsWebServerInstalled  = _webServer.IsInstalled;
        IsWebServerRunning    = _webServer.IsProcessRunning;
        WebServerVersionInfo  = $"nginx {ExternalLinks.NginxVersion}  +  PHP {ExternalLinks.PhpVersion}";
        WebServerInstallPath  = IsWebServerInstalled
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webserver")
            : "Not installed";
        WebServerProcessInfo  = IsWebServerRunning && _webServer.ProcessId.HasValue
            ? $"PID {_webServer.ProcessId}  .  {_webServer.Uptime:hh\\:mm\\:ss}"
            : "--";
    }

    private void OnWebServerProgress(string msg)
    {
        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            WebServerStatusMessage = msg;
            IsWebServerBusy        = _webServer.IsRunning;
            NotifyWebServerCommandsChanged();
        });
    }

    private void OnWebServerStatus(bool running)
    {
        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            IsWebServerRunning   = running;
            WebServerProcessInfo = running && _webServer.ProcessId.HasValue
                ? $"PID {_webServer.ProcessId}  .  {_webServer.Uptime:hh\\:mm\\:ss}"
                : "--";
            NotifyWebServerCommandsChanged();
        });
    }

    // -- Dispose ---------------------------------------------------------------

    public void Dispose()
    {
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        _mysql.ProgressChanged     -= OnMySqlProgress;
        _mysql.StatusChanged       -= OnMySqlStatus;
        _webServer.ProgressChanged -= OnWebServerProgress;
        _webServer.StatusChanged   -= OnWebServerStatus;
    }
}

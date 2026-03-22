using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Trion.Core.Database;
using Trion.Core.Logging;
using Trion.Core.WebServer;
using Trion.Desktop.Models;
using Trion.Desktop.Services;

namespace Trion.Desktop.ViewModels;

public partial class ServerControlViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService                        _settings;
    private readonly ILocalizationService                    _loc;
    private readonly IMySqlSetupService                      _mysql;
    private readonly INginxPhpSetupService                   _webServer;
    private readonly ILogger<ServerControlViewModel>         _logger;
    private readonly DispatcherTimer                         _pollTimer;

    public ILocalizationService Loc => _loc;

    [ObservableProperty] private bool   _isDatabaseRunning;
    [ObservableProperty] private bool   _isWorldRunning;
    [ObservableProperty] private bool   _isLogonRunning;
    [ObservableProperty] private bool   _isEmulatorInstalled;
    [ObservableProperty] private bool   _isWebServerRunning;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ServerControlViewModel(ISettingsService settings,
                                  ILocalizationService loc,
                                  IMySqlSetupService mysql,
                                  INginxPhpSetupService webServer,
                                  ILogger<ServerControlViewModel> logger)
    {
        _settings  = settings;
        _loc       = loc;
        _mysql     = mysql;
        _webServer = webServer;
        _logger    = logger;

        // Sync initial state from the services
        IsDatabaseRunning    = _mysql.IsProcessRunning;
        IsWebServerRunning   = _webServer.IsProcessRunning;
        IsEmulatorInstalled  = CheckEmulatorInstalled();

        // React immediately to mysqld start/stop
        _mysql.StatusChanged += OnMySqlStatusChanged;

        // React immediately to nginx start/stop
        _webServer.StatusChanged += OnWebServerStatusChanged;

        // Defer process scan to a background thread — Process.GetProcessesByName()
        // can take 1-3 s on first call and would freeze the UI thread.
        _ = Task.Run(() =>
        {
            var active = GetActiveEmulator();
            bool world = IsProcessRunning(active.WorldExeName);
            bool logon = IsProcessRunning(active.LogonExeName);
            App.Current?.Dispatcher.InvokeAsync(() =>
            {
                IsWorldRunning = world;
                IsLogonRunning = logon;
                ToggleWorldCommand.NotifyCanExecuteChanged();
                ToggleLogonCommand.NotifyCanExecuteChanged();
            });
        });

        // Poll every 3 s so the button stays in sync even when the StatusChanged
        // event was fired before this ViewModel was constructed (e.g. auto-start on
        // app launch fires before the user first navigates to this page).
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    private void OnWebServerStatusChanged(bool running)
    {
        App.Current.Dispatcher.InvokeAsync(() =>
        {
            IsWebServerRunning = running;
            StatusMessage      = running ? "Web server started." : "Web server stopped.";
            StartWebServerCommand.NotifyCanExecuteChanged();
            StopWebServerCommand.NotifyCanExecuteChanged();
        });
    }

    private void OnMySqlStatusChanged(bool running)
    {
        App.Current.Dispatcher.InvokeAsync(() =>
        {
            IsDatabaseRunning = running;
            StatusMessage     = running ? "Database server started." : "Database server stopped.";
            StartDatabaseCommand.NotifyCanExecuteChanged();
            StopDatabaseCommand.NotifyCanExecuteChanged();
        });
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        // Fast checks — read from in-memory state, safe to do on UI thread
        IsDatabaseRunning   = _mysql.IsProcessRunning;
        IsWebServerRunning  = _webServer.IsProcessRunning;
        IsEmulatorInstalled = CheckEmulatorInstalled();

        // Process.GetProcessesByName() can take 1-3 s on Windows — run off the UI thread
        _ = Task.Run(() =>
        {
            var active = GetActiveEmulator();
            bool world = IsProcessRunning(active.WorldExeName);
            bool logon = IsProcessRunning(active.LogonExeName);
            App.Current?.Dispatcher.InvokeAsync(() =>
            {
                IsWorldRunning = world;
                IsLogonRunning = logon;
                ToggleWorldCommand.NotifyCanExecuteChanged();
                ToggleLogonCommand.NotifyCanExecuteChanged();
            });
        });
    }

    private bool CheckEmulatorInstalled()
    {
        var cfg = _settings.Current;
        return cfg.CustomInstalled  ||
               cfg.ClassicInstalled ||
               cfg.TBCInstalled     ||
               cfg.WotLKInstalled   ||
               cfg.CataInstalled    ||
               cfg.MOPInstalled;
    }

    private void RefreshWorldLogonStatus()
    {
        var active = GetActiveEmulator();
        IsWorldRunning = IsProcessRunning(active.WorldExeName);
        IsLogonRunning = IsProcessRunning(active.LogonExeName);
    }

    /// <summary>
    /// Returns the paths and executable names for whichever SPP expansion is currently enabled.
    /// Falls back to safe defaults so the server control page never crashes when no expansion
    /// is enabled yet.
    /// </summary>
    private ActiveEmulatorInfo GetActiveEmulator()
    {
        var cfg = _settings.Current;

        if (cfg.LaunchClassicCore)
            return new(
                WorldExeLoc:  cfg.ClassicWorldExeLoc,
                LogonExeLoc:  cfg.ClassicLogonExeLoc,
                WorkingDir:   cfg.ClassicWorkingDirectory,
                WorldExeName: string.IsNullOrWhiteSpace(cfg.ClassicWorldExeName) ? EmulatorProfiles.Classic.WorldExeName : cfg.ClassicWorldExeName,
                LogonExeName: string.IsNullOrWhiteSpace(cfg.ClassicLogonExeName) ? EmulatorProfiles.Classic.LogonExeName : cfg.ClassicLogonExeName);

        if (cfg.LaunchTBCCore)
            return new(
                WorldExeLoc:  cfg.TBCWorldExeLoc,
                LogonExeLoc:  cfg.TBCLogonExeLoc,
                WorkingDir:   cfg.TBCWorkingDirectory,
                WorldExeName: string.IsNullOrWhiteSpace(cfg.TBCWorldExeName) ? EmulatorProfiles.Tbc.WorldExeName : cfg.TBCWorldExeName,
                LogonExeName: string.IsNullOrWhiteSpace(cfg.TBCLogonExeName) ? EmulatorProfiles.Tbc.LogonExeName : cfg.TBCLogonExeName);

        if (cfg.LaunchWotLKCore)
            return new(
                WorldExeLoc:  cfg.WotLKWorldExeLoc,
                LogonExeLoc:  cfg.WotLKLogonExeLoc,
                WorkingDir:   cfg.WotLKWorkingDirectory,
                WorldExeName: string.IsNullOrWhiteSpace(cfg.WotLKWorldExeName) ? EmulatorProfiles.Wotlk.WorldExeName : cfg.WotLKWorldExeName,
                LogonExeName: string.IsNullOrWhiteSpace(cfg.WotLKLogonExeName) ? EmulatorProfiles.Wotlk.LogonExeName : cfg.WotLKLogonExeName);

        if (cfg.LaunchCataCore)
            return new(
                WorldExeLoc:  cfg.CataWorldExeLoc,
                LogonExeLoc:  cfg.CataLogonExeLoc,
                WorkingDir:   cfg.CataWorkingDirectory,
                WorldExeName: string.IsNullOrWhiteSpace(cfg.CataWorldExeName) ? EmulatorProfiles.Cata.WorldExeName : cfg.CataWorldExeName,
                LogonExeName: string.IsNullOrWhiteSpace(cfg.CataLogonExeName) ? EmulatorProfiles.Cata.LogonExeName : cfg.CataLogonExeName);

        if (cfg.LaunchMoPCore)
            return new(
                WorldExeLoc:  cfg.MopWorldExeLoc,
                LogonExeLoc:  cfg.MopLogonExeLoc,
                WorkingDir:   cfg.MopWorkingDirectory,
                WorldExeName: string.IsNullOrWhiteSpace(cfg.MopWorldExeName) ? EmulatorProfiles.Mop.WorldExeName : cfg.MopWorldExeName,
                LogonExeName: string.IsNullOrWhiteSpace(cfg.MopLogonExeName) ? EmulatorProfiles.Mop.LogonExeName : cfg.MopLogonExeName);

        if (cfg.LaunchCustomCore)
            return new(
                WorldExeLoc:  cfg.CustomWorldExeLoc,
                LogonExeLoc:  cfg.CustomLogonExeLoc,
                WorkingDir:   cfg.CustomWorkingDirectory,
                WorldExeName: string.IsNullOrWhiteSpace(cfg.CustomWorldExeName) ? "worldserver" : cfg.CustomWorldExeName,
                LogonExeName: string.IsNullOrWhiteSpace(cfg.CustomLogonExeName) ? "authserver"  : cfg.CustomLogonExeName);

        // No expansion enabled — return safe defaults so nothing crashes
        return new("", "", "", "worldserver", "authserver");
    }

    private readonly record struct ActiveEmulatorInfo(
        string WorldExeLoc,
        string LogonExeLoc,
        string WorkingDir,
        string WorldExeName,
        string LogonExeName);

    private static bool IsProcessRunning(string exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName)) return false;
        var name = Path.GetFileNameWithoutExtension(exeName);
        return Process.GetProcessesByName(name).Length > 0;
    }

    // ── Database ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStartDatabase))]
    private async Task StartDatabaseAsync()
    {
        _logger.LogInformation("User requested database start.");
        StatusMessage = "Starting database...";
        try
        {
            await _mysql.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogException(ex);
            StatusMessage = $"Failed to start database: {ex.Message}";
        }
        StartDatabaseCommand.NotifyCanExecuteChanged();
        StopDatabaseCommand.NotifyCanExecuteChanged();
    }
    private bool CanStartDatabase() => !IsDatabaseRunning && !_mysql.IsRunning;

    [RelayCommand(CanExecute = nameof(CanStopDatabase))]
    private async Task StopDatabaseAsync()
    {
        _logger.LogInformation("User requested database stop.");
        StatusMessage = "Stopping database...";
        try
        {
            await _mysql.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogException(ex);
            StatusMessage = $"Failed to stop database: {ex.Message}";
        }
        StartDatabaseCommand.NotifyCanExecuteChanged();
        StopDatabaseCommand.NotifyCanExecuteChanged();
    }
    private bool CanStopDatabase() => IsDatabaseRunning;

    // ── Per-server crash-monitor state ────────────────────────────────────────

    private volatile int _worldProcessId;
    private volatile int _logonProcessId;
    private CancellationTokenSource? _worldMonitorCts;
    private CancellationTokenSource? _logonMonitorCts;

    private const int MaxCrashRestarts = 5;

    // ── World ─────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStartWorld))]
    private Task StartWorldAsync() => StartWorldCoreAsync(crashCount: 0);
    private bool CanStartWorld() => !IsWorldRunning;

    private async Task StartWorldCoreAsync(int crashCount)
    {
        if (!IsDatabaseRunning)
        {
            _logger.LogWarning("World server start blocked — database is not running.");
            StatusMessage = "Database must be running before starting World server.";
            return;
        }

        var active = GetActiveEmulator();
        _logger.LogInformation("Starting World server (crash attempt {Attempt}). Exe: {Exe}", crashCount, active.WorldExeLoc);

        var proc = await LaunchProcessAsync(active.WorldExeLoc, active.WorkingDir);
        if (proc is null)
        {
            StatusMessage = "World server executable not found.";
            _logger.LogError("World server launch failed — executable not found or path is empty.");
            return;
        }

        _worldProcessId = proc.Id;
        IsWorldRunning  = true;
        StatusMessage   = crashCount == 0 ? "World server started." : $"World server restarted (attempt {crashCount}/{MaxCrashRestarts}).";
        _logger.LogInformation("World server launched (PID {Pid}).", proc.Id);

        if (_settings.Current.ServerCrashDetection)
            BeginServerMonitor(proc, isWorld: true, crashCount);

        StartWorldCommand.NotifyCanExecuteChanged();
        StopWorldCommand.NotifyCanExecuteChanged();
        ToggleWorldCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStopWorld))]
    private async Task StopWorldAsync()
    {
        _logger.LogInformation("User requested World server stop.");

        // Cancel the crash monitor BEFORE killing the process so it knows
        // this exit is intentional and does NOT attempt a restart.
        _worldProcessId = 0;
        _worldMonitorCts?.Cancel();
        _worldMonitorCts?.Dispose();
        _worldMonitorCts = null;

        await GracefulStopAsync(GetActiveEmulator().WorldExeName);
        IsWorldRunning = false;
        StatusMessage  = "World server stopped.";
        _logger.LogInformation("World server stopped.");
        StartWorldCommand.NotifyCanExecuteChanged();
        StopWorldCommand.NotifyCanExecuteChanged();
        ToggleWorldCommand.NotifyCanExecuteChanged();
    }
    private bool CanStopWorld() => IsWorldRunning;

    // ── Logon ─────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStartLogon))]
    private Task StartLogonAsync() => StartLogonCoreAsync(crashCount: 0);
    private bool CanStartLogon() => !IsLogonRunning;

    private async Task StartLogonCoreAsync(int crashCount)
    {
        if (!IsDatabaseRunning)
        {
            _logger.LogWarning("Logon server start blocked — database is not running.");
            StatusMessage = "Database must be running before starting Logon server.";
            return;
        }

        var active = GetActiveEmulator();
        _logger.LogInformation("Starting Logon server (crash attempt {Attempt}). Exe: {Exe}", crashCount, active.LogonExeLoc);

        var proc = await LaunchProcessAsync(active.LogonExeLoc, active.WorkingDir);
        if (proc is null)
        {
            StatusMessage = "Logon server executable not found.";
            _logger.LogError("Logon server launch failed — executable not found or path is empty.");
            return;
        }

        _logonProcessId = proc.Id;
        IsLogonRunning  = true;
        StatusMessage   = crashCount == 0 ? "Logon server started." : $"Logon server restarted (attempt {crashCount}/{MaxCrashRestarts}).";
        _logger.LogInformation("Logon server launched (PID {Pid}).", proc.Id);

        if (_settings.Current.ServerCrashDetection)
            BeginServerMonitor(proc, isWorld: false, crashCount);

        StartLogonCommand.NotifyCanExecuteChanged();
        StopLogonCommand.NotifyCanExecuteChanged();
        ToggleLogonCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStopLogon))]
    private async Task StopLogonAsync()
    {
        _logger.LogInformation("User requested Logon server stop.");

        _logonProcessId = 0;
        _logonMonitorCts?.Cancel();
        _logonMonitorCts?.Dispose();
        _logonMonitorCts = null;

        await GracefulStopAsync(GetActiveEmulator().LogonExeName);
        IsLogonRunning = false;
        StatusMessage  = "Logon server stopped.";
        _logger.LogInformation("Logon server stopped.");
        StartLogonCommand.NotifyCanExecuteChanged();
        StopLogonCommand.NotifyCanExecuteChanged();
        ToggleLogonCommand.NotifyCanExecuteChanged();
    }
    private bool CanStopLogon() => IsLogonRunning;

    // ── Crash monitor ─────────────────────────────────────────────────────────

    private void BeginServerMonitor(Process proc, bool isWorld, int crashCount)
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

        _ = Task.Run(() => ServerMonitorLoopAsync(proc, isWorld, crashCount, cts.Token));
    }

    private async Task ServerMonitorLoopAsync(Process proc, bool isWorld, int crashCount,
                                              CancellationToken ct)
    {
        string name = isWorld ? "World" : "Logon";
        int    pid  = proc.Id;

        // Poll every 2 s — same reliable pattern as MySqlSetupService.MonitorLoopAsync.
        // WaitForExitAsync is NOT used because it can throw InvalidOperationException when
        // the process was started with UseShellExecute = true (handle not accessible for
        // async waiting); the broad catch would then silently exit the monitor.
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(2_000, ct); }
            catch (OperationCanceledException) { return; }  // intentional stop

            // PID cleared by Stop command → intentional exit.
            if ((isWorld ? _worldProcessId : _logonProcessId) == 0) return;

            bool alive;
            try   { alive = !Process.GetProcessById(pid).HasExited; }
            catch (ArgumentException) { alive = false; }   // PID no longer exists

            if (!alive) break;  // unexpected exit detected
        }

        if (ct.IsCancellationRequested) return;

        // Final guard: if Stop was called in the narrow window between the last poll
        // and the break, treat it as intentional.
        if ((isWorld ? _worldProcessId : _logonProcessId) == 0) return;

        _logger.LogWarning("{Name} server (PID {Pid}) exited unexpectedly.", name, pid);

        // Update UI state on the dispatcher thread.
        await App.Current.Dispatcher.InvokeAsync(() =>
        {
            if (isWorld) IsWorldRunning = false;
            else         IsLogonRunning = false;
        });

        if (!_settings.Current.ServerCrashDetection) return;

        if (crashCount >= MaxCrashRestarts)
        {
            _logger.LogWarning("{Name} server crashed {Max} times in a row — auto-restart disabled.", name, MaxCrashRestarts);
            await App.Current.Dispatcher.InvokeAsync(() =>
            {
                StatusMessage = $"{name} server crashed {MaxCrashRestarts} times — giving up.";
                if (isWorld) { _worldProcessId = 0; StartWorldCommand.NotifyCanExecuteChanged(); StopWorldCommand.NotifyCanExecuteChanged(); ToggleWorldCommand.NotifyCanExecuteChanged(); }
                else         { _logonProcessId = 0; StartLogonCommand.NotifyCanExecuteChanged(); StopLogonCommand.NotifyCanExecuteChanged(); ToggleLogonCommand.NotifyCanExecuteChanged(); }
            });
            return;
        }

        int attempt = crashCount + 1;
        _logger.LogInformation("{Name} server — restart attempt {Attempt}/{Max}.", name, attempt, MaxCrashRestarts);

        await App.Current.Dispatcher.InvokeAsync(() =>
            StatusMessage = $"{name} server crashed — restart attempt {attempt}/{MaxCrashRestarts}...");

        // Brief pause before restart (gives the OS time to release ports/files).
        await Task.Delay(2_000, ct).ConfigureAwait(false);

        // Re-launch on the dispatcher so observable property setters run on the UI thread.
        await App.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (isWorld) await StartWorldCoreAsync(attempt);
            else         await StartLogonCoreAsync(attempt);
        });
    }

    // ── Toggle commands (used by the single card button) ──────────────────────
    // Command must be bound directly on the <Button> element — WPF does not
    // reliably resolve Bindings placed inside a <Style.Setter> or
    // <DataTrigger.Setter>, so we use one command that switches behaviour on
    // the current running state instead of swapping commands via triggers.

    [RelayCommand]
    private async Task ToggleDatabaseAsync()
    {
        if (IsDatabaseRunning) await StopDatabaseAsync();
        else                   await StartDatabaseAsync();
    }

    [RelayCommand(CanExecute = nameof(CanToggleWorld))]
    private async Task ToggleWorldAsync()
    {
        if (IsWorldRunning) await StopWorldAsync();
        else                await StartWorldAsync();
    }
    private bool CanToggleWorld() => IsWorldRunning || IsEmulatorInstalled;

    [RelayCommand(CanExecute = nameof(CanToggleLogon))]
    private async Task ToggleLogonAsync()
    {
        if (IsLogonRunning) await StopLogonAsync();
        else                await StartLogonAsync();
    }
    private bool CanToggleLogon() => IsLogonRunning || IsEmulatorInstalled;

    // ── Web server (nginx + PHP) ──────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStartWebServer))]
    private async Task StartWebServerAsync()
    {
        _logger.LogInformation("User requested web server start.");
        StatusMessage = "Starting web server...";
        try
        {
            await _webServer.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogException(ex);
            StatusMessage = $"Failed to start web server: {ex.Message}";
        }
        StartWebServerCommand.NotifyCanExecuteChanged();
        StopWebServerCommand.NotifyCanExecuteChanged();
    }
    private bool CanStartWebServer() => !IsWebServerRunning && !_webServer.IsRunning;

    [RelayCommand(CanExecute = nameof(CanStopWebServer))]
    private async Task StopWebServerAsync()
    {
        _logger.LogInformation("User requested web server stop.");
        StatusMessage = "Stopping web server...";
        try
        {
            await _webServer.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogException(ex);
            StatusMessage = $"Failed to stop web server: {ex.Message}";
        }
        StartWebServerCommand.NotifyCanExecuteChanged();
        StopWebServerCommand.NotifyCanExecuteChanged();
    }
    private bool CanStopWebServer() => IsWebServerRunning;

    [RelayCommand]
    private async Task ToggleWebServerAsync()
    {
        if (IsWebServerRunning) await StopWebServerAsync();
        else                    await StartWebServerAsync();
    }

    // ── Start All / Stop All ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task StartAllAsync()
    {
        await StartDatabaseAsync();
        await Task.Delay(1500);
        await StartWorldAsync();
        await StartLogonAsync();
    }

    [RelayCommand]
    private async Task StopAllAsync()
    {
        await StopWorldAsync();
        await StopLogonAsync();
        await Task.Delay(500);
        await StopDatabaseAsync();
    }

    [RelayCommand]
    private void Refresh() => RefreshWorldLogonStatus();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task<Process?> LaunchProcessAsync(string exePath, string workingDir)
    {
        bool hide = _settings.Current.ConsoleHide;

        return Task.Run<Process?>(() =>
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return null;

            var psi = new ProcessStartInfo
            {
                FileName         = exePath,
                WorkingDirectory = workingDir,
                UseShellExecute  = !hide,
                CreateNoWindow   = hide,
            };
            return Process.Start(psi);
        });
    }

    /// <summary>
    /// Sends Ctrl+C to all processes matching <paramref name="exeName"/>,
    /// waits 10 s for graceful exit, then force-kills survivors.
    /// </summary>
    private static Task GracefulStopAsync(string exeName)
    {
        return Task.Run(async () =>
        {
            var name  = Path.GetFileNameWithoutExtension(exeName);
            var procs = Process.GetProcessesByName(name);
            if (procs.Length == 0) return;

            foreach (var p in procs)
            {
                try { p.CloseMainWindow(); } catch { /* headless process — ignore */ }
            }

            await Task.Delay(10_000);

            foreach (var p in procs)
            {
                try
                {
                    if (!p.HasExited) p.Kill(entireProcessTree: true);
                }
                catch { /* already gone */ }
            }
        });
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _pollTimer.Stop();
        _mysql.StatusChanged      -= OnMySqlStatusChanged;
        _webServer.StatusChanged  -= OnWebServerStatusChanged;
        _worldMonitorCts?.Cancel();
        _worldMonitorCts?.Dispose();
        _logonMonitorCts?.Cancel();
        _logonMonitorCts?.Dispose();
    }
}

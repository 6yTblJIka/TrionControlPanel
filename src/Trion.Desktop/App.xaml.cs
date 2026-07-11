using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows;
using Trion.Core.Abstractions.Services;
using Trion.Core.Database;
using Trion.Core.Logging;
using Trion.Core.Monitoring;
using Trion.Core.WebServer;
using Trion.Desktop.Infrastructure.Extensions;
using Trion.Desktop.Infrastructure.Helpers;
using Trion.Desktop.Models;
using Trion.Desktop.Services;
using Trion.Desktop.ViewModels;
using Trion.Desktop.Views;
using Trion.Platform;
#pragma warning disable CA2000 // services are disposed by the host

namespace Trion.Desktop;

public partial class App : Application
{
    public static IHost AppHost { get; private set; } = null!;

    private static Mutex? _instanceMutex;
    private const string MutexName = "Global\\TrionControlPanel_SingleInstance";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance guard  -  must be the very first check.
        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            TrionMessageBox.Show(
                "Trion Control Panel is already running.\n\nCheck the system tray if the window is not visible.",
                "Trion  -  Already Running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        // Prevent WPF from shutting down when the login window closes before
        // the main window has been shown.  We call Shutdown() explicitly in OnExit.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        AppHost = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddTrionLogger();
            })
            .ConfigureServices((_, services) =>
            {
                // Core services
                services.AddSingleton<ISettingsService, SettingsService>();
                services.AddSingleton<IThemeService, ThemeService>();
                services.AddSingleton<ILocalizationService, LocalizationService>();
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<INotificationService, NotificationService>();
                services.AddSingleton<IMySqlSetupService, MySqlSetupService>();
                services.AddSingleton<INginxPhpSetupService, NginxPhpSetupService>();
                services.AddSingleton<IApiKeyService, ApiKeyService>();
                services.AddSingleton<IAccountService, AccountService>();
                services.AddSingleton<INewsService, NewsService>();
                services.AddSingleton<IDonationGoalService, DonationGoalService>();
                services.AddSingleton<ISupportersService, SupportersService>();
                services.AddSingleton<IEmulatorInstallerService, EmulatorInstallerService>();
                services.AddSingleton<INetworkService, NetworkService>();
                services.AddSingleton<IDdnsUpdateService, DdnsUpdateService>();
                services.AddSingleton<IDatabaseService, DatabaseService>();
                services.AddSingleton<IAutoUpdater, DesktopAutoUpdater>();
                services.AddSingleton<IBugReportService, BugReportService>();

                // Platform + monitoring (OS-aware, cross-platform)
                services.AddPlatformServices();
                services.AddMonitoringServices();
                services.AddHostedService<HeartbeatService>();
                services.AddHostedService<RepackUpdateService>();

                // Wire the user's chosen metrics interval into ProcessMonitorOptions.
                // Configure<ISettingsService> runs lazily when CurrentValue is first accessed
                // (always after settings.Load() which is called before StartAsync).
                services.AddOptions<ProcessMonitorOptions>()
                    .Configure<ISettingsService>((opts, settings) =>
                        opts.RefreshInterval = TimeSpan.FromSeconds(
                            Math.Clamp(settings.Current.MetricsIntervalSeconds, 1, 60)));

                // Register all ViewModels, Views, and Services via extension
                services.RegisterViewModels();
                services.RegisterViews();
                services.RegisterTrayService();
                services.RegisterGameClientServices();
            })
            .Build();

        await AppHost.StartAsync();

        var logger = AppHost.Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Application started. OS: {OS}  CLR: {CLR}", Environment.OSVersion, Environment.Version);

        // Load settings and apply theme/language
        var settings = AppHost.Services.GetRequiredService<ISettingsService>();
        settings.Load();
        logger.LogInformation("Settings loaded. Theme: {Theme}  Lang: {Lang}", settings.Current.Theme, settings.Current.Language);

        // Initialise the API key system: load cached guest key or register a new install
        var apiKeys = AppHost.Services.GetRequiredService<IApiKeyService>();
        _ = Task.Run(async () =>
        {
            try   { await apiKeys.InitialiseAsync(); }
            catch (Exception ex) { logger.LogException(ex, "App.ApiKeyInit"); }
        });

        var theme = AppHost.Services.GetRequiredService<IThemeService>();
        theme.SetTheme(settings.Current.Theme);

        var loc = AppHost.Services.GetRequiredService<ILocalizationService>();
        loc.LoadLanguage(settings.Current.Language);
        TrionMessageBox.SetLocalization(loc);

        // Resolve the MySQL service early — IsInstalled is needed by the port check to
        // distinguish a pre-existing system MySQL (not a conflict) from an orphan process.
        var mysql = AppHost.Services.GetRequiredService<IMySqlSetupService>();

        // Pre-flight port + process check
        // Must run before any windows are shown so we can block or warn without
        // a partially-initialised UI in the background.
        var portCheck = StartupPortCheck.Run(settings.Current, mysql.IsInstalled);

        if (portCheck.SystemMySqlDetected)
            logger.LogInformation(
                "System MySQL detected on port {Port} — portable MySQL will not be installed.",
                portCheck.MySqlPort);

        if (portCheck.AnyPortBlocked)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Trion cannot start because the following ports are already in use:\n");
            if (portCheck.MySqlPortBlocked)
                sb.AppendLine($"  - Port {portCheck.MySqlPort}  (MySQL)");
            if (portCheck.HttpPortBlocked)
                sb.AppendLine($"  - Port {portCheck.HttpPort}  (HTTP / nginx)");
            if (portCheck.HttpsPortBlocked)
                sb.AppendLine($"  - Port {portCheck.HttpsPort}  (HTTPS / nginx)");
            sb.AppendLine("\nFree the listed ports and try again.");

            logger.LogError("Startup blocked  -  ports in use: MySQL={MySql} HTTP={Http} HTTPS={Https}",
                portCheck.MySqlPortBlocked, portCheck.HttpPortBlocked, portCheck.HttpsPortBlocked);

            TrionMessageBox.Show(sb.ToString(), "Trion  -  Cannot Start",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        if (portCheck.OrphanMySql.Length > 0)
        {
            var names = string.Join(", ",
                portCheck.OrphanMySql.Select(pr => $"{pr.ProcessName} (PID {pr.Id})"));

            logger.LogWarning("Orphan MySQL process(es) detected before startup: {Processes}", names);

            var answer = TrionMessageBox.Show(
                $"A MySQL process is already running outside of Trion:\n  {names}\n\n" +
                $"Port {portCheck.MySqlPort} is currently free, but running multiple MySQL instances " +
                $"on the same machine can cause performance and I/O problems.\n\n" +
                $"Do you want Trion to kill this process before continuing?",
                "Trion  -  MySQL Already Running",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer == MessageBoxResult.Yes)
            {
                foreach (var proc in portCheck.OrphanMySql)
                {
                    try
                    {
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(3000);
                        logger.LogInformation("Killed orphan MySQL process {Name} PID {Id}",
                            proc.ProcessName, proc.Id);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to kill MySQL process PID {Id}", proc.Id);
                    }
                    finally { proc.Dispose(); }
                }
            }
            else
            {
                foreach (var proc in portCheck.OrphanMySql) proc.Dispose();
            }
        }

        // Show login window if the user has no saved session and hasn't chosen guest mode
        var account = AppHost.Services.GetRequiredService<IAccountService>();
        if (!account.IsLoggedIn && !account.IsGuest)
        {
            var loginWindow = AppHost.Services.GetRequiredService<LoginWindow>();
            loginWindow.ShowDialog();   // blocks until user logs in or skips
        }

        var tray = AppHost.Services.GetRequiredService<ITrayService>();

        var window = AppHost.Services.GetRequiredService<MainWindow>();
        tray.Initialize(window);
        window.Closed += (_, _) => Shutdown();

        window.Show();

        // MySQL detection / installation runs on a background thread after the
        // window is visible.  Progress is broadcast via IMySqlSetupService.ProgressChanged;
        // DashboardViewModel subscribes and shows the banner + event log entries.
        _ = Task.Run(async () =>
        {
            try   { await mysql.RunStartupCheckAsync(); }
            catch (Exception ex)
            { logger.LogException(ex, "App.MySqlStartup"); }
        });

        // Web server (nginx + PHP) -- only starts when WebServerAutoStart is enabled.
        // Users opt in via Settings so those who don't need a web server are unaffected.
        if (settings.Current.WebServerAutoStart)
        {
            var webServer = AppHost.Services.GetRequiredService<INginxPhpSetupService>();
            _ = Task.Run(async () =>
            {
                try   { await webServer.RunStartupCheckAsync(); }
                catch (Exception ex) { logger.LogException(ex, "App.WebServerStartup"); }
            });
        }

        // Auto-start world/logon servers once MySQL is ready, when the setting is enabled.
        // ServerControlViewModel.GetActiveEmulator() resolves the correct exe for both
        // traditional setups and catalog repacks (SSP), so one path covers all cases.
        if (settings.Current.RunServerWithWindows)
        {
            var serverVm = AppHost.Services.GetRequiredService<ServerControlViewModel>();

            void StartServersIfNeeded()
            {
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    if (!serverVm.IsWorldRunning)
                        await serverVm.StartWorldCommand.ExecuteAsync(null);
                    if (!serverVm.IsLogonRunning)
                        await serverVm.StartLogonCommand.ExecuteAsync(null);
                });
            }

            if (mysql.IsRunning || mysql.IsUsingSystemMySQL)
            {
                StartServersIfNeeded();
            }
            else
            {
                void OnMySqlStatusChanged(bool running)
                {
                    if (!running) return;
                    mysql.StatusChanged -= OnMySqlStatusChanged;
                    StartServersIfNeeded();
                }
                mysql.StatusChanged += OnMySqlStatusChanged;
            }
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        // Dispose the tray icon immediately so it doesn't linger in the taskbar
        AppHost.Services.GetRequiredService<ITrayService>().Dispose();

        var logger = AppHost.Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Application shutdown initiated.");

        // Gracefully stop MySQL before tearing down the host
        var mysql = AppHost.Services.GetRequiredService<IMySqlSetupService>();
        try { await mysql.StopAsync(); }
        catch (Exception ex) { logger.LogException(ex, "App.OnExit.StopMySQL"); }

        // Gracefully stop web server (nginx + PHP-CGI) if it was running
        var webServer = AppHost.Services.GetRequiredService<INginxPhpSetupService>();
        try { await webServer.StopAsync(); }
        catch (Exception ex) { logger.LogException(ex, "App.OnExit.StopWebServer"); }

        var settings = AppHost.Services.GetRequiredService<ISettingsService>();

        // If the user didn't choose "Remember me", wipe the session token so
        // they are prompted to sign in again on next launch (drops to guest mode).
        var cfg = settings.Current;
        if (!string.IsNullOrEmpty(cfg.AccountToken) && !cfg.RememberMe)
        {
            cfg.AccountToken    = "";
            cfg.LoginSkipped    = true;   // return to guest on next launch
        }

        settings.Save();
        logger.LogInformation("Settings saved. Shutting down host.");

        await AppHost.StopAsync();
        AppHost.Dispose();

        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Timers;
using Trion.Core.Database;
using Trion.Core.Monitoring;
using Trion.Core.WebServer;
using Trion.Desktop.Models;
using Trion.Desktop.Services;
using Timer = System.Timers.Timer;

namespace Trion.Desktop.ViewModels;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService      _settings;
    private readonly IMySqlSetupService    _mysql;
    private readonly INginxPhpSetupService _webServer;
    private readonly ISystemMetricsService _metrics;
    private readonly ILocalizationService  _loc;
    private readonly INewsService          _news;
    private readonly Timer                 _serverTimer;
    private readonly Timer                 _uptimeTimer;

    public ILocalizationService Loc => _loc;

    // ── MySQL setup banner ────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isSetupRunning;
    [ObservableProperty] private string _setupStatusMessage = string.Empty;

    // ── Server status ────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isDatabaseRunning;
    [ObservableProperty] private bool _isWorldRunning;
    [ObservableProperty] private bool _isLogonRunning;
    [ObservableProperty] private bool _isWebServerRunning;

    [ObservableProperty] private string _databaseUptime  = "00:00:00";
    [ObservableProperty] private string _worldUptime     = "00:00:00";
    [ObservableProperty] private string _logonUptime     = "00:00:00";
    [ObservableProperty] private string _webServerUptime = "00:00:00";

    [ObservableProperty] private int _databasePid;
    [ObservableProperty] private int _worldPid;
    [ObservableProperty] private int _logonPid;
    [ObservableProperty] private int _webServerPid;

    // ── Machine metrics ───────────────────────────────────────────────────────

    public string CpuCores { get; } = $"{Environment.ProcessorCount} Cores";

    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private double _ramPercent;
    [ObservableProperty] private string _ramUsed   = "—";
    [ObservableProperty] private string _ramTotal  = "—";
    [ObservableProperty] private string _diskRead  = "—";
    [ObservableProperty] private string _diskWrite = "—";
    [ObservableProperty] private string _networkRx = "—";
    [ObservableProperty] private string _networkTx = "—";

    // Sparkline history (newest value appended; oldest dropped when over capacity)
    private const int HistoryCapacity = 60;   // 60 × 2 s = 2 min window

    private readonly List<double> _cpuBuf       = new(HistoryCapacity + 1);
    private readonly List<double> _ramBuf        = new(HistoryCapacity + 1);
    private readonly List<double> _diskReadBuf  = new(HistoryCapacity + 1);
    private readonly List<double> _diskWriteBuf = new(HistoryCapacity + 1);
    private readonly List<double> _netRecvBuf   = new(HistoryCapacity + 1);
    private readonly List<double> _netSendBuf   = new(HistoryCapacity + 1);

    [ObservableProperty] private double[] _cpuHistory        = [];
    [ObservableProperty] private double[] _ramHistory        = [];
    [ObservableProperty] private double[] _diskReadHistory  = [];
    [ObservableProperty] private double[] _diskWriteHistory = [];
    [ObservableProperty] private double[] _netRecvHistory   = [];
    [ObservableProperty] private double[] _netSendHistory   = [];

    // ── Loading state ─────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isLoading = true;
    private bool _metricsReady;
    private bool _serverStatusReady;

    private void CheckLoadingComplete()
    {
        // Must already be on the UI thread (all callers dispatch here first)
        if (_metricsReady && _serverStatusReady)
            IsLoading = false;
    }

    // ── News ─────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isNewsLoading = true;
    [ObservableProperty] private bool _isNewsEmpty;
    public ObservableCollection<NewsItem> NewsItems { get; } = [];

    // ── Server process tracking ───────────────────────────────────────────────

    private DateTime? _dbStartTime;
    private DateTime? _worldStartTime;
    private DateTime? _logonStartTime;
    private DateTime? _webServerStartTime;

    // ── Construction ──────────────────────────────────────────────────────────

    public DashboardViewModel(ISettingsService settings, IMySqlSetupService mysql,
                              INginxPhpSetupService webServer,
                              ISystemMetricsService metrics, ILocalizationService loc,
                              INewsService news)
    {
        _settings  = settings;
        _mysql     = mysql;
        _webServer = webServer;
        _metrics   = metrics;
        _loc       = loc;
        _news      = news;

        // MySQL setup banner
        IsSetupRunning     = _mysql.IsRunning;
        SetupStatusMessage = _mysql.LastMessage;
        _mysql.ProgressChanged += OnMySqlProgress;
        _mysql.StatusChanged   += OnMySqlStatusChanged;

        // Web server — sync initial state
        _webServer.StatusChanged += OnWebServerStatusChanged;

        // Machine metrics — handle late subscription (first sample may already be ready)
        _metrics.Updated += OnMetricsUpdated;
        if (_metrics.Last is { } last)
            ApplyMetrics(last);

        // Server status polled on a 3-second timer (process checks are cheap)
        _serverTimer = new Timer(3_000) { AutoReset = true };
        _serverTimer.Elapsed += (_, _) => RefreshServerStatus();
        _serverTimer.Start();

        // Uptime strings updated every second (just string formatting, no process scan)
        _uptimeTimer = new Timer(1_000) { AutoReset = true };
        _uptimeTimer.Elapsed += (_, _) => TickUptime();
        _uptimeTimer.Start();

        // Defer initial process scan to a background thread — Process.GetProcessesByName()
        // can take 1-3 s on first call and would freeze the UI thread.
        _ = Task.Run(RefreshServerStatus);
        _ = LoadNewsAsync();
    }

    // ── News loading ──────────────────────────────────────────────────────────

    private async Task LoadNewsAsync()
    {
        IsNewsLoading = true;
        var items = await _news.GetLatestAsync(6);
        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            NewsItems.Clear();
            foreach (var item in items)
                NewsItems.Add(item);
            IsNewsLoading = false;
            IsNewsEmpty   = NewsItems.Count == 0;
        });
    }

    // ── MySQL banner / status ─────────────────────────────────────────────────

    private void OnMySqlProgress(string message)
    {
        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            SetupStatusMessage = message;
            IsSetupRunning     = _mysql.IsRunning;
        });
    }

    private void OnMySqlStatusChanged(bool running)
    {
        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            IsDatabaseRunning = running;
            DatabasePid       = _mysql.ProcessId ?? 0;
            _dbStartTime      = _mysql.StartTime;
            DatabaseUptime    = FormatUptime(_dbStartTime);
        });
    }

    private void OnWebServerStatusChanged(bool running)
    {
        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            IsWebServerRunning   = running;
            WebServerPid         = _webServer.ProcessId ?? 0;
            _webServerStartTime  = _webServer.StartTime;
            WebServerUptime      = FormatUptime(_webServerStartTime);
        });
    }

    // ── Machine metrics ───────────────────────────────────────────────────────

    private void OnMetricsUpdated(MachineMetrics m)
        => App.Current?.Dispatcher.InvokeAsync(() => ApplyMetrics(m));

    private void ApplyMetrics(MachineMetrics m)
    {
        double ramPct = m.RamTotalBytes > 0
            ? (double)m.RamUsedBytes / m.RamTotalBytes * 100.0
            : 0.0;

        CpuPercent = Math.Round(m.CpuPercent, 1);

        if (!_metricsReady)
        {
            _metricsReady = true;
            CheckLoadingComplete();
        }
        RamPercent = Math.Round(ramPct, 1);
        RamUsed    = MetricsFormatter.FormatBytes(m.RamUsedBytes);
        RamTotal   = MetricsFormatter.FormatBytes(m.RamTotalBytes);
        DiskRead   = MetricsFormatter.FormatBps(m.DiskReadBytesPerSec);
        DiskWrite  = MetricsFormatter.FormatBps(m.DiskWriteBytesPerSec);
        NetworkRx  = MetricsFormatter.FormatBps(m.NetworkRxBytesPerSec);
        NetworkTx  = MetricsFormatter.FormatBps(m.NetworkTxBytesPerSec);

        Push(_cpuBuf,       CpuPercent);
        Push(_ramBuf,       RamPercent);
        Push(_diskReadBuf,  m.DiskReadBytesPerSec);
        Push(_diskWriteBuf, m.DiskWriteBytesPerSec);
        Push(_netRecvBuf,   m.NetworkRxBytesPerSec);
        Push(_netSendBuf,   m.NetworkTxBytesPerSec);

        CpuHistory       = [.. _cpuBuf];
        RamHistory       = [.. _ramBuf];
        DiskReadHistory  = [.. _diskReadBuf];
        DiskWriteHistory = [.. _diskWriteBuf];
        NetRecvHistory   = [.. _netRecvBuf];
        NetSendHistory   = [.. _netSendBuf];
    }

    private static void Push(List<double> buf, double value)
    {
        buf.Add(value);
        if (buf.Count > HistoryCapacity)
            buf.RemoveAt(0);
    }

    // ── Server status ─────────────────────────────────────────────────────────

    private void TickUptime()
    {
        var db        = FormatUptime(_dbStartTime);
        var world     = FormatUptime(_worldStartTime);
        var logon     = FormatUptime(_logonStartTime);
        var webServer = FormatUptime(_webServerStartTime);
        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            DatabaseUptime  = db;
            WorldUptime     = world;
            LogonUptime     = logon;
            WebServerUptime = webServer;
        });
    }

    private void RefreshServerStatus()
    {
        var cfg = _settings.Current;

        // Database: use the service (PID-based) to avoid false positives from any
        // system-installed mysqld that is unrelated to the one Trion launched.
        bool dbRunning = _mysql.IsProcessRunning;
        int  dbPid     = _mysql.ProcessId ?? 0;
        if (!dbRunning)
            _dbStartTime = null;
        else
            _dbStartTime ??= _mysql.StartTime;

        // Web server: same PID-based approach via the service
        bool wsRunning = _webServer.IsProcessRunning;
        int  wsPid     = _webServer.ProcessId ?? 0;
        if (!wsRunning)
            _webServerStartTime = null;
        else
            _webServerStartTime ??= _webServer.StartTime;

        bool    worldRunning = false;
        int     worldPid     = 0;
        bool    logonRunning = false;
        int     logonPid     = 0;

        CheckProcess(cfg.WotLKWorldExeName.Length > 0 ? cfg.WotLKWorldExeName : "worldserver",
            ref worldRunning, ref worldPid, ref _worldStartTime);
        CheckProcess(cfg.WotLKLogonExeName.Length > 0 ? cfg.WotLKLogonExeName : "authserver",
            ref logonRunning, ref logonPid, ref _logonStartTime);

        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            IsDatabaseRunning  = dbRunning;
            IsWorldRunning     = worldRunning;
            IsLogonRunning     = logonRunning;
            IsWebServerRunning = wsRunning;
            DatabasePid        = dbPid;
            WorldPid           = worldPid;
            LogonPid           = logonPid;
            WebServerPid       = wsPid;
            DatabaseUptime     = FormatUptime(_dbStartTime);
            WorldUptime        = FormatUptime(_worldStartTime);
            LogonUptime        = FormatUptime(_logonStartTime);
            WebServerUptime    = FormatUptime(_webServerStartTime);

            if (!_serverStatusReady)
            {
                _serverStatusReady = true;
                CheckLoadingComplete();
            }
        });
    }

    private static void CheckProcess(string exeName,
        ref bool running, ref int pid, ref DateTime? startTime)
    {
        var name  = Path.GetFileNameWithoutExtension(exeName);
        var procs = Process.GetProcessesByName(name);
        if (procs.Length > 0)
        {
            running   = true;
            pid       = procs[0].Id;
            startTime ??= procs[0].StartTime;
        }
        else
        {
            running   = false;
            pid       = 0;
            startTime = null;
        }
    }

    private static string FormatUptime(DateTime? startTime)
    {
        if (startTime is null) return "00:00:00";
        return (DateTime.Now - startTime.Value).ToString(@"hh\:mm\:ss");
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _mysql.ProgressChanged     -= OnMySqlProgress;
        _mysql.StatusChanged       -= OnMySqlStatusChanged;
        _webServer.StatusChanged   -= OnWebServerStatusChanged;
        _metrics.Updated           -= OnMetricsUpdated;
        _serverTimer.Stop();
        _serverTimer.Dispose();
        _uptimeTimer.Stop();
        _uptimeTimer.Dispose();
    }
}

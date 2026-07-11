using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
using Trion.Core.Database;
using Trion.Core.Logging;
using Trion.Core.WebServer;
using Trion.Desktop.Infrastructure.Constants;
using Trion.Desktop.Models;

namespace Trion.Desktop.Services;

/// <summary>
/// Handles detection, portable installation, lifecycle management, and hardware-tuned
/// configuration of nginx + PHP-CGI on Windows.  All paths are relative to the
/// application's base directory so the whole folder is portable.
/// </summary>
public sealed class NginxPhpSetupService : INginxPhpSetupService, IDisposable
{
    // -- Versions --------------------------------------------------------------

    private static readonly string NginxVersion = ExternalLinks.NginxVersion;
    private static readonly string PhpVersion   = ExternalLinks.PhpVersion;
    private const int PhpCgiFastCgiPort = 9000;

    // -- Paths (all under AppDir so the installation is portable) --------------

    private static readonly string AppDir    = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string WebDir    = Path.Combine(AppDir, "webserver");
    private static readonly string NginxDir  = Path.Combine(WebDir, "nginx");
    private static readonly string PhpDir    = Path.Combine(WebDir, "php");
    private static readonly string WwwDir    = Path.Combine(WebDir, "www");
    private static readonly string SslDir    = Path.Combine(WebDir, "ssl");
    private static readonly string NginxExe  = Path.Combine(NginxDir, "nginx.exe");
    private static readonly string PhpCgiExe = Path.Combine(PhpDir, "php-cgi.exe");
    private static readonly string NginxConf = Path.Combine(NginxDir, "conf", "nginx.conf");
    private static readonly string PhpIni    = Path.Combine(PhpDir, "php.ini");
    private static readonly string LogDir    = Path.Combine(AppDir, "logs", "webserver");

    private static readonly string DefaultCertPath = Path.Combine(SslDir, "trion_cert.pem");
    private static readonly string DefaultKeyPath  = Path.Combine(SslDir, "trion_key.pem");

    // -- HTTP client ------------------------------------------------------------

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };

    static NginxPhpSetupService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) TrionControlPanel/1.0");
    }

    // -- Dependencies ----------------------------------------------------------

    private readonly ISettingsService              _settings;
    private readonly INotificationService          _notifications;
    private readonly ILogger<NginxPhpSetupService> _logger;
    private readonly IApiKeyService                _apiKeys;

    // -- Setup operation state -------------------------------------------------

    public event Action<string>?            ProgressChanged;
    public event Action<DownloadProgress?>? DownloadProgressChanged;

    public bool              IsRunning       { get; private set; }
    public string            LastMessage     { get; private set; } = string.Empty;
    public DownloadProgress? CurrentDownload { get; private set; }

    // -- Process lifecycle state ------------------------------------------------

    private volatile int               _nginxProcessId;
    private volatile int               _phpCgiProcessId;
    private          DateTime          _processStartTime;
    private          CancellationTokenSource? _monitorCts;
    private readonly SemaphoreSlim     _startStopLock = new(1, 1);

    private          int               _crashCount;
    private const    int               MaxCrashRestarts = 5;

    public event Action<bool>? StatusChanged;

    public bool IsProcessRunning
    {
        get
        {
            var pid = _nginxProcessId;
            if (pid <= 0) return false;
            try   { return !Process.GetProcessById(pid).HasExited; }
            catch (ArgumentException) { return false; }
        }
    }

    public int?      ProcessId => _nginxProcessId > 0 ? _nginxProcessId   : null;
    public DateTime? StartTime => _nginxProcessId > 0 ? _processStartTime : null;
    public TimeSpan  Uptime    => _nginxProcessId > 0
        ? DateTime.Now - _processStartTime
        : TimeSpan.Zero;

    // -- Constructor -----------------------------------------------------------

    public NginxPhpSetupService(ISettingsService settings, INotificationService notifications,
                                ILogger<NginxPhpSetupService> logger, IApiKeyService apiKeys)
    {
        _settings      = settings;
        _notifications = notifications;
        _logger        = logger;
        _apiKeys       = apiKeys;
    }

    // -- Installation flags ----------------------------------------------------

    private bool IsNginxExtracted => File.Exists(NginxExe);
    private bool IsPhpExtracted   => File.Exists(PhpCgiExe);

    /// <summary>
    /// True when both binaries are present on disk, regardless of whether Trion
    /// has configured them.  Used internally by Start/Repair/RunStartupCheck.
    /// </summary>
    private bool AreFilesPresent => IsNginxExtracted && IsPhpExtracted;

    /// <summary>
    /// True when Trion has successfully installed and configured nginx + PHP
    /// (binaries present AND the WebServerInstalled settings flag is set).
    /// Manually-placed binaries that have not been configured by Trion return false.
    /// </summary>
    public bool IsInstalled => AreFilesPresent && _settings.Current.WebServerInstalled;

    // -- Startup check ---------------------------------------------------------

    public async Task RunStartupCheckAsync(CancellationToken ct = default)
    {
        IsRunning = true;
        try
        {
            // -- Download combined web server package (nginx + PHP) ------------
            // The ZIP contains nginx/ and php/ subdirectories that land directly
            // under WebDir after extraction, giving us WebDir/nginx/nginx.exe
            // and WebDir/php/php-cgi.exe.
            if (!IsNginxExtracted || !IsPhpExtracted)
            {
                Notify($"Web server files not found -- downloading combined package (nginx {NginxVersion} + PHP {PhpVersion})...");
                Report($"Downloading web server package (nginx {NginxVersion} + PHP {PhpVersion})...");
                var webZip = await DownloadPackageAsync(
                    "webserver", "combined",
                    AppLinks.WebServerApiDownloadUrl, null, ct);
                Notify("Web server download complete. Extracting files...");
                Report("Extracting web server files...");
                ExtractPackage(webZip, WebDir);
                File.Delete(webZip);
            }
            else
            {
                Report("Web server binaries found -- resuming from previous attempt...");
            }

            Directory.CreateDirectory(LogDir);
            Directory.CreateDirectory(WwwDir);

            if (_settings.Current.EnableHttps)
            {
                Report("Generating self-signed TLS certificate...");
                GenerateSelfSignedCert();
            }

            Report("Generating hardware-tuned nginx.conf...");
            GenerateNginxConf();

            Report("Generating php.ini...");
            GeneratePhpIni();

            Report("Creating sample index.php...");
            CreateSampleIndexPhp();

            _settings.Current.WebServerInstalled = true;
            _settings.Current.WebServerLocation  = WebDir;
            _settings.Save();

            Notify("Web server installed and configured successfully.");
            Report("Web server installed. Starting...");
            await StartAsync(ct: ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Web server setup was cancelled by user or application shutdown.");
            Notify("Web server setup was cancelled.");
            Report("Web server setup was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogException(ex, nameof(RunStartupCheckAsync));
            Notify($"Web server setup failed: {ex.Message}");
            Report($"Web server setup failed: {ex.Message}");
            throw;
        }
        finally
        {
            IsRunning = false;
            ProgressChanged?.Invoke(LastMessage);
        }
    }

    // -- Process lifecycle -----------------------------------------------------

    public async Task StartAsync(bool? hideConsole = null, CancellationToken ct = default)
    {
        if (!AreFilesPresent)
        {
            Report("Web server is not installed. Run startup check first.");
            return;
        }

        bool hide = hideConsole ?? _settings.Current.ConsoleHide;

        await _startStopLock.WaitAsync(ct);
        try
        {
            if (IsProcessRunning)
            {
                Report("Web server is already running.");
                return;
            }

            if (_settings.Current.EnableHttps)
                GenerateSelfSignedCert();

            GenerateNginxConf();
            GeneratePhpIni();
            Directory.CreateDirectory(LogDir);

            // Start PHP-CGI as a FastCGI listener on 127.0.0.1:9000
            var phpPsi = new ProcessStartInfo
            {
                FileName         = PhpCgiExe,
                Arguments        = $"-b 127.0.0.1:{PhpCgiFastCgiPort}",
                UseShellExecute  = false,
                CreateNoWindow   = true,
                WorkingDirectory = PhpDir,
            };

            var phpProc = Process.Start(phpPsi)
                ?? throw new InvalidOperationException("Failed to start php-cgi.exe.");
            _phpCgiProcessId = phpProc.Id;
            _logger.LogInformation("php-cgi started (PID {Pid}) on port {Port}.", phpProc.Id, PhpCgiFastCgiPort);

            // Brief pause so PHP-CGI is bound before nginx begins forwarding requests
            await Task.Delay(400, ct);

            // Start nginx -- WorkingDirectory must be the nginx folder so it can
            // resolve relative paths in nginx.conf (e.g. logs/, conf/mime.types)
            var nginxPsi = new ProcessStartInfo
            {
                FileName         = NginxExe,
                WorkingDirectory = NginxDir,
                UseShellExecute  = false,
                CreateNoWindow   = hide,
            };

            var nginxProc = Process.Start(nginxPsi)
                ?? throw new InvalidOperationException("Failed to start nginx.exe.");

            _nginxProcessId   = nginxProc.Id;
            _processStartTime = DateTime.Now;

            Report($"Web server started (nginx PID: {nginxProc.Id}, php-cgi PID: {phpProc.Id}).");
            Notify($"Web server started (nginx PID: {nginxProc.Id}).");
            StatusChanged?.Invoke(true);

            BeginMonitoring();
        }
        finally
        {
            _startStopLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _startStopLock.WaitAsync(ct);
        try
        {
            if (_nginxProcessId <= 0 && _phpCgiProcessId <= 0)
            {
                Report("Web server is not running.");
                return;
            }

            Report("Stopping web server (nginx -s quit)...");

            // Gracefully tell nginx to finish active connections then exit
            if (_nginxProcessId > 0)
            {
                try
                {
                    await SendNginxSignalAsync("quit", ct);

                    try
                    {
                        var nginxProc = Process.GetProcessById(_nginxProcessId);
                        if (!nginxProc.WaitForExit(10_000))
                        {
                            Report("nginx did not respond to quit -- force killing...");
                            nginxProc.Kill(entireProcessTree: true);
                            try { await nginxProc.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(5), ct); } catch { }
                        }
                    }
                    catch (ArgumentException) { /* already exited */ }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Error stopping nginx."); }
            }

            // php-cgi has no graceful shutdown protocol -- kill it directly
            if (_phpCgiProcessId > 0)
            {
                try
                {
                    var phpProc = Process.GetProcessById(_phpCgiProcessId);
                    phpProc.Kill(entireProcessTree: true);
                    try { await phpProc.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(5), ct); } catch { }
                }
                catch (ArgumentException) { /* already exited */ }
                catch (Exception ex) { _logger.LogWarning(ex, "Error stopping php-cgi."); }
            }

            ClearProcessState();
            _crashCount = 0;

            Report("Web server stopped.");
            Notify("Web server stopped.");
            StatusChanged?.Invoke(false);
        }
        finally
        {
            _startStopLock.Release();
        }
    }

    // -- nginx -s <signal> ------------------------------------------------------

    private async Task SendNginxSignalAsync(string signal, CancellationToken ct)
    {
        // nginx -s <signal> sends the signal to the running master process via its PID file.
        // This is the canonical way to control nginx on Windows -- no Ctrl+C tricks needed.
        var psi = new ProcessStartInfo
        {
            FileName         = NginxExe,
            Arguments        = $"-s {signal}",
            WorkingDirectory = NginxDir,
            UseShellExecute  = false,
            CreateNoWindow   = true,
        };

        using var proc = Process.Start(psi);
        if (proc != null)
        {
            try { await proc.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(10), ct); }
            catch { /* timeout is acceptable -- the signal was already sent */ }
        }
    }

    // -- Monitor loop -- detects unexpected nginx exit ---------------------------

    private void BeginMonitoring()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = new CancellationTokenSource();
        var token = _monitorCts.Token;
        _ = Task.Run(() => MonitorLoopAsync(token), token);
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(3_000, ct); }
            catch (OperationCanceledException) { return; }

            var pid = _nginxProcessId;
            if (pid <= 0) return;

            bool alive;
            try   { alive = !Process.GetProcessById(pid).HasExited; }
            catch (ArgumentException) { alive = false; }

            if (!alive)
            {
                ClearProcessState();
                _logger.LogWarning("nginx (PID {Pid}) exited unexpectedly -- possible crash.", pid);
                StatusChanged?.Invoke(false);

                if (_settings.Current.ServerCrashDetection)
                {
                    if (_crashCount >= MaxCrashRestarts)
                    {
                        _crashCount = 0;
                        string giveUp = $"Web server crashed {MaxCrashRestarts} times -- auto-restart disabled.";
                        _logger.LogWarning("{Message}", giveUp);
                        Report("Web server stopped unexpectedly.");
                        Notify(giveUp);
                        return;
                    }

                    _crashCount++;
                    string msg = $"Web server crashed -- restart attempt {_crashCount}/{MaxCrashRestarts}...";
                    _logger.LogInformation("{Message}", msg);
                    Report(msg);
                    Notify($"Web server stopped unexpectedly. Restarting ({_crashCount}/{MaxCrashRestarts})...");

                    await Task.Delay(2_000, CancellationToken.None);

                    try   { await StartAsync(); }
                    catch (Exception ex) { _logger.LogException(ex, "NginxPhpSetupService crash restart"); }
                }
                else
                {
                    Report("Web server process exited unexpectedly.");
                    Notify("Web server stopped unexpectedly.");
                }

                return;
            }
        }
    }

    private void ClearProcessState()
    {
        _nginxProcessId   = 0;
        _phpCgiProcessId  = 0;
        _processStartTime = default;
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = null;
    }

    // -- Repair / Uninstall / Reinstall ----------------------------------------

    public async Task RepairAsync(CancellationToken ct = default)
    {
        if (!AreFilesPresent)
        {
            Report("Web server is not installed -- nothing to repair.");
            return;
        }

        Report("Stopping web server for repair...");
        await StopAsync(ct);

        Report("Regenerating nginx.conf and php.ini...");
        GenerateNginxConf();
        GeneratePhpIni();
        Notify("Web server configuration repaired.");

        Report("Restarting web server...");
        await StartAsync(ct: ct);
    }

    public async Task UninstallAsync(CancellationToken ct = default)
    {
        if (IsProcessRunning)
        {
            Report("Stopping web server before uninstall...");
            await StopAsync(ct);
        }

        Report("Removing web server files...");
        if (Directory.Exists(WebDir))
            await Task.Run(() => Directory.Delete(WebDir, recursive: true), ct);

        _settings.Current.WebServerInstalled = false;
        _settings.Current.WebServerLocation  = string.Empty;
        _settings.Save();

        Report("Web server uninstalled.");
        Notify("Web server has been uninstalled.");
    }

    public async Task ReinstallAsync(CancellationToken ct = default)
    {
        Report("Preparing for reinstall -- uninstalling current installation...");
        await UninstallAsync(ct);

        Report("Starting fresh web server installation...");
        await RunStartupCheckAsync(ct);
    }

    // -- Dispose ---------------------------------------------------------------

    public void Dispose()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _startStopLock.Dispose();
    }

    // -- nginx.conf generation -------------------------------------------------

    public void GenerateNginxConf()
    {
        var cfg = _settings.Current;

        int    cpuCores   = Environment.ProcessorCount;
        int    workerConn = Math.Clamp(cpuCores * 1_024, 1_024, 65_536);
        int    httpPort   = cfg.WebServerPort  > 0 ? cfg.WebServerPort  : 80;
        int    httpsPort  = cfg.HttpsPort      > 0 ? cfg.HttpsPort      : 443;
        bool   https      = cfg.EnableHttps;
        string certPath   = !string.IsNullOrEmpty(cfg.SslCertPath) ? cfg.SslCertPath : DefaultCertPath;
        string keyPath    = !string.IsNullOrEmpty(cfg.SslKeyPath)  ? cfg.SslKeyPath  : DefaultKeyPath;

        // nginx on Windows resolves relative paths from its working directory,
        // which we set to NginxDir on start.  Forward-slash paths are required.
        string wwwDirFwd   = WwwDir.Replace('\\', '/');
        string logDirFwd   = LogDir.Replace('\\', '/');
        string nginxDirFwd = NginxDir.Replace('\\', '/');
        string certPathFwd = certPath.Replace('\\', '/');
        string keyPathFwd  = keyPath.Replace('\\', '/');

        var conf = new StringBuilder();
        conf.AppendLine($"# nginx {NginxVersion} -- generated by Trion Control Panel");
        conf.AppendLine($"# System: {cpuCores} CPU threads");
        conf.AppendLine();
        conf.AppendLine($"worker_processes {cpuCores};");
        conf.AppendLine();
        conf.AppendLine($"error_log  \"{logDirFwd}/nginx_error.log\" warn;");
        conf.AppendLine($"pid        \"{logDirFwd}/nginx.pid\";");
        conf.AppendLine();
        conf.AppendLine("events {");
        conf.AppendLine($"    worker_connections {workerConn};");
        conf.AppendLine("}");
        conf.AppendLine();
        conf.AppendLine("http {");
        conf.AppendLine($"    include       \"{nginxDirFwd}/conf/mime.types\";");
        conf.AppendLine("    default_type  application/octet-stream;");
        conf.AppendLine();
        conf.AppendLine("    sendfile        on;");
        conf.AppendLine("    keepalive_timeout 65;");
        conf.AppendLine();
        conf.AppendLine("    gzip  on;");
        conf.AppendLine("    gzip_types text/plain text/css application/json application/javascript text/xml application/xml;");
        conf.AppendLine();

        if (https)
        {
            // HTTP -> HTTPS redirect block
            conf.AppendLine("    # HTTP -- redirect all traffic to HTTPS");
            conf.AppendLine("    server {");
            conf.AppendLine($"        listen       {httpPort};");
            conf.AppendLine("        server_name  localhost;");
            conf.AppendLine("        return 301   https://$host$request_uri;");
            conf.AppendLine("    }");
            conf.AppendLine();

            // HTTPS server block
            conf.AppendLine("    # HTTPS server");
            conf.AppendLine("    server {");
            conf.AppendLine($"        listen       {httpsPort} ssl;");
            conf.AppendLine("        server_name  localhost;");
            conf.AppendLine();
            conf.AppendLine($"        ssl_certificate      \"{certPathFwd}\";");
            conf.AppendLine($"        ssl_certificate_key  \"{keyPathFwd}\";");
            conf.AppendLine("        ssl_protocols        TLSv1.2 TLSv1.3;");
            conf.AppendLine("        ssl_ciphers          HIGH:!aNULL:!MD5;");
            conf.AppendLine("        ssl_session_cache    shared:SSL:10m;");
            conf.AppendLine("        ssl_session_timeout  10m;");
        }
        else
        {
            conf.AppendLine("    server {");
            conf.AppendLine($"        listen       {httpPort};");
            conf.AppendLine("        server_name  localhost;");
        }

        conf.AppendLine();
        conf.AppendLine($"        root  \"{wwwDirFwd}\";");
        conf.AppendLine("        index index.php index.html index.htm;");
        conf.AppendLine();
        conf.AppendLine($"        access_log \"{logDirFwd}/nginx_access.log\";");
        conf.AppendLine($"        error_log  \"{logDirFwd}/nginx_error.log\";");
        conf.AppendLine();
        conf.AppendLine("        location / {");
        conf.AppendLine("            try_files $uri $uri/ /index.php?$query_string;");
        conf.AppendLine("        }");
        conf.AppendLine();
        conf.AppendLine("        # Pass .php requests to PHP-CGI FastCGI listener");
        conf.AppendLine("        location ~ \\.php$ {");
        conf.AppendLine($"            fastcgi_pass   127.0.0.1:{PhpCgiFastCgiPort};");
        conf.AppendLine("            fastcgi_index  index.php;");
        conf.AppendLine("            fastcgi_param  SCRIPT_FILENAME $document_root$fastcgi_script_name;");
        conf.AppendLine($"            include        \"{nginxDirFwd}/conf/fastcgi_params\";");
        conf.AppendLine("        }");
        conf.AppendLine("    }");
        conf.AppendLine("}");

        Directory.CreateDirectory(Path.GetDirectoryName(NginxConf)!);
        File.WriteAllText(NginxConf, conf.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // -- TLS certificate generation --------------------------------------------

    public void GenerateSelfSignedCert()
    {
        var certPath = DefaultCertPath;
        var keyPath  = DefaultKeyPath;

        // Honour custom paths if already configured
        if (!string.IsNullOrEmpty(_settings.Current.SslCertPath))
            certPath = _settings.Current.SslCertPath;
        if (!string.IsNullOrEmpty(_settings.Current.SslKeyPath))
            keyPath = _settings.Current.SslKeyPath;

        // Only skip if both files already exist (allows forced re-generation via Repair)
        if (File.Exists(certPath) && File.Exists(keyPath))
            return;

        Directory.CreateDirectory(SslDir);

        using var rsa = RSA.Create(2048);

        var req = new CertificateRequest(
            "CN=localhost, O=Trion Control Panel",
            rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Subject Alternative Names -- required by modern browsers
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);         // 127.0.0.1
        san.AddIpAddress(IPAddress.IPv6Loopback);     // ::1
        req.CertificateExtensions.Add(san.Build());

        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, critical: false));

        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: false));

        // Extended key usage: server authentication (OID 1.3.6.1.5.5.7.3.1)
        req.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1")], critical: false));

        var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),   // -1 day to absorb clock skew
            DateTimeOffset.UtcNow.AddYears(10));

        File.WriteAllText(certPath, cert.ExportCertificatePem(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(keyPath, rsa.ExportPkcs8PrivateKeyPem(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        _settings.Current.SslCertPath = certPath;
        _settings.Current.SslKeyPath  = keyPath;
        _settings.Save();

        _logger.LogInformation("Self-signed TLS cert generated -> {Cert}", certPath);
    }

    // -- php.ini generation ----------------------------------------------------

    private void GeneratePhpIni()
    {
        string logDirFwd = LogDir.Replace('\\', '/');
        string phpDirFwd = PhpDir.Replace('\\', '/');

        var ini = new StringBuilder();
        ini.AppendLine($"; PHP {PhpVersion} -- generated by Trion Control Panel");
        ini.AppendLine();
        ini.AppendLine($"extension_dir = \"{phpDirFwd}/ext\"");
        ini.AppendLine();
        ini.AppendLine("; FastCGI settings");
        ini.AppendLine("cgi.fix_pathinfo = 0");
        ini.AppendLine("fastcgi.impersonate = 1");
        ini.AppendLine();
        ini.AppendLine("; Error reporting");
        ini.AppendLine("error_reporting = E_ALL & ~E_DEPRECATED & ~E_STRICT");
        ini.AppendLine("display_errors = Off");
        ini.AppendLine("log_errors = On");
        ini.AppendLine($"error_log = \"{logDirFwd}/php_error.log\"");
        ini.AppendLine();
        ini.AppendLine("; Memory and execution");
        ini.AppendLine("memory_limit = 128M");
        ini.AppendLine("max_execution_time = 30");
        ini.AppendLine("upload_max_filesize = 32M");
        ini.AppendLine("post_max_size = 32M");
        ini.AppendLine();
        ini.AppendLine("; Timezone");
        ini.AppendLine("date.timezone = UTC");

        File.WriteAllText(PhpIni, ini.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // -- Sample web page -------------------------------------------------------

    private static void CreateSampleIndexPhp()
    {
        Directory.CreateDirectory(WwwDir);
        var indexPath = Path.Combine(WwwDir, "index.php");
        if (File.Exists(indexPath)) return;  // never overwrite user content

        File.WriteAllText(indexPath,
            "<?php\n" +
            "// Trion Control Panel -- web server is working!\n" +
            "echo '<h1>Trion Web Server</h1>';\n" +
            "echo '<p>nginx ' . nginx_version() . ' + PHP ' . phpversion() . ' is running.</p>';\n" +
            "echo '<p>Web root: ' . __DIR__ . '</p>';\n",
            Encoding.UTF8);
    }

    // -- Download --------------------------------------------------------------

    private async Task<string> DownloadPackageAsync(
        string packageName, string version,
        string apiUrl, string? cdnFallback,
        CancellationToken ct)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"{packageName}-{version}-winx64.zip");

        if (File.Exists(zipPath))
        {
            if (IsValidZip(zipPath))
            {
                Report($"Found cached {packageName} download, resuming extract...");
                return zipPath;
            }

            Report($"Cached {packageName} download is corrupt -- deleting and re-downloading...");
            File.Delete(zipPath);
        }

        // Primary: Trion API -- POST JSON {"emulator":"<packageName>"} so the package name
        // travels in the request body, not the query string.
        Report($"Downloading {packageName} {version} via Trion API...");
        var jsonBody = $"{{\"emulator\":\"{packageName}\"}}";
        bool apiOk = await TryDownloadFromUrlAsync(apiUrl, zipPath, addApiKey: true, ct, jsonBody);

        if (!apiOk)
        {
            if (cdnFallback is null)
                throw new InvalidOperationException(
                    $"{packageName} download failed. Check your connection and supporter API key.");

            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (attempt == 1)
                    Report($"Trion API unavailable -- falling back to {packageName} CDN...");
                else
                    Report($"{packageName} CDN download stalled -- retry {attempt}/{maxAttempts}...");

                // CDN fallback uses plain GET (no JSON body)
                bool cdnOk = await TryDownloadFromUrlAsync(cdnFallback, zipPath, addApiKey: false, ct);
                if (cdnOk) break;

                if (attempt == maxAttempts)
                    throw new InvalidOperationException(
                        $"{packageName} download failed after {maxAttempts} attempts. Check your connection and try again.");
            }
        }

        return zipPath;
    }

    private async Task<bool> TryDownloadFromUrlAsync(string url, string destPath, bool addApiKey, CancellationToken ct, string? jsonBody = null)
    {
        try
        {
            await DownloadFromUrlAsync(url, destPath, addApiKey, ct, jsonBody);
            return true;
        }
        catch
        {
            if (File.Exists(destPath)) File.Delete(destPath);
            return false;
        }
    }

    private async Task DownloadFromUrlAsync(string url, string destPath, bool addApiKey, CancellationToken ct, string? jsonBody = null)
    {
        var method = jsonBody is not null ? HttpMethod.Post : HttpMethod.Get;
        using var req = new HttpRequestMessage(method, url);
        if (addApiKey)
        {
            var key = _apiKeys.ActiveKey;
            if (!string.IsNullOrEmpty(key))
                req.Headers.TryAddWithoutValidation("X-API-Key", key);
        }
        if (jsonBody is not null)
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? -1L;
        var initial = new DownloadProgress(0, totalBytes, 0, null, 0);
        CurrentDownload = initial;
        DownloadProgressChanged?.Invoke(initial);

        await using var networkStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream    = new FileStream(destPath, FileMode.Create,
                                                       FileAccess.Write, FileShare.None,
                                                       bufferSize: 81_920, useAsync: true);
        var  buffer      = new byte[81_920];
        long downloaded  = 0L;
        int  read;

        var    sw            = Stopwatch.StartNew();
        long   windowBytes   = 0L;
        long   windowStartMs = 0L;
        double speedBps      = 0;

        using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stallCts.CancelAfter(TimeSpan.FromSeconds(30));

        while ((read = await networkStream.ReadAsync(buffer, stallCts.Token)) > 0)
        {
            stallCts.CancelAfter(TimeSpan.FromSeconds(30));
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded  += read;
            windowBytes += read;

            long elapsedMs = sw.ElapsedMilliseconds;
            long windowMs  = elapsedMs - windowStartMs;

            if (windowMs >= 500)
            {
                speedBps      = windowBytes * 1000.0 / windowMs;
                windowBytes   = 0;
                windowStartMs = elapsedMs;

                int       pct = totalBytes > 0 ? (int)(downloaded * 100 / totalBytes) : 0;
                TimeSpan? eta = totalBytes > 0 && speedBps > 0
                    ? TimeSpan.FromSeconds((totalBytes - downloaded) / speedBps)
                    : null;

                var progress = new DownloadProgress(downloaded, totalBytes, speedBps, eta, pct);
                CurrentDownload = progress;
                DownloadProgressChanged?.Invoke(progress);
                Report(BuildDownloadMessage(downloaded, totalBytes, speedBps, eta, pct));
            }
        }

        CurrentDownload = null;
        DownloadProgressChanged?.Invoke(null);
    }

    private static string BuildDownloadMessage(
        long downloaded, long total, double speedBps, TimeSpan? eta, int pct)
    {
        string size  = total > 0
            ? $"{FormatBytes(downloaded)} / {FormatBytes(total)} ({pct}%)"
            : FormatBytes(downloaded);
        string speed = $"{FormatBytes((long)speedBps)}/s";
        string time  = eta.HasValue ? $"ETA {eta.Value:mm\\:ss}" : "ETA --";
        return $"Downloading web server...  {size}  *  {speed}  *  {time}";
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024         => $"{bytes / 1_024.0:F0} KB",
        _                => $"{bytes} B",
    };

    // -- Extract ---------------------------------------------------------------

    private void ExtractPackage(string zipPath, string destDir)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"trion_extract_{Guid.NewGuid():N}");

        try
        {
            Report($"Extracting archive to {Path.GetFileName(destDir)}/ folder...");
            try
            {
                ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true);
            }
            catch (InvalidDataException)
            {
                try { File.Delete(zipPath); } catch { /* best effort */ }
                throw new InvalidDataException(
                    "The downloaded archive is corrupt and has been deleted. " +
                    "Please restart the application to re-download.");
            }

            // Both nginx and PHP ship with a single root folder in their zips
            // (e.g. nginx-1.27.4\, php-8.3.20-nts-Win32-vs16-x64\).
            // Move the contents up one level so binaries land directly in destDir.
            var entries = Directory.GetFileSystemEntries(tempDir);
            string sourceDir = entries.Length == 1 && Directory.Exists(entries[0])
                ? entries[0]
                : tempDir;

            Directory.CreateDirectory(destDir);

            foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(sourceDir, destDir));

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                File.Move(file, file.Replace(sourceDir, destDir), overwrite: true);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    // -- Progress helpers ------------------------------------------------------

    private void Report(string message)
    {
        LastMessage = message;
        ProgressChanged?.Invoke(message);
        _logger.LogInformation("{Message}", message);
    }

    private void Notify(string message) => _notifications.Add($"[WebServer] {message}");

    // -- ZIP validation --------------------------------------------------------

    private static bool IsValidZip(string path)
    {
        const long minSize = 512 * 1_024; // 512 KB

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < minSize) return false;

            Span<byte> magic = stackalloc byte[4];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return fs.Read(magic) == 4
                && magic[0] == 0x50   // 'P'
                && magic[1] == 0x4B   // 'K'
                && magic[2] == 0x03
                && magic[3] == 0x04;
        }
        catch
        {
            return false;
        }
    }
}

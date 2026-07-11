using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Trion.Core.Abstractions.Monitoring;
using Trion.Core.Logging;
using Trion.Core.Monitoring;

namespace Trion.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsMetricsProvider : IMachineMetricsProvider
{
    private readonly ILogger              _log;
    private readonly WindowsDiskReader    _disk;
    private readonly WindowsNetworkReader _net;

    private long _totalRamBytes;
    private volatile bool _initialized;
    private readonly object _initLock = new();

    // -- CPU via GetSystemTimes --------------------------------------------
    private long _prevIdle;
    private long _prevTotal;
    private bool _sysTimesSeeded;

    public WindowsMetricsProvider(
        TrionLogger          trionLogger,
        WindowsDiskReader    disk,
        WindowsNetworkReader net)
    {
        _log  = trionLogger.CreateLogger(nameof(WindowsMetricsProvider));
        _disk = disk;
        _net  = net;
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            SeedCpuTimes();
            InitTotalRam();
            _initialized = true;
        }
    }

    public MachineMetrics GetSnapshot()
    {
        EnsureInitialized();
        var cpu    = GetCpuPercent();
        var usedRam = GetUsedRamBytes();
        var (diskRead, diskWrite) = _disk.GetDiskThroughput();
        var (netRx, netTx)        = _net.GetNetworkThroughput();

        return new MachineMetrics(
            CpuPercent:           cpu,
            RamTotalBytes:        _totalRamBytes,
            RamUsedBytes:         usedRam,
            DiskReadBytesPerSec:  (long)diskRead,
            DiskWriteBytesPerSec: (long)diskWrite,
            NetworkRxBytesPerSec: (long)netRx,
            NetworkTxBytesPerSec: (long)netTx,
            Timestamp:            DateTimeOffset.UtcNow);
    }

    // Seed the previous-tick baseline so the first real call returns a valid rate.
    private void SeedCpuTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return;
        _prevIdle  = FileTimeToLong(idle);
        _prevTotal = FileTimeToLong(kernel) + FileTimeToLong(user);
        _sysTimesSeeded = true;
    }

    private void InitTotalRam()
    {
        try
        {
            if (TryGetMemoryStatus(out var mem))
                _totalRamBytes = (long)mem.ullTotalPhys;
            else
                _totalRamBytes = 0;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "GlobalMemoryStatusEx unavailable.");
            _totalRamBytes = 0;
        }
    }

    private double GetCpuPercent()
    {
        if (!_sysTimesSeeded) return 0.0;
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0.0;

        long currentIdle  = FileTimeToLong(idle);
        long currentTotal = FileTimeToLong(kernel) + FileTimeToLong(user);

        long idleDelta  = currentIdle  - _prevIdle;
        long totalDelta = currentTotal - _prevTotal;

        _prevIdle  = currentIdle;
        _prevTotal = currentTotal;

        if (totalDelta <= 0) return 0.0;

        // kernel time includes idle time -> CPU% = (1 - idle/total) * 100
        return Math.Round(Math.Clamp((1.0 - (double)idleDelta / totalDelta) * 100.0, 0.0, 100.0), 1);
    }

    private long GetUsedRamBytes()
    {
        try
        {
            if (!TryGetMemoryStatus(out var mem)) return 0;
            return (long)(mem.ullTotalPhys - mem.ullAvailPhys);
        }
        catch { return 0; }
    }

    // -- GetSystemTimes P/Invoke -------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out FILETIME lpIdleTime,
        out FILETIME lpKernelTime,
        out FILETIME lpUserTime);

    private static long FileTimeToLong(FILETIME ft) =>
        ((long)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

    // -- GlobalMemoryStatusEx P/Invoke -------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint  dwLength;
        public uint  dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static bool TryGetMemoryStatus(out MEMORYSTATUSEX mem)
    {
        mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref mem);
    }
}

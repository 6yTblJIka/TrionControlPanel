using System.Diagnostics;
using System.Net.Sockets;

namespace Trion.Core.Services.Infrastructure;

/// <summary>
/// Probes infrastructure services (MySQL, Auth server, World server, Nginx)
/// using TCP port connects and process-name lookups.
/// </summary>
public sealed class InfraStatusService
{
    private static readonly TimeSpan TcpTimeout = TimeSpan.FromSeconds(1);

    public async Task<InfraStatus[]> GetAllAsync(CancellationToken ct = default)
    {
        var tasks = new[]
        {
            ProbeAsync("MySQL",        TcpProbe("127.0.0.1", 3306,  ct)),
            ProbeAsync("Auth Server",  AnyAsync(ProcessProbe("authserver", "bnetserver"),
                                                TcpProbe("127.0.0.1", 3724, ct))),
            ProbeAsync("World Server", AnyAsync(ProcessProbe("worldserver"),
                                                TcpProbe("127.0.0.1", 8085, ct))),
            ProbeAsync("Nginx",        AnyAsync(ProcessProbe("nginx"),
                                                TcpProbe("127.0.0.1", 80,   ct))),
        };

        return await Task.WhenAll(tasks);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<InfraStatus> ProbeAsync(string name, Task<(bool ok, string detail)> probe)
    {
        var (ok, detail) = await probe;
        return new InfraStatus(name, detail, ok ? InfraServiceState.Online : InfraServiceState.Offline);
    }

    private static async Task<(bool, string)> TcpProbe(string host, int port, CancellationToken ct)
    {
        try
        {
            using var tcp    = new TcpClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TcpTimeout);

            await tcp.ConnectAsync(host, port, linked.Token);
            return (true, $":{port}");
        }
        catch
        {
            return (false, $":{port} unreachable");
        }
    }

    private static Task<(bool, string)> ProcessProbe(params string[] names)
    {
        foreach (var name in names)
        {
            var procs = Process.GetProcessesByName(name);
            if (procs.Length > 0)
            {
                var pid = procs[0].Id;
                foreach (var p in procs) p.Dispose();
                return Task.FromResult<(bool, string)>((true, $"PID {pid}"));
            }
        }
        return Task.FromResult<(bool, string)>((false, "not running"));
    }

    /// <summary>Returns the first probe that reports online, or the last result if all fail.</summary>
    private static async Task<(bool, string)> AnyAsync(params Task<(bool, string)>[] probes)
    {
        var results = await Task.WhenAll(probes);
        return Array.Find(results, r => r.Item1) is { Item1: true } hit
            ? hit
            : results[^1];
    }
}

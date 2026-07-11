using Microsoft.Extensions.DependencyInjection;

namespace Trion.Core.Monitoring;

public static class MonitoringExtensions
{
    /// <summary>
    /// Registers the cross-platform monitoring stack:
    /// <see cref="MetricsChannelAccessor"/>, <see cref="ISystemMetricsService"/>,
    /// <see cref="MachineMetricsWorker"/>, and <see cref="ProcessMonitor"/>.
    /// Call <c>AddPlatformServices()</c> (Trion.Platform) first to register
    /// <c>IMachineMetricsProvider</c>.
    /// </summary>
    public static IServiceCollection AddMonitoringServices(this IServiceCollection services)
    {
        // Options -- defaults are suitable for Desktop; Web binds from appsettings.json separately.
        services.AddOptions<ProcessMonitorOptions>();

        services.AddSingleton<MetricsChannelAccessor>();
        services.AddSingleton<ISystemMetricsService, SystemMetricsService>();
        services.AddHostedService<MachineMetricsWorker>();
        services.AddHostedService<ProcessMonitor>();
        return services;
    }
}

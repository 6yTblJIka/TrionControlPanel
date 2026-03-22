namespace Trion.Core.Monitoring;

/// <summary>
/// Bridges <see cref="MetricsChannelAccessor.MachineUpdated"/> into the
/// <see cref="ISystemMetricsService"/> event/property contract consumed by UI layers.
/// Register as a singleton via <see cref="MonitoringExtensions.AddMonitoringServices"/>.
/// </summary>
public sealed class SystemMetricsService : ISystemMetricsService, IDisposable
{
    private readonly MetricsChannelAccessor _accessor;

    public MachineMetrics? Last { get; private set; }
    public event Action<MachineMetrics>? Updated;

    public SystemMetricsService(MetricsChannelAccessor accessor)
    {
        _accessor = accessor;
        Last      = _accessor.LastMachine;
        _accessor.MachineUpdated += OnMachineUpdated;
    }

    private void OnMachineUpdated(MachineMetrics m)
    {
        Last = m;
        Updated?.Invoke(m);
    }

    public void Dispose() => _accessor.MachineUpdated -= OnMachineUpdated;
}

namespace Trion.Core.Monitoring;

/// <summary>
/// Provides the most-recent machine metrics snapshot and notifies subscribers
/// whenever a new snapshot is published by <see cref="MachineMetricsWorker"/>.
/// </summary>
public interface ISystemMetricsService
{
    /// <summary>Most recent snapshot, or <see langword="null"/> before the first sample is ready.</summary>
    MachineMetrics? Last { get; }

    /// <summary>Raised on the background thread whenever a new snapshot is available.</summary>
    event Action<MachineMetrics>? Updated;
}

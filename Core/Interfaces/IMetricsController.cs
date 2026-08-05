using TaskbarProgress.Core.Models;

namespace TaskbarProgress.Core.Interfaces;

public interface IMetricsCollector
{
    string Name { get; }
    Task<SystemMetrics> CollectAsync(CancellationToken ct);
}
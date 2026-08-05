namespace TaskbarProgress.Infrastructure.Collectors;

using System.Diagnostics;
using Core.Interfaces;
using Core.Models;

public class CpuCollector : IMetricsCollector
{
    private readonly PerformanceCounter _cpuCounter;
    private readonly PerformanceCounter _cpuCounter2; // For averaging over interval
    public string Name => "CPU";

    public CpuCollector()
    {
        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        _cpuCounter2 = new PerformanceCounter("Processor", "% Processor Time", "_Total");
    }

    public async Task<SystemMetrics> CollectAsync(CancellationToken ct)
    {
        // First read primes the counter
        _ = _cpuCounter.NextValue();
        await Task.Delay(100, ct); // Need a small interval for accurate reading
        
        var cpuUsage = _cpuCounter.NextValue();
        
        return new SystemMetrics { CpuPercent = Math.Round(cpuUsage, 1) };
    }
}
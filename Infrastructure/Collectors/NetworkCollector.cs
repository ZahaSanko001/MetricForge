using System.Diagnostics;
using TaskbarProgress.Core.Interfaces;
using TaskbarProgress.Core.Models;

namespace TaskbarProgress.Infrastructure.Collectors;

public class NetworkCollector : IMetricsCollector
{
    private readonly PerformanceCounter _bytesSent;
    private readonly PerformanceCounter _bytesReceived;
    private bool _isInitialized;
    public string Name => "Network";

    public NetworkCollector()
    {
        try
        {
            var category = new PerformanceCounterCategory("Network Interface");
            var instanceNames = category.GetInstanceNames();
            var instance = instanceNames.FirstOrDefault(n => 
                !n.Contains("isatap") && !n.Contains("Loopback")) ?? instanceNames.FirstOrDefault() ?? "";
            
            _bytesSent = new PerformanceCounter("Network Interface", "Bytes Sent/sec", instance);
            _bytesReceived = new PerformanceCounter("Network Interface", "Bytes Received/sec", instance);
            _isInitialized = true;
        }
        catch
        {
            _isInitialized = false;
            _bytesSent = null!;
            _bytesReceived = null!;
        }
    }

    public Task<SystemMetrics> CollectAsync(CancellationToken ct)
    {
        if (!_isInitialized)
            return Task.FromResult(new SystemMetrics { NetworkKbps = 0 });

        var sent = _bytesSent.NextValue();
        var received = _bytesReceived.NextValue();
        var totalKbps = (sent + received) * 8 / 1024; // Convert bytes to kilobits
        
        return Task.FromResult(new SystemMetrics { NetworkKbps = Math.Round(totalKbps, 1) });
    }
}
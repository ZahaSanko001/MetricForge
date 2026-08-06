using System.Diagnostics;
using TaskbarProgress.Core.Interfaces;
using TaskbarProgress.Core.Models;

namespace TaskbarProgress.Infrastructure.Collectors;

public sealed class NetworkCollector : IMetricsCollector
{
    private readonly List<(PerformanceCounter Sent, PerformanceCounter Received)> _interfaces = new();

    public string Name => "Network";

    public NetworkCollector()
    {
        try
        {
            var category = new PerformanceCounterCategory("Network Interface");

            foreach (var instance in category.GetInstanceNames())
            {
                if (instance.Contains("isatap", StringComparison.OrdinalIgnoreCase) ||
                    instance.Contains("loopback", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    _interfaces.Add((
                        new PerformanceCounter("Network Interface", "Bytes Sent/sec", instance),
                        new PerformanceCounter("Network Interface", "Bytes Received/sec", instance)));
                }
                catch
                {
                    // Some adapters disappear while Windows is enumerating them.
                }
            }
        }
        catch
        {
            // Performance counters may be unavailable on some Windows setups.
        }
    }

    public Task<SystemMetrics> CollectAsync(CancellationToken ct)
    {
        if (_interfaces.Count == 0)
            return Task.FromResult(new SystemMetrics { NetworkKbps = 0 });

        double downloadBytesPerSecond = 0;
        double uploadBytesPerSecond = 0;
        foreach (var networkInterface in _interfaces)
        {
            try
            {
                uploadBytesPerSecond += networkInterface.Sent.NextValue();
                downloadBytesPerSecond += networkInterface.Received.NextValue();
            }
            catch
            {
                // Ignore an adapter that became unavailable after startup.
            }
        }

        var uploadKbps = uploadBytesPerSecond * 8 / 1024;
        var downloadKbps = downloadBytesPerSecond * 8 / 1024;
        return Task.FromResult(new SystemMetrics
        {
            NetworkKbps = Math.Round(uploadKbps + downloadKbps, 1),
            NetworkDownloadKbps = Math.Round(downloadKbps, 1),
            NetworkUploadKbps = Math.Round(uploadKbps, 1)
        });
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using TaskbarProgress.Core.Interfaces;
using TaskbarProgress.Core.Models;

namespace TaskbarProgress.Infrastructure.Collectors;

public class MemoryCollector : IMetricsCollector
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    private readonly PerformanceCounter _fallbackMemoryCounter;
    public string Name => "Memory";

    public MemoryCollector()
    {
        _fallbackMemoryCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
    }

    public Task<SystemMetrics> CollectAsync(CancellationToken ct)
    {
        // Task Manager's main Memory percentage is based on physical RAM.
        // GlobalMemoryStatusEx returns the same kind of system-wide value,
        // unlike the committed-bytes counter, which can be much lower.
        var status = new MEMORYSTATUSEX
        {
            Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
        };

        var memoryUsage = GlobalMemoryStatusEx(ref status)
            ? status.MemoryLoad
            : _fallbackMemoryCounter.NextValue();

        return Task.FromResult(new SystemMetrics { MemoryPercent = Math.Round(memoryUsage, 1) });
    }
}

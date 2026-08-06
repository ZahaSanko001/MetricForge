namespace TaskbarProgress.Core.Models;

public record SystemMetrics
{
    public double CpuPercent { get; init; }
    public double MemoryPercent { get; init; }
    public double NetworkKbps { get; init; }
    public double NetworkDownloadKbps { get; init; }
    public double NetworkUploadKbps { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public enum MetricType
{
    Cpu,
    Memory,
    Network,
    Combined
}

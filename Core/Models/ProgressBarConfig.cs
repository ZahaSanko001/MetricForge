namespace TaskbarProgress.Core.Models;

public record ProgressBarConfig
{
    public int BarHeight { get; init; } = 3; // Pixels
    public int UpdateIntervalMs { get; init; } = 1000;
    // Network collector reports kilobits per second.
    public double NetworkPeakKbps { get; init; } = 100_000; // 100 Mbps
    public bool AutoStart { get; init; } = true;
    public MetricType DisplayMetric { get; init; } = MetricType.Cpu;
    public ProgressBarColors Colors { get; init; } = new();
}

public record ProgressBarColors
{
    public (byte R, byte G, byte B) Low { get; init; } = (0, 255, 100);    // Green
    public (byte R, byte G, byte B) Medium { get; init; } = (255, 200, 0);  // Yellow
    public (byte R, byte G, byte B) High { get; init; } = (255, 50, 50);    // Red
}

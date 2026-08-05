namespace TaskbarProgress.Core.Models;

public record ProgressBarConfig
{
    public int BarSize { get; init; } = 10; // Pixels; controls bar width and height
    public int BarOpacity { get; init; } = 70; // Percentage, 10-100
    public int UpdateIntervalMs { get; init; } = 1000;
    // Network collector reports kilobits per second.
    public double NetworkPeakKbps { get; init; } = 100_000; // 100 Mbps
    public bool AutoStart { get; init; } = true;
    public MetricType DisplayMetric { get; init; } = MetricType.Cpu;
    public ProgressBarColors Colors { get; init; } = new();
}

public record ProgressBarColors
{
    public (byte R, byte G, byte B) Low { get; init; } = (46, 41, 78);     // #2E294E
    public (byte R, byte G, byte B) Medium { get; init; } = (244, 140, 6);  // #f48c06
    public (byte R, byte G, byte B) High { get; init; } = (157, 2, 8);      // #9D0208
}

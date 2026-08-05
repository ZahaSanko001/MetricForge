namespace TaskbarProgress.Core.Models;

public record ProgressBarConfig
{
    public int BarSize { get; init; } = 10; // Pixels; controls bar width and height
    public int BarOpacity { get; init; } = 100; // Percentage, 10-100
    public int UpdateIntervalMs { get; init; } = 1000;
    // Network collector reports kilobits per second.
    public double NetworkPeakKbps { get; init; } = 100_000; // 100 Mbps
    public bool AutoStart { get; init; } = true;
    public MetricType DisplayMetric { get; init; } = MetricType.Cpu;
    public ProgressBarColors Colors { get; init; } = new();
}

public record ProgressBarColors
{
    public (byte R, byte G, byte B) Low { get; init; } = (73, 255, 0);     // #49FF00
    public (byte R, byte G, byte B) Medium { get; init; } = (251, 255, 0); // #FBFF00
    public (byte R, byte G, byte B) High { get; init; } = (255, 0, 0);     // #FF0000
}

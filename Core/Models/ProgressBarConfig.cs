namespace TaskbarProgress.Core.Models;

public record ProgressBarConfig
{
    public int BarSize { get; init; } = 8; // Pixels; controls bar width and height
    public int BarOpacity { get; init; } = 100; // Percentage, 10-100
    public ThemePreference ThemeOverride { get; init; } = ThemePreference.Auto;
    public bool ShowLabels { get; init; } = true;
    public bool ShowValues { get; init; } = true;
    public int UpdateIntervalMs { get; init; } = 1000;
    // Network collector reports kilobits per second.
    public double NetworkPeakKbps { get; init; } = 30_000; // 30 Mbps
    public bool AutoStart { get; init; } = true;
    public MetricType DisplayMetric { get; init; } = MetricType.Cpu;
    public ProgressBarColors Colors { get; init; } = new();
}

public record ProgressBarColors
{
    public (byte R, byte G, byte B) Low { get; init; } = (5, 63, 82);     // #053f52
    public (byte R, byte G, byte B) Medium { get; init; } = (186, 236, 23); // #BAEC17
    public (byte R, byte G, byte B) High { get; init; } = (181, 26, 44);   // #b51a2c
}

public enum ThemePreference
{
    Auto,
    Light,
    Dark
}

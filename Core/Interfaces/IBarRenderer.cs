using TaskbarProgress.Core.Models;

public interface IBarRenderer
{
    void Initialize(int barHeight);
    void Render(SystemMetrics metrics, ProgressBarConfig config);
    void Clear();
    void UpdateConfiguration(ProgressBarConfig config);
}

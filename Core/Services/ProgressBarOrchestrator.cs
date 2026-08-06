using Microsoft.Extensions.Logging;
using TaskbarProgress.Core.Interfaces;
using TaskbarProgress.Core.Models;

namespace TaskbarProgress.Core.Services;

public class ProgressBarOrchestrator
{
    private readonly IEnumerable<IMetricsCollector> _collectors;
    private readonly IBarRenderer _renderer;
    private readonly ILogger<ProgressBarOrchestrator> _logger;
    private ProgressBarConfig _config;
    private CancellationTokenSource? _cts;
    private Task? _renderLoop;

    public ProgressBarConfig CurrentConfig => _config;

    public ProgressBarOrchestrator(
        IEnumerable<IMetricsCollector> collectors,
        IBarRenderer renderer,
        ProgressBarConfig config,
        ILogger<ProgressBarOrchestrator> logger)
    {
        _collectors = collectors;
        _renderer = renderer;
        _config = config;
        _logger = logger;
    }

    public void Start()
    {
        if (_renderLoop != null) return;
        
        _cts = new CancellationTokenSource();
        _renderer.Initialize(_config.BarSize);
        
        _logger.LogInformation("Starting MetricForge for {Metric}", _config.DisplayMetric);
        
        _renderLoop = Task.Run(() => RenderLoopAsync(_cts.Token));
    }

    private async Task RenderLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CollectAndRenderAsync();
                await Task.Delay(_config.UpdateIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in render loop");
            }
        }
    }

    private async Task CollectAndRenderAsync()
    {
        var tasks = _collectors.Select(c => c.CollectAsync(_cts!.Token));
        var results = await Task.WhenAll(tasks);
        
        var aggregated = AggregateMetrics(results);
        _renderer.Render(aggregated, _config);
    }

    private static SystemMetrics AggregateMetrics(SystemMetrics[] metrics) => new()
    {
        // Each collector returns one populated field and zero for the other
        // fields, so summing combines the independent collector results.
        CpuPercent = metrics.Sum(m => m.CpuPercent),
        MemoryPercent = metrics.Sum(m => m.MemoryPercent),
        NetworkKbps = metrics.Sum(m => m.NetworkKbps)
        ,NetworkDownloadKbps = metrics.Sum(m => m.NetworkDownloadKbps)
        ,NetworkUploadKbps = metrics.Sum(m => m.NetworkUploadKbps)
    };

    public void Stop()
    {
        _cts?.Cancel();
        _renderLoop = null;
        _renderer.Clear();
        _logger.LogInformation("MetricForge stopped");
    }

    public void UpdateConfig(ProgressBarConfig newConfig)
    {
        var wasRunning = _renderLoop != null;
        if (wasRunning) Stop();
        
        _config = newConfig;
        _renderer.UpdateConfiguration(newConfig);
        
        if (wasRunning) Start();
    }
}

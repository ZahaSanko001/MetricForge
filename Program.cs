using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskbarProgress.Core.Interfaces;
using TaskbarProgress.Core.Models;
using TaskbarProgress.Core.Services;
using TaskbarProgress.Infrastructure.Collectors;
using TaskbarProgress.Infrastructure.Renderers;
using TaskbarProgress.Presentation.Forms;

namespace TaskbarProgress;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        
        var services = new ServiceCollection();
        ConfigureServices(services);
        
        var serviceProvider = services.BuildServiceProvider();
        var trayApp = serviceProvider.GetRequiredService<TrayApplication>();
        
        Application.Run(trayApp);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        
        services.AddSingleton(new ProgressBarConfig());
        
        services.AddSingleton<ProgressBarOrchestrator>();
        
        services.AddSingleton<IMetricsCollector, CpuCollector>();
        services.AddSingleton<IMetricsCollector, MemoryCollector>();
        services.AddSingleton<IMetricsCollector, NetworkCollector>();
        
        services.AddSingleton<IBarRenderer, DwmBarRenderer>();
        
        services.AddSingleton<TrayApplication>();
    }
}
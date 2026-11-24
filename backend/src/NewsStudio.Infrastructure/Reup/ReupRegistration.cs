using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NewsStudio.Infrastructure.Reup;

public static class ReupRegistration
{
    public static IServiceCollection AddReupServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ReupPaths>();
        services.AddSingleton<ReupMedia>();
        services.AddSingleton<ReupEngine>();
        // redirects are followed by hand so each hop can be checked (never a private / local address)
        services.AddHttpClient<ReupStockClient>(c => c.Timeout = TimeSpan.FromMinutes(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.All });
        services.AddScoped<ReupService>();
        if (string.Equals(configuration["Worker:Role"], "pc", StringComparison.OrdinalIgnoreCase))
            services.AddHostedService<ReupPcMonitor>();
        return services;
    }
}

/// <summary>
/// Only on the owner's PC: tells the web page what the engine has (libraries, models, speed) shortly after start and
/// every 15 minutes, and removes work folders left by a run that was cut off.
/// </summary>
public class ReupPcMonitor : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ReupPcMonitor> _logger;

    public ReupPcMonitor(IServiceProvider services, ILogger<ReupPcMonitor> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
        var first = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var reup = scope.ServiceProvider.GetRequiredService<ReupService>();
                if (first) reup.CleanLeftoverWork(await reup.GetSettingsAsync(stoppingToken));
                await reup.ProbeEngineAsync(first, stoppingToken); // the speed test once per start (≈15 s of CPU)
                first = false;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Reup engine check failed: {Message}", ex.Message);
            }
            try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}

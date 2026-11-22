using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Hangfire;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServiceProvider _serviceProvider;

    public Worker(
        ILogger<Worker> logger,
        IConfiguration configuration,
        IHostApplicationLifetime lifetime,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _lifetime = lifetime;
        _serviceProvider = serviceProvider;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("NewsStudio.Worker is starting with Hangfire Background Server active.");

        var tempStoragePath = NewsStudio.Infrastructure.Common.TempStorage.GetRoot(_configuration);
        try
        {
            if (!Directory.Exists(tempStoragePath))
            {
                Directory.CreateDirectory(tempStoragePath);
                _logger.LogInformation("Created temporary storage directory at: {Path}", tempStoragePath);
            }
            else
            {
                _logger.LogInformation("Temporary storage directory verified at: {Path}", tempStoragePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize temp storage directory: {Path}", tempStoragePath);
        }

        _lifetime.ApplicationStopping.Register(() =>
        {
            _logger.LogInformation("Worker received SIGTERM/Stop signal. Gracefully waiting for in-flight Hangfire jobs to complete or pause...");
        });

        _lifetime.ApplicationStopped.Register(() =>
        {
            _logger.LogInformation("Worker stopped gracefully.");
        });

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker service loop is running. Awaiting jobs from Hangfire queues.");

        // 1. Startup: Recover dangling jobs from previous container crash/restart
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var recoveryService = scope.ServiceProvider.GetRequiredService<IJobRecoveryService>();
            var recoveryReport = await recoveryService.RecoverDanglingJobsAsync(stoppingToken);
            _logger.LogInformation("Worker Startup Recovery: Examined {Count} dangling jobs. Recovered to Review: {Awaiting}, Reset: {Reset}, Marked Failed: {Failed}",
                recoveryReport.TotalDanglingProjectsExamined,
                recoveryReport.RecoveredToAwaitingApprovalCount,
                recoveryReport.ResetToPendingCount,
                recoveryReport.MarkedFailedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute startup Job Recovery check.");
        }

        // 1b. "Tin đang hot": refresh RSS feeds on a schedule (default every 30 minutes) and once now
        try
        {
            var cron = _configuration["Trending:RefreshCron"] ?? "*/30 * * * *";
            var recurring = _serviceProvider.GetRequiredService<Hangfire.IRecurringJobManager>();
            recurring.AddOrUpdate<ITrendingNewsService>(
                "trending-news-refresh",
                service => service.RefreshAsync(CancellationToken.None),
                cron);
            _serviceProvider.GetRequiredService<Hangfire.IBackgroundJobClient>()
                .Enqueue<ITrendingNewsService>(service => service.RefreshAsync(CancellationToken.None));
            _logger.LogInformation("Trending news refresh scheduled ({Cron}) and queued once at startup.", cron);

            // "Tự động đăng": checks the daily posting slots every 2 minutes
            recurring.AddOrUpdate<IAutoPostService>(
                "auto-post-tick",
                service => service.TickAsync(CancellationToken.None),
                "*/2 * * * *");

            // Views of published Reels, per Facebook Page (dashboard)
            recurring.AddOrUpdate<IPageStatsService>(
                "facebook-stats-sync",
                service => service.SyncAsync(false, CancellationToken.None),
                "7,37 * * * *");

            recurring.RemoveIfExists("film-gpu-tick"); // GPU film pipeline removed (the AI video studio replaces it)

            // Daily "Điểm tin nóng" (one wall post + one Story per page, evening)
            recurring.AddOrUpdate<IDailyDigestService>(
                "daily-digest",
                service => service.TickAsync(CancellationToken.None),
                "*/5 * * * *");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to schedule trending news refresh.");
        }

        // 2. Main execution loop with periodic storage cleanup and heartbeat
        var cleanupIntervalMinutes = _configuration.GetValue<int>("Storage:CleanupIntervalMinutes", 15);
        var lastCleanupTime = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Heartbeat every 60 seconds
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);

                var proc = Process.GetCurrentProcess();
                var memMb = Math.Round(proc.WorkingSet64 / (1024.0 * 1024.0), 1);
                _logger.LogDebug("NewsStudio.Worker heartbeat: ready for tasks. Memory WorkingSet: {MemMb} MB", memMb);

                // Periodic storage quota & TTL enforcement
                if (DateTime.UtcNow - lastCleanupTime >= TimeSpan.FromMinutes(cleanupIntervalMinutes))
                {
                    lastCleanupTime = DateTime.UtcNow;
                    using var cleanupScope = _serviceProvider.CreateScope();
                    var cleanupService = cleanupScope.ServiceProvider.GetRequiredService<IStorageCleanupService>();
                    var report = await cleanupService.RunStorageCleanupAsync(stoppingToken);

                    _logger.LogInformation("Periodic Storage Cleanup: Freed {FreedMb} MB. Current usage: {UsageMb} MB / {LimitMb} MB (Within limits: {Status})",
                        report.TotalBytesFreedMb,
                        report.CurrentStorageUsedMb,
                        report.StorageLimitMb,
                        report.StorageWithinLimits);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Worker execution cancellation triggered.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in worker background service loop.");
            }
        }
    }
}

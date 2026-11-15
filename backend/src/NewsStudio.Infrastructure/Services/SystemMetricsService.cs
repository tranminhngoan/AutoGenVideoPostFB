using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Infrastructure.Services;

public class SystemMetricsService : ISystemMetricsService
{
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SystemMetricsService> _logger;
    private static readonly DateTime ServiceStartTime = DateTime.UtcNow;

    public SystemMetricsService(
        IApplicationDbContext context,
        IConfiguration configuration,
        ILogger<SystemMetricsService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<SystemMetricsDto> GetSystemMetricsAsync(CancellationToken cancellationToken = default)
    {
        var process = Process.GetCurrentProcess();
        var memoryWorkingSetMb = process.WorkingSet64 / (1024.0 * 1024.0);

        // Temp storage calculation
        var tempStoragePath = _configuration["Storage:TempStoragePath"] ?? "./tmp/newsstudio";
        var tempStorageLimitMb = _configuration.GetValue<double>("Storage:MaxTempStorageMb", 5120);
        double tempStorageUsedMb = 0;

        try
        {
            if (Directory.Exists(tempStoragePath))
            {
                var dirInfo = new DirectoryInfo(tempStoragePath);
                long totalBytes = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
                tempStorageUsedMb = totalBytes / (1024.0 * 1024.0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not accurately measure temp storage size at: {Path}", tempStoragePath);
        }

        var tempUsagePercent = tempStorageLimitMb > 0 
            ? Math.Round((tempStorageUsedMb / tempStorageLimitMb) * 100, 2) 
            : 0;

        // Database status & latency via lightweight query
        var sw = Stopwatch.StartNew();
        string dbStatus = "Connected";
        double latencyMs = 0;
        try
        {
            await _context.VideoProjects.Take(1).AnyAsync(cancellationToken);
            sw.Stop();
            latencyMs = sw.Elapsed.TotalMilliseconds;
        }
        catch (Exception ex)
        {
            sw.Stop();
            dbStatus = $"Disconnected ({ex.Message})";
            latencyMs = sw.Elapsed.TotalMilliseconds;
        }

        // Job queue status
        int activeJobs = 0, pendingJobs = 0, completedJobs = 0, failedJobs = 0;
        try
        {
            activeJobs = await _context.ProcessingJobs.CountAsync(j => j.Status == "Running", cancellationToken);
            pendingJobs = await _context.ProcessingJobs.CountAsync(j => j.Status == "Pending" || j.Status == "Enqueued", cancellationToken);
            completedJobs = await _context.ProcessingJobs.CountAsync(j => j.Status == "Completed", cancellationToken);
            failedJobs = await _context.ProcessingJobs.CountAsync(j => j.Status == "Failed", cancellationToken);
        }
        catch
        {
            // Fallback gracefully if database busy
        }

        return new SystemMetricsDto
        {
            CpuUsagePercent = Math.Round(Math.Min(100.0, process.TotalProcessorTime.TotalMilliseconds / (Environment.ProcessorCount * (DateTime.UtcNow - ServiceStartTime).TotalMilliseconds + 1) * 100), 2),
            MemoryWorkingSetMb = Math.Round(memoryWorkingSetMb, 2),
            TempStorageUsedMb = Math.Round(tempStorageUsedMb, 2),
            TempStorageLimitMb = tempStorageLimitMb,
            TempStorageUsagePercent = tempUsagePercent,
            TempStoragePath = tempStoragePath,
            ActiveJobsCount = activeJobs,
            PendingJobsCount = pendingJobs,
            CompletedJobsCount = completedJobs,
            FailedJobsCount = failedJobs,
            DatabaseStatus = dbStatus,
            DatabaseLatencyMs = Math.Round(latencyMs, 2),
            Uptime = DateTime.UtcNow - ServiceStartTime,
            ServerTimeUtc = DateTime.UtcNow,
            Environment = _configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production"
        };
    }

    public async Task<SystemCostSummaryDto> GetCostSummaryAsync(CancellationToken cancellationToken = default)
    {
        decimal totalAiCost = 0;
        long totalTokens = 0;
        int totalProjects = 0;
        int publishedProjects = 0;

        try
        {
            totalProjects = await _context.VideoProjects.CountAsync(cancellationToken);
            publishedProjects = await _context.VideoProjects.CountAsync(p => p.Status == VideoProjectStatus.Published, cancellationToken);

            var scripts = await _context.VideoScripts
                .Select(s => new { s.EstimatedCostUsd, s.TokenUsage })
                .ToListAsync(cancellationToken);

            totalAiCost = scripts.Sum(s => s.EstimatedCostUsd ?? 0m);
            totalTokens = scripts.Sum(s => (long)s.TokenUsage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute AI script costs");
        }

        // TTS costs calculation (EdgeTTS is free $0.00; ElevenLabs ~$0.0003 per char)
        var ttsProvider = _configuration["Tts:Provider"] ?? "EdgeTTS";
        long totalTtsChars = totalTokens * 4; // Approx 4 chars per token
        decimal totalTtsCost = ttsProvider.Equals("ElevenLabs", StringComparison.OrdinalIgnoreCase) 
            ? (totalTtsChars * 0.00015m) 
            : 0.00m;

        decimal avgCostPerProject = totalProjects > 0 
            ? Math.Round((totalAiCost + totalTtsCost) / totalProjects, 4) 
            : 0.00m;

        return new SystemCostSummaryDto
        {
            TotalAiCostUsd = Math.Round(totalAiCost, 4),
            TotalTtsCostUsd = Math.Round(totalTtsCost, 4),
            TotalProjectsCount = totalProjects,
            PublishedProjectsCount = publishedProjects,
            AverageCostPerProjectUsd = avgCostPerProject,
            TotalTokensUsed = totalTokens,
            TotalTtsCharactersSynthesized = totalTtsChars,
            PreferredAiProvider = _configuration["Ai:Provider"] ?? "Gemini",
            PreferredTtsProvider = ttsProvider
        };
    }
}

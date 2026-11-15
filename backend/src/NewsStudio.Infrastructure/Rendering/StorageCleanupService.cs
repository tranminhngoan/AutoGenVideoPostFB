using System;
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

namespace NewsStudio.Infrastructure.Rendering;

public class StorageCleanupService : IStorageCleanupService
{
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StorageCleanupService> _logger;

    public StorageCleanupService(
        IApplicationDbContext context,
        IConfiguration configuration,
        ILogger<StorageCleanupService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<StorageCleanupReportDto> RunStorageCleanupAsync(CancellationToken cancellationToken = default)
    {
        var tempStoragePath = _configuration["Storage:TempStoragePath"] ?? "./tmp/newsstudio";
        var maxStorageMb = _configuration.GetValue<double>("Storage:MaxTempStorageMb", 5120);
        long maxStorageBytes = (long)(maxStorageMb * 1024 * 1024);

        var report = new StorageCleanupReportDto
        {
            StorageLimitMb = maxStorageMb,
            ExecutedAtUtc = DateTime.UtcNow
        };

        _logger.LogInformation("StorageCleanupService: Starting routine storage cleanup at {Path} (Quota: {Quota} MB)...",
            tempStoragePath, maxStorageMb);

        // 1. Clean expired assets tracked in DB
        var expiredAssets = await _context.VideoAssets
            .Where(a => !a.IsCleanedUp && a.ExpiresAtUtc < DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        long freedBytes = 0;
        foreach (var asset in expiredAssets)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(asset.LocalTempPath) && File.Exists(asset.LocalTempPath))
                {
                    var fileInfo = new FileInfo(asset.LocalTempPath);
                    freedBytes += fileInfo.Length;
                    File.Delete(asset.LocalTempPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete expired asset file: {Path}", asset.LocalTempPath);
            }

            asset.IsCleanedUp = true;
            asset.Status = "CleanedUp";
            report.ExpiredAssetsCleanedCount++;
        }

        await _context.SaveChangesAsync(cancellationToken);

        // 2. Scan physical directory for quota overflow and unreferenced files
        try
        {
            if (Directory.Exists(tempStoragePath))
            {
                var dirInfo = new DirectoryInfo(tempStoragePath);
                var allFiles = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                    .OrderBy(fi => fi.CreationTimeUtc) // Oldest first
                    .ToList();

                long currentUsageBytes = allFiles.Sum(fi => fi.Length);

                // If over limit, delete oldest files until under 80% of quota
                long targetBytes = (long)(maxStorageBytes * 0.80);
                if (currentUsageBytes > maxStorageBytes)
                {
                    _logger.LogWarning("Temp storage ({UsageMb:F2} MB) exceeds quota ({QuotaMb} MB). Pruning oldest files...",
                        currentUsageBytes / (1024.0 * 1024.0), maxStorageMb);

                    foreach (var file in allFiles)
                    {
                        if (currentUsageBytes <= targetBytes)
                            break;

                        try
                        {
                            long fileLen = file.Length;
                            file.Delete();
                            freedBytes += fileLen;
                            currentUsageBytes -= fileLen;
                            report.OrphanedDirectoriesRemovedCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to prune file during quota enforcement: {File}", file.FullName);
                        }
                    }
                }

                // Clean empty project directories
                var subDirs = dirInfo.EnumerateDirectories("*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.FullName.Length); // Deepest first

                foreach (var dir in subDirs)
                {
                    try
                    {
                        if (!dir.EnumerateFileSystemInfos().Any())
                        {
                            dir.Delete();
                            report.OrphanedDirectoriesRemovedCount++;
                        }
                    }
                    catch
                    {
                        // Directory might be in active use
                    }
                }

                // Final measurement
                long finalUsageBytes = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
                report.CurrentStorageUsedMb = Math.Round(finalUsageBytes / (1024.0 * 1024.0), 2);
                report.StorageWithinLimits = finalUsageBytes <= maxStorageBytes;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning physical temp storage directory: {Path}", tempStoragePath);
        }

        report.TotalBytesFreedMb = Math.Round(freedBytes / (1024.0 * 1024.0), 2);

        _logger.LogInformation("Storage cleanup completed: Freed {FreedMb} MB. Current: {CurrentMb} MB / {LimitMb} MB. Within limits: {Status}",
            report.TotalBytesFreedMb,
            report.CurrentStorageUsedMb,
            report.StorageLimitMb,
            report.StorageWithinLimits);

        return report;
    }
}

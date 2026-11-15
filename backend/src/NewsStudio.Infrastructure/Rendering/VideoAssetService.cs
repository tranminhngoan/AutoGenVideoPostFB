using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Infrastructure.Rendering;

public class VideoAssetService : IVideoAssetService
{
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<VideoAssetService> _logger;

    public VideoAssetService(
        IApplicationDbContext context,
        IConfiguration configuration,
        ILogger<VideoAssetService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    public string GetProjectStorageDirectory(Guid projectId, string subfolder = "")
    {
        var tempBase = NewsStudio.Infrastructure.Common.TempStorage.GetRoot(_configuration);
        var dir = Path.Combine(tempBase, projectId.ToString());

        if (!string.IsNullOrWhiteSpace(subfolder))
        {
            dir = Path.Combine(dir, subfolder);
        }

        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<VideoAssetDto> RegisterAssetAsync(
        Guid projectId,
        string assetType,
        string localPath,
        string mimeType,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        // Always persist an absolute path: the file may be written by the Worker and served by the
        // API, which run from different working directories.
        localPath = Path.GetFullPath(localPath);
        var fileInfo = new FileInfo(localPath);
        var sizeBytes = fileInfo.Exists ? fileInfo.Length : 0;
        var hash = fileInfo.Exists ? await ComputeFileSha256Async(localPath, cancellationToken) : null;

        var defaultTtlHours = 2;
        if (int.TryParse(_configuration["Storage:TtlHours"], out var cfgHours) && cfgHours > 0)
        {
            defaultTtlHours = cfgHours;
        }

        var expiresAt = DateTime.UtcNow.Add(ttl ?? TimeSpan.FromHours(defaultTtlHours));

        var asset = new VideoAsset
        {
            VideoProjectId = projectId,
            AssetType = assetType,
            LocalTempPath = localPath,
            FileSizeBytes = sizeBytes,
            MimeType = mimeType,
            Sha256Hash = hash,
            ExpiresAtUtc = expiresAt,
            IsCleanedUp = false,
            Status = "Active"
        };

        _context.VideoAssets.Add(asset);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Registered VideoAsset {AssetId} ({AssetType}, {Size} bytes, expires at {Expires}) for Project {ProjectId}",
            asset.Id, assetType, sizeBytes, expiresAt, projectId);

        return MapToDto(asset);
    }

    public async Task<List<VideoAssetDto>> GetAssetsByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var assets = await _context.VideoAssets
            .AsNoTracking()
            .Where(a => a.VideoProjectId == projectId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return assets.Select(MapToDto).ToList();
    }

    public async Task<int> CleanupExpiredAssetsAsync(
        TimeSpan? defaultTtl = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expiredAssets = await _context.VideoAssets
            .Where(a => !a.IsCleanedUp && a.ExpiresAtUtc.HasValue && a.ExpiresAtUtc.Value <= now)
            .ToListAsync(cancellationToken);

        int count = 0;
        foreach (var asset in expiredAssets)
        {
            try
            {
                if (File.Exists(asset.LocalTempPath))
                {
                    File.Delete(asset.LocalTempPath);
                }

                asset.IsCleanedUp = true;
                asset.CleanedUpAtUtc = DateTime.UtcNow;
                asset.Status = "CleanedUp";
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete expired asset file at {Path}", asset.LocalTempPath);
            }
        }

        if (count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Cleaned up {Count} expired temporary video assets", count);
        }

        return count;
    }

    public async Task<bool> CleanupProjectAssetsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var projectAssets = await _context.VideoAssets
            .Where(a => a.VideoProjectId == projectId && !a.IsCleanedUp)
            .ToListAsync(cancellationToken);

        foreach (var asset in projectAssets)
        {
            try
            {
                if (File.Exists(asset.LocalTempPath))
                {
                    File.Delete(asset.LocalTempPath);
                }

                asset.IsCleanedUp = true;
                asset.CleanedUpAtUtc = DateTime.UtcNow;
                asset.Status = "CleanedUp";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete asset at {Path}", asset.LocalTempPath);
            }
        }

        // Also clean project temp directory if exists
        try
        {
            var tempBase = NewsStudio.Infrastructure.Common.TempStorage.GetRoot(_configuration);
            var projectDir = Path.Combine(tempBase, projectId.ToString());
            if (Directory.Exists(projectDir))
            {
                Directory.Delete(projectDir, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete project temp folder for {ProjectId}", projectId);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static VideoAssetDto MapToDto(VideoAsset asset)
    {
        return new VideoAssetDto
        {
            Id = asset.Id,
            VideoProjectId = asset.VideoProjectId,
            AssetType = asset.AssetType,
            LocalTempPath = asset.LocalTempPath,
            FileSizeBytes = asset.FileSizeBytes,
            MimeType = asset.MimeType,
            Sha256Hash = asset.Sha256Hash,
            ExpiresAtUtc = asset.ExpiresAtUtc,
            IsCleanedUp = asset.IsCleanedUp,
            CreatedAtUtc = asset.CreatedAtUtc
        };
    }

    private static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var bytes = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

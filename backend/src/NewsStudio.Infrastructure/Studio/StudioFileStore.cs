using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;

namespace NewsStudio.Infrastructure.Studio;

/// <summary>
/// Where the studio keeps its big files (generated clips, final videos): never the database (the Railway Postgres
/// volume is tiny). With a shared S3-compatible bucket configured (Storage:S3:*, e.g. Cloudflare R2), every file goes
/// there too, so it survives redeploys and the owner's PC and the cloud server see the same files; the app disk is
/// then only a cache. Without it, files live on the app disk only (wiped on redeploy).
/// A StudioBlob row whose Data is empty but SizeBytes &gt; 0 lives here. Folder: Studio:MediaPath (default the OS
/// temp dir), kept apart from the news temp folder, whose quota cleaner deletes old files.
/// </summary>
public class StudioFileStore
{
    private readonly string _root;
    private readonly Common.S3Storage? _s3;

    public StudioFileStore(IConfiguration configuration)
    {
        var configured = configuration["Studio:MediaPath"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? Path.Combine(Path.GetTempPath(), "studio-media") : configured);
        Directory.CreateDirectory(_root);
        _s3 = Common.S3Storage.FromConfiguration(configuration);
    }

    /// <summary>Files are kept in the shared bucket (they survive redeploys and are shared with the PC worker).</summary>
    public bool Shared => _s3 != null;

    private static string Key(Guid blobId) => $"studio/{blobId:N}.bin";

    public string PathFor(Guid blobId) => Path.Combine(_root, blobId.ToString("N") + ".bin");

    /// <summary>Whether the file exists here or in the shared bucket.</summary>
    public async Task<bool> ExistsAsync(Guid blobId, CancellationToken ct)
    {
        if (File.Exists(PathFor(blobId))) return true;
        if (_s3 == null) return false;
        try { return await _s3.ExistsAsync(Key(blobId), ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return true; } // bucket unreachable: don't call it lost
    }

    public async Task SaveAsync(Guid blobId, byte[] data, CancellationToken ct)
    {
        var path = PathFor(blobId);
        var tmp = path + ".part";
        await File.WriteAllBytesAsync(tmp, data, ct);
        File.Move(tmp, path, overwrite: true); // never leave a half-written clip under the real name
        if (_s3 != null) await _s3.PutAsync(Key(blobId), data, "application/octet-stream", ct);
    }

    /// <summary>
    /// Stores a file that is already on disk (big videos: never loaded whole into memory). With the shared bucket the
    /// file is uploaded and, unless <paramref name="keepLocal"/>, removed from this disk (the bucket is the copy);
    /// without it the file is moved into the store's folder.
    /// </summary>
    public async Task SaveFileAsync(Guid blobId, string sourcePath, string contentType, bool keepLocal, CancellationToken ct)
    {
        var path = PathFor(blobId);
        if (_s3 != null)
        {
            await _s3.PutFileAsync(Key(blobId), sourcePath, contentType, ct);
            if (keepLocal) File.Copy(sourcePath, path, overwrite: true);
            return;
        }
        File.Copy(sourcePath, path + ".part", overwrite: true);
        File.Move(path + ".part", path, overwrite: true);
    }

    /// <summary>Copies the stored file to <paramref name="destination"/> (streamed from the bucket when this disk lacks it). False when gone.</summary>
    public async Task<bool> CopyToAsync(Guid blobId, string destination, CancellationToken ct)
    {
        var path = PathFor(blobId);
        if (File.Exists(path))
        {
            File.Copy(path, destination, overwrite: true);
            return true;
        }
        return _s3 != null && await _s3.GetToFileAsync(Key(blobId), destination, ct);
    }

    public async Task<byte[]?> ReadAsync(Guid blobId, CancellationToken ct)
    {
        var path = PathFor(blobId);
        if (File.Exists(path)) return await File.ReadAllBytesAsync(path, ct);
        if (_s3 == null) return null;
        var data = await _s3.GetAsync(Key(blobId), ct);
        if (data != null) await CacheAsync(path, data, ct);
        return data;
    }

    /// <summary>The file on this machine's disk (fetched from the bucket when needed), or null when it is gone.</summary>
    public async Task<string?> LocalPathAsync(Guid blobId, CancellationToken ct)
    {
        var path = PathFor(blobId);
        if (File.Exists(path)) return path;
        if (_s3 == null) return null;
        try
        {
            return await _s3.GetToFileAsync(Key(blobId), path, ct) ? path : null; // streamed: a big video never sits whole in memory
        }
        catch (IOException) when (File.Exists(path))
        {
            return path; // another request cached it at the same time
        }
    }

    private static async Task CacheAsync(string path, byte[] data, CancellationToken ct)
    {
        try
        {
            var tmp = path + "." + Guid.NewGuid().ToString("N")[..6] + ".part";
            await File.WriteAllBytesAsync(tmp, data, ct);
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException) { /* another request cached it at the same time */ }
    }

    public void Delete(Guid blobId)
    {
        try { File.Delete(PathFor(blobId)); } catch { /* best effort */ }
        if (_s3 != null)
        {
            try { _s3.DeleteAsync(Key(blobId), CancellationToken.None).GetAwaiter().GetResult(); } catch { /* best effort */ }
        }
    }
}

/// <summary>
/// One-off at startup: moves video blobs still stored inside Postgres (older builds) out to <see cref="StudioFileStore"/>
/// and gives the space back to the volume (VACUUM FULL). Retries while the database is still coming up.
/// </summary>
public class StudioStorageMigrator : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<StudioStorageMigrator> _logger;

    public StudioStorageMigrator(IServiceProvider services, ILogger<StudioStorageMigrator> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        for (var attempt = 1; attempt <= 20 && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await MigrateAsync(stoppingToken);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Studio storage migration attempt {Attempt} failed; retrying in 1 min", attempt);
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task MigrateAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<StudioFileStore>();
        var ids = await db.StudioBlobs.Where(b => b.MimeType.StartsWith("video/") && b.Data.Length > 0).Select(b => b.Id).ToListAsync(ct);
        if (ids.Count == 0) return;

        long moved = 0;
        foreach (var id in ids) // one at a time: a clip is a few MB, never load them all
        {
            var blob = await db.StudioBlobs.FirstAsync(b => b.Id == id, ct);
            await store.SaveAsync(blob.Id, blob.Data, ct);
            moved += blob.Data.Length;
            blob.SizeBytes = blob.Data.Length;
            blob.Data = Array.Empty<byte>();
            await db.SaveChangesAsync(ct);
            if (db is DbContext tracked) tracked.ChangeTracker.Clear();
        }
        if (db is DbContext ctx)
            await ctx.Database.ExecuteSqlRawAsync("VACUUM FULL \"StudioBlobs\";", ct); // hand the freed pages back to the volume
        _logger.LogInformation("Studio: moved {Count} video files ({Mb:F0} MB) out of the database", ids.Count, moved / 1048576.0);
    }
}

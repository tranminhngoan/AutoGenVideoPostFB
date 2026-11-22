using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;

namespace NewsStudio.Infrastructure.Studio;

/// <summary>
/// Where the studio keeps its big files (generated clips, final videos): the app's own disk, never the database.
/// The Railway plan gives Postgres a tiny volume (≈0.5 GB) — a few videos stored as bytea filled it and crashed the
/// database. The disk is wiped on redeploy, which is acceptable: clips only matter until the video is posted.
/// A StudioBlob row whose Data is empty but SizeBytes &gt; 0 lives here. Folder: Studio:MediaPath (default the OS
/// temp dir), kept apart from the news temp folder, whose quota cleaner deletes old files.
/// </summary>
public class StudioFileStore
{
    private readonly string _root;

    public StudioFileStore(IConfiguration configuration)
    {
        var configured = configuration["Studio:MediaPath"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? Path.Combine(Path.GetTempPath(), "studio-media") : configured);
        Directory.CreateDirectory(_root);
    }

    public string PathFor(Guid blobId) => Path.Combine(_root, blobId.ToString("N") + ".bin");

    public bool Exists(Guid blobId) => File.Exists(PathFor(blobId));

    public async Task SaveAsync(Guid blobId, byte[] data, CancellationToken ct)
    {
        var path = PathFor(blobId);
        var tmp = path + ".part";
        await File.WriteAllBytesAsync(tmp, data, ct);
        File.Move(tmp, path, overwrite: true); // never leave a half-written clip under the real name
    }

    public async Task<byte[]?> ReadAsync(Guid blobId, CancellationToken ct) =>
        Exists(blobId) ? await File.ReadAllBytesAsync(PathFor(blobId), ct) : null;

    public void Delete(Guid blobId)
    {
        try { File.Delete(PathFor(blobId)); } catch { /* best effort */ }
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

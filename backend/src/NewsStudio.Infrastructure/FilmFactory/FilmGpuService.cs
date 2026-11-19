using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Common;

namespace NewsStudio.Infrastructure.FilmFactory;

/// <summary>
/// Free-GPU production path: still frames (RealVisXL + face reference + 2x upscale) and ~35% motion clips are
/// made on the GPU worker instead of paid Gemini images. One episode per batch; a recurring tick submits the
/// next queued episode, watches the running batch and, when it finishes, attaches the files and starts assembly.
/// </summary>
public class FilmGpuService
{
    public const string StatusQueued = "GpuQueued";
    public const string StatusRendering = "GpuRendering";

    private readonly IApplicationDbContext _context;
    private readonly IFilmGpuBackend _backend;
    private readonly IConfiguration _configuration;
    private readonly IBackgroundJobClient _jobs;
    private readonly IFilmRendererService _renderer;
    private readonly ILogger<FilmGpuService> _logger;

    public FilmGpuService(IApplicationDbContext context, IFilmGpuBackend backend, IConfiguration configuration,
        IBackgroundJobClient jobs, IFilmRendererService renderer, ILogger<FilmGpuService> logger)
    {
        _renderer = renderer;
        _context = context;
        _backend = backend;
        _configuration = configuration;
        _jobs = jobs;
        _logger = logger;
    }

    /// <summary>GPU path is used when enabled (default) and a backend account is configured.</summary>
    public bool Enabled => _configuration.GetValue("Film:Gpu:Enabled", true) && _backend.IsConfigured;

    private string Root => Path.Combine(TempStorage.GetRoot(_configuration), "film_factory");

    /// <summary>Recurring tick (every 2 minutes): finish the running batch, or start the next queued episode.</summary>
    [Queue("render")]
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(600)]
    public async Task TickAsync(CancellationToken ct = default)
    {
        if (!Enabled) return;

        var running = await _context.FilmEpisodes.Where(e => e.Status == StatusRendering).Select(e => e.Id).FirstOrDefaultAsync(ct);
        if (running != Guid.Empty)
        {
            await CheckRunningAsync(await LoadAsync(running, ct), ct);
            return;
        }

        var next = await _context.FilmEpisodes
            .Where(e => e.Status == StatusQueued)
            .OrderBy(e => e.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (next != null) await SubmitAsync(next.Id, ct);
    }

    private async Task CheckRunningAsync(FilmEpisode episode, CancellationToken ct)
    {
        var maxHours = _configuration.GetValue("Film:Gpu:MaxHours", 11.5);
        var (state, detail) = await _backend.GetStateAsync(episode.GpuBatchRef ?? "", ct);
        if (state is GpuBatchState.Queued or GpuBatchState.Running)
        {
            if (episode.GpuSubmittedAtUtc < DateTime.UtcNow.AddHours(-maxHours))
                await FailAsync(episode, $"GPU {_backend.Name} chạy quá {maxHours} giờ mà chưa xong ({detail}).", ct);
            return;
        }
        if (state == GpuBatchState.Failed)
        {
            await FailAsync(episode, $"Máy GPU {_backend.Name} báo lỗi: {detail}", ct);
            return;
        }

        try
        {
            var warnings = await IngestAsync(episode, ct);
            episode.Status = "GpuDone";
            episode.ErrorMessage = warnings;
            episode.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
            _jobs.Enqueue<FilmGpuService>(g => g.AssembleAsync(episode.Id, CancellationToken.None));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await FailAsync(episode, ex.Message, ct);
        }
    }

    /// <summary>Final step once the GPU files are in place (the renderer records any failure on the episode).</summary>
    [Queue("render")]
    [AutomaticRetry(Attempts = 0)]
    public async Task AssembleAsync(Guid episodeId, CancellationToken ct = default)
    {
        try
        {
            await _renderer.AssembleEpisodeAsync(episodeId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Assembly after GPU batch failed for episode {Id}", episodeId);
        }
    }

    /// <summary>Builds the job bundle for one episode (voices must already be recorded) and starts it on the GPU.</summary>
    public async Task SubmitAsync(Guid episodeId, CancellationToken ct)
    {
        var episode = await LoadAsync(episodeId, ct);
        try
        {
            var batchId = $"ep{episode.EpisodeNumber}-{DateTime.UtcNow:yyyyMMddHHmmss}";
            var bundleDir = Directory.CreateDirectory(Path.Combine(Root, "gpu", batchId, "bundle")).FullName;
            var jobs = BuildJobs(episode, bundleDir);
            await File.WriteAllTextAsync(Path.Combine(bundleDir, "jobs.json"),
                JsonSerializer.Serialize(new { batch_id = batchId, jobs }, new JsonSerializerOptions { WriteIndented = true }), ct);

            var reference = await _backend.SubmitAsync(bundleDir, batchId, ct);
            episode.GpuBatchRef = reference;
            episode.GpuSubmittedAtUtc = DateTime.UtcNow;
            episode.Status = StatusRendering;
            episode.ErrorMessage = null;
            episode.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Episode {Num}: GPU batch {Ref} started with {Count} jobs", episode.EpisodeNumber, reference, jobs.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await FailAsync(episode, ex.Message, ct);
        }
    }

    // ─────────────────────────────── Bundle ───────────────────────────────

    private List<Dictionary<string, object>> BuildJobs(FilmEpisode episode, string bundleDir)
    {
        var project = episode.FilmProject!;
        var landscape = project.AspectRatio == "16:9";
        var (gw, gh) = landscape ? (1344, 768) : (768, 1344);   // SDXL native size
        var (tw, th) = landscape ? (1920, 1080) : (1080, 1920); // delivered (after 2x upscale + fit)
        // No lighting words here: the scene prompt decides day/night (a "natural light" style tag turned night scenes into day).
        var style = $"{project.VisualArtStyle}, cinematic film still, photorealistic, sharp focus, detailed skin texture";
        var jobs = new List<Dictionary<string, object>>();
        var shots = Ordered(episode);
        var chars = project.Characters.ToDictionary(c => c.CharacterId, StringComparer.OrdinalIgnoreCase);

        // Face references: one portrait per character that appears alone in a frame (IP-Adapter applies one face to
        // everybody in the picture, so frames with several people rely on their written description only).
        var portraitName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in shots.Select(Characters).Where(c => c.Count == 1).Select(c => c[0]).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!chars.TryGetValue(id, out var c)) continue;
            var name = $"portrait_{Hash(c.Id + c.ConsistencyPrompt)}.jpg";
            portraitName[id] = name;
            if (!string.IsNullOrWhiteSpace(c.FrontReferenceImageUrl) && File.Exists(c.FrontReferenceImageUrl))
            {
                File.Copy(c.FrontReferenceImageUrl, Path.Combine(bundleDir, name), true);
                continue;
            }
            jobs.Add(new()
            {
                ["id"] = name, ["type"] = "image", ["out"] = name,
                ["width"] = 832, ["height"] = 1216, ["target_width"] = 832, ["target_height"] = 1216,
                ["seed"] = Seed(c.Id.ToString()),
                ["prompt"] = $"close-up portrait photo of a {c.ConsistencyPrompt}, facing the camera, plain grey studio background, soft even light, {style}"
            });
        }

        foreach (var group in shots.GroupBy(s => s.VisualPrompt))
        {
            var first = group.First();
            var name = $"frame_{Hash(group.Key + project.AspectRatio)}.jpg";
            var job = new Dictionary<string, object>
            {
                ["id"] = name, ["type"] = "image", ["out"] = name,
                ["width"] = gw, ["height"] = gh, ["target_width"] = tw, ["target_height"] = th,
                ["seed"] = Seed(group.Key),
                ["prompt"] = $"{group.Key}, {style}"
            };
            var present = Characters(first);
            if (present.Count == 1 && portraitName.TryGetValue(present[0], out var p))
            {
                job["face_ref"] = p;
                // Strong face guidance also copies the portrait's studio light/background; 0.45 keeps the face, frees the scene.
                job["face_strength"] = 0.45;
            }
            jobs.Add(job);
        }

        foreach (var shot in shots.Where(s => s.UseMotion))
        {
            jobs.Add(new()
            {
                ["id"] = $"motion_{shot.Id:N}", ["type"] = "motion", ["out"] = $"motion_{shot.Id:N}.mp4",
                ["image"] = $"frame_{Hash(shot.VisualPrompt + project.AspectRatio)}.jpg",
                ["prompt"] = (string.IsNullOrWhiteSpace(shot.MotionPrompt) ? "subtle natural movement" : shot.MotionPrompt!)
                             + ", smooth natural motion, faces stay natural and stable, cinematic",
                // A bit longer than the line: the renderer drops the clip's last 15% (where motion tends to smear).
                ["seconds"] = Math.Round(Math.Clamp(shot.DurationSeconds / 0.85, 2, 5), 1),
                ["width"] = landscape ? 832 : 480, ["height"] = landscape ? 480 : 832,
                ["seed"] = Seed(shot.Id.ToString())
            });
        }
        return jobs;
    }

    // ─────────────────────────────── Ingest ───────────────────────────────

    /// <summary>Moves the batch's files into place and links them to the shots. Returns a warning text (or null).</summary>
    private async Task<string?> IngestAsync(FilmEpisode episode, CancellationToken ct)
    {
        var batchId = (episode.GpuBatchRef ?? "").Split(':').ElementAtOrDefault(1) ?? "";
        var outDir = Path.Combine(Root, "gpu", batchId, "output");
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        await _backend.DownloadAsync(episode.GpuBatchRef!, outDir, ct);

        var resultsFile = Directory.GetFiles(outDir, "results.json", SearchOption.AllDirectories).FirstOrDefault()
                          ?? throw new InvalidOperationException("Máy GPU không trả về results.json — xem log notebook.");
        var filesDir = Path.GetDirectoryName(resultsFile)!;
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(resultsFile, ct));
        var root = doc.RootElement;
        var gotBatch = root.TryGetProperty("batch_id", out var b) ? b.GetString() : null;
        if (gotBatch != batchId)
            throw new InvalidOperationException($"Kết quả GPU thuộc lô khác ({gotBatch ?? "?"}), không phải {batchId}.");

        var jobResults = root.GetProperty("jobs");
        string? Ok(string id) =>
            jobResults.TryGetProperty(id, out var r) && r.GetProperty("status").GetString() == "ok"
                ? Path.Combine(filesDir, r.GetProperty("out").GetString()!)
                : null;
        string Error(string id) =>
            jobResults.TryGetProperty(id, out var r) && r.TryGetProperty("error", out var e) ? e.GetString() ?? "?" : "không có kết quả";

        var project = episode.FilmProject!;
        var shots = Ordered(episode);
        var framesDir = Directory.CreateDirectory(Path.Combine(Root, "frames")).FullName;
        var motionDir = Directory.CreateDirectory(Path.Combine(Root, "motion")).FullName;
        var charDir = Directory.CreateDirectory(Path.Combine(Root, "characters")).FullName;

        foreach (var c in project.Characters)
        {
            var name = $"portrait_{Hash(c.Id + c.ConsistencyPrompt)}.jpg";
            var file = Ok(name);
            if (file == null) continue;
            var dest = Path.Combine(charDir, name);
            File.Copy(file, dest, true);
            c.FrontReferenceImageUrl = dest;
        }

        var missingFrames = new List<string>();
        foreach (var group in shots.GroupBy(s => s.VisualPrompt))
        {
            var name = $"frame_{Hash(group.Key + project.AspectRatio)}.jpg";
            var file = Ok(name);
            if (file == null)
            {
                missingFrames.Add($"{name}: {Error(name)}");
                continue;
            }
            var dest = Path.Combine(framesDir, name);
            File.Copy(file, dest, true);
            foreach (var s in group)
            {
                s.RawImagePath = dest;
                s.RenderStatus = "Pending";
            }
        }
        if (missingFrames.Count > 0)
            throw new InvalidOperationException($"{missingFrames.Count} khung hình không vẽ được trên GPU: {string.Join(" | ", missingFrames.Take(3))}");

        var motionErrors = new List<string>();
        foreach (var s in shots.Where(s => s.UseMotion))
        {
            var id = $"motion_{s.Id:N}";
            var file = Ok(id);
            if (file == null)
            {
                motionErrors.Add(Error(id));
                s.MotionVideoPath = null;
                continue;
            }
            var dest = Path.Combine(motionDir, $"{s.Id:N}.mp4");
            File.Copy(file, dest, true);
            s.MotionVideoPath = dest;
            s.RenderStatus = "Pending";
        }
        await _context.SaveChangesAsync(ct);

        try { Directory.Delete(Path.Combine(Root, "gpu", batchId), true); } catch { /* best effort */ }

        var env = root.TryGetProperty("env", out var en) ? en.GetRawText() : "";
        _logger.LogInformation("Episode {Num}: GPU batch ingested ({Frames} frames, {Clips} clips, {Failed} clip errors) {Env}",
            episode.EpisodeNumber, shots.Select(s => s.VisualPrompt).Distinct().Count(), shots.Count(s => s.MotionVideoPath != null), motionErrors.Count, env);
        return motionErrors.Count == 0
            ? null
            : $"Cảnh báo: {motionErrors.Count} clip chuyển động lỗi nên dùng ảnh tĩnh thay thế ({motionErrors[0]}).";
    }

    // ─────────────────────────────── Helpers ───────────────────────────────

    private async Task<FilmEpisode> LoadAsync(Guid episodeId, CancellationToken ct) =>
        await _context.FilmEpisodes
            .Include(e => e.FilmProject).ThenInclude(p => p!.Characters)
            .Include(e => e.Scenes).ThenInclude(s => s.Shots)
            .FirstOrDefaultAsync(e => e.Id == episodeId, ct)
        ?? throw new ArgumentException("Không tìm thấy tập phim.");

    private async Task FailAsync(FilmEpisode episode, string message, CancellationToken ct)
    {
        _logger.LogError("Episode {Num} GPU step failed: {Message}", episode.EpisodeNumber, message);
        episode.Status = "Failed";
        episode.ErrorMessage = message.Length > 1900 ? message[..1900] : message;
        episode.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }

    private static List<FilmShot> Ordered(FilmEpisode e) =>
        e.Scenes.OrderBy(s => s.SceneNumber).SelectMany(s => s.Shots.OrderBy(x => x.ShotNumber)).ToList();

    private static List<string> Characters(FilmShot s) =>
        (s.CharacterIdsPresent ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..20].ToLowerInvariant();

    /// <summary>Stable seed so a re-run draws the same picture.</summary>
    private static int Seed(string s) => BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(s)), 0) & 0x7fffffff;
}

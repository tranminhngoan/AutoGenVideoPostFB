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
        if (string.IsNullOrWhiteSpace(episode.GpuBatchRef))
        {
            await FailAsync(episode, "Mất thông tin batch GPU (GpuBatchRef trống). Cần ấn dựng lại tập này.", ct);
            return;
        }

        var maxHours = _configuration.GetValue("Film:Gpu:MaxHours", 11.5);
        var (state, detail) = await _backend.GetStateAsync(episode.GpuBatchRef, ct);
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
                JsonSerializer.Serialize(new { batch_id = batchId, jobs, lipsync = LipSyncSpec(), qc = QcSpec() },
                    new JsonSerializerOptions { WriteIndented = true }), ct);

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

        // Face references: anchor portrait for every character in the project
        // (IP-Adapter Plus Face applies one face reference per frame).
        var portraitName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in project.Characters)
        {
            var name = $"portrait_{Hash(c.Id + c.ConsistencyPrompt)}.jpg";
            portraitName[c.CharacterId] = name;
            if (!string.IsNullOrWhiteSpace(c.FrontReferenceImageUrl) && File.Exists(c.FrontReferenceImageUrl))
            {
                File.Copy(c.FrontReferenceImageUrl, Path.Combine(bundleDir, name), true);
                continue;
            }

            var isFemale = (c.Gender ?? "").Contains("Nữ", StringComparison.OrdinalIgnoreCase)
                           || (c.Gender ?? "").Contains("Female", StringComparison.OrdinalIgnoreCase);
            var genderPositive = isFemale ? "1woman, feminine facial structure, female" : "1man, masculine facial structure, male";
            var genderNegative = isFemale
                ? "man, male, masculine, boy, beard, mustache, facial hair"
                : "woman, female, feminine, girl";

            jobs.Add(new()
            {
                ["id"] = name, ["type"] = "image", ["out"] = name,
                ["width"] = 832, ["height"] = 1216, ["target_width"] = 832, ["target_height"] = 1216,
                ["seed"] = Seed(c.Id.ToString()),
                ["prompt"] = $"close-up portrait photo of a {genderPositive}, {c.ConsistencyPrompt}, looking at camera, neutral plain grey studio background, soft cinematic lighting, {style}",
                ["negative"] = $"{genderNegative}, blurry, deformed face, bad anatomy, extra fingers, cartoon, 3d render"
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
            string? refPortrait = null;
            string? genderNeg = null;

            if (present.Count == 1 && portraitName.TryGetValue(present[0], out var p))
            {
                refPortrait = p;
                if (chars.TryGetValue(present[0], out var c1))
                {
                    var isFemale = (c1.Gender ?? "").Contains("Nữ", StringComparison.OrdinalIgnoreCase) || (c1.Gender ?? "").Contains("Female", StringComparison.OrdinalIgnoreCase);
                    genderNeg = isFemale ? "man, male, masculine, boy, beard, mustache" : "woman, female, feminine";
                }
            }
            else if (present.Count > 1 && !string.IsNullOrWhiteSpace(first.SpeakerCharacterId) && portraitName.TryGetValue(first.SpeakerCharacterId, out var spkP))
            {
                // In a multi-person frame, focus face conditioning on the speaking character
                refPortrait = spkP;
                if (chars.TryGetValue(first.SpeakerCharacterId, out var spkC))
                {
                    var isFemale = (spkC.Gender ?? "").Contains("Nữ", StringComparison.OrdinalIgnoreCase) || (spkC.Gender ?? "").Contains("Female", StringComparison.OrdinalIgnoreCase);
                    genderNeg = isFemale ? "man, male, masculine, boy, beard, mustache" : "woman, female, feminine";
                }
            }

            if (refPortrait != null)
            {
                job["face_ref"] = refPortrait;
                job["face_strength"] = 0.45;
            }
            if (genderNeg != null)
            {
                job["negative"] = $"{genderNeg}, blurry, deformed face, bad anatomy, extra limbs, cartoon, 3d render";
            }
            jobs.Add(job);
        }

        var lipSyncOn = LipSyncEnabled;
        foreach (var shot in shots.Where(s => s.UseMotion))
        {
            var sync = lipSyncOn && FilmShotPlanner.IsOnCameraSpeech(shot);
            var (motionPrompt, negative) = FilmShotPlanner.MotionPrompts(shot, lipSyncOn);
            jobs.Add(new()
            {
                ["id"] = $"motion_{shot.Id:N}", ["type"] = "motion", ["out"] = $"motion_{shot.Id:N}.mp4",
                ["image"] = $"frame_{Hash(shot.VisualPrompt + project.AspectRatio)}.jpg",
                ["prompt"] = motionPrompt,
                ["negative"] = negative,
                ["seconds"] = FilmShotPlanner.MotionSeconds(shot, sync),
                ["width"] = landscape ? 832 : 480, ["height"] = landscape ? 480 : 832,
                ["seed"] = Seed(shot.Id.ToString())
            });

            if (!sync) continue;
            // The approved master voice travels with the batch; the worker only reads it to drive the mouth.
            var voiceName = $"voice_{shot.Id:N}{Path.GetExtension(shot.AudioVoicePath)}";
            File.Copy(shot.AudioVoicePath!, Path.Combine(bundleDir, voiceName), true);
            jobs.Add(new()
            {
                ["id"] = LipSyncName(shot), ["type"] = "lipsync", ["out"] = LipSyncName(shot),
                ["video"] = $"motion_{shot.Id:N}.mp4",
                ["audio"] = voiceName,
                ["seed"] = Seed("lipsync" + shot.Id)
            });
        }
        return jobs;
    }

    /// <summary>Lip sync is opt-in until benchmark Test A passes on the rented GPU (Film:LipSync:Enabled).</summary>
    public bool LipSyncEnabled => _configuration.GetValue("Film:LipSync:Enabled", false);

    /// <summary>Output name carries a hash of the voice file, so a re-recorded line never reuses an old sync.</summary>
    private static string LipSyncName(FilmShot shot) => $"lipsync_{shot.Id:N}_{Hash(shot.AudioVoicePath ?? "")}.mp4";

    /// <summary>Lip-sync settings and QC thresholds sent with the batch (jobs.json "lipsync" / "qc").</summary>
    private object LipSyncSpec() => new
    {
        steps = _configuration.GetValue("Film:LipSync:Steps", 20),
        guidance = _configuration.GetValue("Film:LipSync:Guidance", 1.5),
        deepcache = _configuration.GetValue("Film:LipSync:DeepCache", true),
        max_attempts = _configuration.GetValue("Film:LipSync:MaxAttempts", 2)
    };

    private object QcSpec() => new
    {
        sync_conf_pass = _configuration.GetValue("Film:Qc:SyncConfPass", 6.0),
        sync_conf_reject = _configuration.GetValue("Film:Qc:SyncConfReject", 3.0),
        sync_offset_pass = _configuration.GetValue("Film:Qc:SyncOffsetPass", 1),
        sync_offset_reject = _configuration.GetValue("Film:Qc:SyncOffsetReject", 3)
    };

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
            s.VisualMethod = FilmShotPlanner.DefaultMethod;
            s.RenderStatus = "Pending";
        }
        if (motionErrors.Count > 0)
        {
            throw new InvalidOperationException($"Lỗi GPU không tạo được chuyển động video ({motionErrors.Count}/{shots.Count(s => s.UseMotion)} clips): {motionErrors.First()}");
        }

        // Lip sync: only shots that had a lipsync job in this batch. A failed sync stops the episode (a talking
        // mouth that does not match the voice is worse than none); QC verdicts are reported, not hidden.
        var syncErrors = new List<string>();
        var qcNotes = new List<string>();
        foreach (var s in shots)
        {
            var id = LipSyncName(s);
            if (!jobResults.TryGetProperty(id, out var r)) continue;
            var file = Ok(id);
            if (file == null)
            {
                syncErrors.Add($"shot {s.ShotNumber}: {Error(id)}");
                continue;
            }
            var dest = Path.Combine(motionDir, $"{s.Id:N}_lipsync.mp4");
            File.Copy(file, dest, true);
            s.MotionVideoPath = dest;
            s.VisualMethod = FilmShotPlanner.LipSyncMethod;
            s.RenderStatus = "Pending";

            if (r.TryGetProperty("qc_sync", out var q))
            {
                var verdict = q.TryGetProperty("verdict", out var v) ? v.GetString() : "NOT_MEASURED";
                var conf = q.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble().ToString("0.00") : "?";
                var off = q.TryGetProperty("av_offset_frames", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32().ToString() : "?";
                _logger.LogInformation("Episode {Num} shot {Shot}: lip-sync QC {Verdict} (SyncNet conf {Conf}, offset {Off} frames)",
                    episode.EpisodeNumber, s.ShotNumber, verdict, conf, off);
                if (verdict != "PASS")
                    qcNotes.Add($"cảnh {s.FilmScene?.SceneNumber}/shot {s.ShotNumber}: {verdict} (conf {conf}, lệch {off} khung)");
            }
        }
        if (syncErrors.Count > 0)
            throw new InvalidOperationException($"Lỗi khớp khẩu hình ({syncErrors.Count} shot): {string.Join(" | ", syncErrors.Take(3))}");
        await _context.SaveChangesAsync(ct);

        try { Directory.Delete(Path.Combine(Root, "gpu", batchId), true); } catch { /* best effort */ }

        var env = root.TryGetProperty("env", out var en) ? en.GetRawText() : "";
        _logger.LogInformation("Episode {Num}: GPU batch ingested ({Frames} frames, {Clips} clips, {Failed} clip errors) {Env}",
            episode.EpisodeNumber, shots.Select(s => s.VisualPrompt).Distinct().Count(), shots.Count(s => s.MotionVideoPath != null), motionErrors.Count, env);
        // QC thresholds are provisional until calibrated on benchmark Test A, so non-PASS shots are reported
        // for a human check instead of silently passing or blocking the episode.
        return qcNotes.Count == 0
            ? null
            : $"Kiểm tra khẩu hình (SyncNet) chưa đạt ở {qcNotes.Count} shot — cần xem lại bằng mắt: {string.Join("; ", qcNotes.Take(6))}";
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

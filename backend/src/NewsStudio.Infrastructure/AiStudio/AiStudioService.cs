using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Infrastructure.AiStudio;

/// <summary>
/// AI Video Studio: upload a reference video → the owner's PC analyses it locally (FFmpeg + OpenCV measurements and a
/// local Qwen3-VL model, no paid analysis) → scenes, timeline, character / environment / style bibles → per-scene
/// Seedance prompts the user can edit (with versions) → new clips made by the project's existing Seedance integration
/// (same key, same cost records and monthly budget as the video studio) → preview, download, history.
/// Heavy analysis runs only in the Hangfire queue <see cref="PcQueue"/>, which only the PC listens to.
/// </summary>
public partial class AiStudioService
{
    public const string PcQueue = "pc_local";
    public const long MaxSourceBytes = 500L * 1024 * 1024;
    public const double MinSourceSeconds = 2;
    public const double MaxSourceSeconds = 180;
    public const long MaxImageBytes = 15L * 1024 * 1024;
    private static readonly string[] AnalysisRunning = { "Queued", "Analyzing" };
    private static readonly string[] GenRunning = { "Queued", "Generating", "Recovering" };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    /// <summary>One analysis at a time on the PC: the local model uses the whole graphics card.</summary>
    private static readonly SemaphoreSlim AnalysisLock = new(1, 1);

    private readonly IApplicationDbContext _db;
    private readonly StudioFileStore _files;
    private readonly AiStudioMedia _media;
    private readonly AiStudioEngine _engine;
    private readonly SeedanceVideoClient _video;
    private readonly StudioService _studio;
    private readonly IAiStudioNarrator _voices;
    private readonly IAiStudioCaptionWriter _captions;
    private readonly StudioMedia _brandMedia;
    private readonly IAiStudioTextWriter _writer;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<AiStudioService> _logger;

    public AiStudioService(IApplicationDbContext db, StudioFileStore files, AiStudioMedia media, AiStudioEngine engine, SeedanceVideoClient video,
        StudioService studio, IAiStudioNarrator voices, IAiStudioCaptionWriter captions, StudioMedia brandMedia, IAiStudioTextWriter writer, IBackgroundJobClient jobs, ILogger<AiStudioService> logger)
    {
        _db = db;
        _files = files;
        _media = media;
        _engine = engine;
        _video = video;
        _studio = studio;
        _voices = voices;
        _captions = captions;
        _brandMedia = brandMedia;
        _writer = writer;
        _jobs = jobs;
        _logger = logger;
    }

    // ═══════════════════════════════ files ═══════════════════════════════

    private async Task<StudioBlob> StoreFileAsync(string path, string mime, CancellationToken ct)
    {
        var blob = new StudioBlob { MimeType = mime, Data = Array.Empty<byte>(), SizeBytes = new FileInfo(path).Length };
        await _files.SaveFileAsync(blob.Id, path, mime, keepLocal: false, ct);
        _db.StudioBlobs.Add(blob);
        return blob;
    }

    private async Task DeleteBlobAsync(Guid? id, CancellationToken ct)
    {
        if (id is not { } blobId) return;
        _files.Delete(blobId);
        var row = await _db.StudioBlobs.FirstOrDefaultAsync(b => b.Id == blobId, ct);
        if (row != null) _db.StudioBlobs.Remove(row);
    }

    /// <summary>A direct bucket link (the browser streams from R2, not through Railway), or null to serve it from here.</summary>
    public async Task<string?> BucketUrlAsync(Guid? blobId, string? downloadName, CancellationToken ct)
    {
        if (blobId is not { } id) return null;
        var mime = await _db.StudioBlobs.AsNoTracking().Where(b => b.Id == id).Select(b => b.MimeType).FirstOrDefaultAsync(ct);
        return mime == null ? null : await _files.PresignedUrlAsync(id, mime, downloadName, ct);
    }

    public async Task<(string Path, string MimeType)?> FileAsync(Guid? blobId, CancellationToken ct)
    {
        if (blobId is not { } id) return null;
        var mime = await _db.StudioBlobs.AsNoTracking().Where(b => b.Id == id).Select(b => b.MimeType).FirstOrDefaultAsync(ct);
        if (mime == null) return null;
        var path = await _files.LocalPathAsync(id, ct);
        return path == null ? null : (path, mime);
    }

    /// <summary>A keyframe is served only when it belongs to that scene (ids are not enough to read any studio file).</summary>
    public async Task<Guid?> FrameBlobAsync(Guid sceneId, Guid blobId, CancellationToken ct)
    {
        var frames = await _db.AiStudioScenes.AsNoTracking().Where(s => s.Id == sceneId).Select(s => s.FramesJson).FirstOrDefaultAsync(ct);
        return frames != null && ReadFrames(frames).Any(f => f.BlobId == blobId) ? blobId : null;
    }

    public async Task<Guid?> ProjectBlobAsync(Guid id, string which, CancellationToken ct)
    {
        var p = await _db.AiStudioProjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) return null;
        return which switch
        {
            "source" => p.SourceDeleted ? null : p.SourceBlobId,
            "thumb" => p.SourceThumbBlobId,
            "reference" => p.ReferenceImageBlobId,
            "final" => p.FinalBlobId,
            "narration" => p.NarrationBlobId,
            _ => null
        };
    }

    public async Task<Guid?> SceneThumbAsync(Guid sceneId, CancellationToken ct) =>
        await _db.AiStudioScenes.AsNoTracking().Where(s => s.Id == sceneId).Select(s => s.ThumbBlobId).FirstOrDefaultAsync(ct);

    public async Task<Guid?> ClipBlobAsync(Guid generationId, CancellationToken ct) =>
        await _db.AiStudioGenerations.AsNoTracking().Where(g => g.Id == generationId && !g.FilesDeleted).Select(g => g.ClipBlobId).FirstOrDefaultAsync(ct);

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ai-studio", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void DeleteDir(string? dir)
    {
        if (dir == null) return;
        for (var i = 0; i < 3; i++)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); return; }
            catch (IOException) { Thread.Sleep(500); }
            catch (UnauthorizedAccessException) { Thread.Sleep(500); }
        }
    }

    // ═══════════════════════════════ projects ═══════════════════════════════

    public async Task<List<AiStudioProjectSummaryDto>> ListProjectsAsync(CancellationToken ct)
    {
        var rows = await _db.AiStudioProjects.AsNoTracking().OrderByDescending(p => p.CreatedAtUtc).Take(200).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var gens = await _db.AiStudioGenerations.AsNoTracking().Where(g => ids.Contains(g.ProjectId))
            .GroupBy(g => g.ProjectId)
            .Select(g => new { g.Key, Done = g.Count(x => x.Status == "Done"), Cost = g.Sum(x => x.CostUsd) })
            .ToDictionaryAsync(x => x.Key, ct);
        return rows.Select(p =>
        {
            var dto = new AiStudioProjectSummaryDto();
            FillSummary(dto, p);
            if (gens.TryGetValue(p.Id, out var g)) { dto.ClipsDone = g.Done; dto.CostUsd = Math.Round(g.Cost, 4); }
            return dto;
        }).ToList();
    }

    public async Task<AiStudioProjectDto> GetProjectAsync(Guid id, CancellationToken ct)
    {
        var p = await _db.AiStudioProjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        var scenes = await _db.AiStudioScenes.AsNoTracking().Where(s => s.ProjectId == id).OrderBy(s => s.Order).ToListAsync(ct);
        var gens = await _db.AiStudioGenerations.AsNoTracking().Where(g => g.ProjectId == id).OrderByDescending(g => g.CreatedAtUtc).ToListAsync(ct);
        return ToDto(p, scenes, gens);
    }

    /// <summary>A new project from an uploaded reference video (already on this server's disk): checked, probed, stored.</summary>
    public async Task<AiStudioProjectDto> CreateProjectAsync(string path, string fileName, string? title, CancellationToken ct, string? topic = null)
    {
        var p = new AiStudioProject { Topic = AiStudioTopics.Normalize(topic) };
        await ApplySourceAsync(p, path, fileName, ct);
        p.AudioMode = DefaultAudioMode(p);
        p.Title = !string.IsNullOrWhiteSpace(title) ? title.Trim() : Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(p.Title)) p.Title = "Video tham chiếu";
        if (p.Title.Length > 300) p.Title = p.Title[..300];
        var settings = await AiStudioSettingsStore.GetAsync(_db, ct);
        p.AnalysisModel = settings.DefaultModel;
        p.VideoModel = _video.DefaultModel;
        p.NegativePrompt = AiStudioPromptEngine.DefaultNegative;
        _db.AiStudioProjects.Add(p);
        await _db.SaveChangesAsync(ct);
        return await GetProjectAsync(p.Id, ct);
    }

    /// <summary>Replaces the reference video: the old analysis, scenes and prompts go (generated clips stay in the history).</summary>
    public async Task<AiStudioProjectDto> ReplaceSourceAsync(Guid id, string path, string fileName, CancellationToken ct)
    {
        var p = await EditableProjectAsync(id, ct);
        if (await _db.AiStudioGenerations.AnyAsync(g => g.ProjectId == id && GenRunning.Contains(g.Status), ct))
            throw new InvalidOperationException("Đang tạo clip cho dự án này — chờ xong rồi mới thay video.");
        var oldSource = p.SourceBlobId;
        var oldThumb = p.SourceThumbBlobId;
        await ApplySourceAsync(p, path, fileName, ct);
        await DeleteBlobAsync(oldSource, ct);
        await DeleteBlobAsync(oldThumb, ct);
        await ClearAnalysisAsync(p, ct);
        await _db.SaveChangesAsync(ct);
        return await GetProjectAsync(id, ct);
    }

    private async Task ApplySourceAsync(AiStudioProject p, string path, string fileName, CancellationToken ct)
    {
        var size = new FileInfo(path).Length;
        if (size == 0) throw new ArgumentException("File rỗng.");
        if (size > MaxSourceBytes) throw new ArgumentException($"Video quá lớn ({size / 1048576.0:0} MB) — tối đa {MaxSourceBytes / 1048576} MB.");
        var container = await AiStudioMedia.SniffFileAsync(path, ct)
                        ?? throw new ArgumentException("File không phải video MP4 / MOV / WEBM (đổi đuôi file không đổi được định dạng thật).");
        var probe = await _media.ProbeAsync(path, ct);
        if (!probe.HasVideo) throw new ArgumentException("File không có hình (chỉ có âm thanh?).");
        if (probe.Duration < MinSourceSeconds) throw new ArgumentException($"Video quá ngắn ({probe.Duration:0.0}s) — tối thiểu {MinSourceSeconds:0} giây.");
        if (probe.Duration > MaxSourceSeconds) throw new ArgumentException($"Video dài {probe.Duration:0}s — tối đa {MaxSourceSeconds / 60:0} phút cho một lần phân tích. Cắt bớt rồi tải lên lại.");
        if (probe.Width < 160 || probe.Height < 160) throw new ArgumentException($"Video quá nhỏ ({probe.Width}×{probe.Height}).");
        if (string.IsNullOrWhiteSpace(probe.VideoCodec)) throw new ArgumentException("Không nhận ra codec video.");
        await _media.CheckDecodesAsync(path, probe.Duration, ct);

        var thumbPath = path + ".thumb.jpg";
        try
        {
            await _media.ThumbnailAsync(path, thumbPath, Math.Min(1, probe.Duration / 2), 480, ct);
            p.SourceThumbBlobId = (await StoreFileAsync(thumbPath, "image/jpeg", ct)).Id;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("AI Studio thumbnail failed: {Message}", ex.Message);
            p.SourceThumbBlobId = null;
        }
        finally
        {
            if (File.Exists(thumbPath)) File.Delete(thumbPath);
        }
        // the sound alone (small) outlives the video: the joined video can keep the original narration later
        var oldAudio = p.SourceAudioBlobId;
        p.SourceAudioBlobId = null;
        if (probe.Audio.Count > 0)
        {
            var audioPath = path + ".audio.m4a";
            try
            {
                await _media.ExtractAudioAsync(path, audioPath, ct);
                p.SourceAudioBlobId = (await StoreFileAsync(audioPath, "audio/mp4", ct)).Id;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("AI Studio audio extract failed: {Message}", ex.Message);
            }
            finally
            {
                if (File.Exists(audioPath)) File.Delete(audioPath);
            }
        }
        await DeleteBlobAsync(oldAudio, ct);
        var mime = container switch { "webm" => "video/webm", "mov" => "video/quicktime", _ => "video/mp4" };
        p.SourceBlobId = (await StoreFileAsync(path, mime, ct)).Id;
        p.SourceDeleted = false;
        p.SourceFileName = Path.GetFileName(fileName);
        if (p.SourceFileName.Length > 300) p.SourceFileName = p.SourceFileName[^300..];
        p.SourceContainer = container;
        p.SourceSizeBytes = size;
        p.DurationSeconds = Math.Round(probe.Duration, 3);
        p.Width = probe.Width;
        p.Height = probe.Height;
        p.FpsText = probe.FpsText;
        p.Fps = Math.Round(probe.Fps, 3);
        p.VideoCodec = probe.VideoCodec;
        p.BitrateKbps = probe.BitrateKbps;
        p.AspectRatio = AiStudioMedia.AspectOf(probe.Width, probe.Height);
        p.GenAspectRatio = AiStudioMedia.GenerationAspect(probe.Width, probe.Height);
        p.HasAudio = probe.Audio.Count > 0;
        p.AudioJson = JsonSerializer.Serialize(probe.Audio, Json);
        if (!p.HasAudio || (p.AudioMode == "Original" && p.SourceAudioBlobId == null)) p.AudioMode = "None";
    }

    /// <summary>None = silent (music picked on Facebook); Narration = the narration read again by an AI voice; Original = the reference's own sound.</summary>
    public static readonly string[] AudioModes = { "None", "Narration", "Original" };

    /// <summary>The theme's default sound: the childhood-memories channel re-reads the video's narration with an AI voice, others are silent.</summary>
    public static string DefaultAudioMode(AiStudioProject p) =>
        p.Topic == AiStudioTopics.VietnamChildhood && p.HasAudio ? "Narration" : "None";

    private async Task ClearAnalysisAsync(AiStudioProject p, CancellationToken ct)
    {
        var scenes = await _db.AiStudioScenes.Where(s => s.ProjectId == p.Id).ToListAsync(ct);
        foreach (var s in scenes)
        {
            await DeleteBlobAsync(s.ThumbBlobId, ct);
            await DeleteBlobAsync(s.VoiceBlobId, ct); // per-scene takes of earlier versions
            foreach (var f in ReadFrames(s.FramesJson)) await DeleteBlobAsync(f.BlobId, ct);
        }
        _db.AiStudioScenes.RemoveRange(scenes);
        _db.AiStudioPromptVersions.RemoveRange(await _db.AiStudioPromptVersions.Where(v => v.ProjectId == p.Id && v.SceneId != null).ToListAsync(ct));
        await DeleteBlobAsync(p.NarrationBlobId, ct);
        (p.NarrationBlobId, p.NarrationSeconds, p.NarrationKey) = (null, null, null);
        p.Status = "Draft";
        p.Stage = null;
        p.ProgressText = null;
        p.ProgressPercent = 0;
        p.ScenesDone = p.ScenesTotal = 0;
        p.ErrorMessage = null;
        p.AnalysisMetaJson = "{}";
        p.OverviewJson = "{}";
        p.CharacterBibleJson = "[]";
        p.EnvironmentBibleJson = "[]";
        p.StyleBibleJson = "{}";
        p.PaletteJson = "[]";
        p.TranscriptJson = null;
        p.AnalysisStartedAtUtc = p.AnalysisFinishedAtUtc = null;
    }

    private async Task<AiStudioProject> EditableProjectAsync(Guid id, CancellationToken ct)
    {
        var p = await _db.AiStudioProjects.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        if (AnalysisRunning.Contains(p.Status)) throw new InvalidOperationException("Đang phân tích video — huỷ trước khi sửa.");
        return p;
    }

    public async Task<AiStudioProjectDto> UpdateProjectAsync(Guid id, AiStudioProjectUpdateDto input, CancellationToken ct)
    {
        var p = await _db.AiStudioProjects.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        if (!string.IsNullOrWhiteSpace(input.Title)) p.Title = input.Title.Trim().Length > 300 ? input.Title.Trim()[..300] : input.Title.Trim();
        if (input.AnalysisModel != null)
        {
            if (AnalysisRunning.Contains(p.Status)) throw new InvalidOperationException("Đang phân tích — không đổi model được lúc này.");
            p.AnalysisModel = AiStudioSettingsStore.NormalizeModel(input.AnalysisModel);
        }
        await _db.SaveChangesAsync(ct);
        return await GetProjectAsync(id, ct);
    }

    public async Task DeleteProjectAsync(Guid id, CancellationToken ct)
    {
        var p = await _db.AiStudioProjects.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        if (AnalysisRunning.Contains(p.Status)) throw new InvalidOperationException("Đang phân tích — huỷ trước khi xoá.");
        if (await _db.AiStudioGenerations.AnyAsync(g => g.ProjectId == id && GenRunning.Contains(g.Status), ct))
            throw new InvalidOperationException("Đang tạo clip — chờ xong rồi mới xoá.");
        await ClearAnalysisAsync(p, ct);
        foreach (var g in await _db.AiStudioGenerations.Where(g => g.ProjectId == id).ToListAsync(ct))
        {
            await DeleteBlobAsync(g.ClipBlobId, ct);
            _db.AiStudioGenerations.Remove(g);
        }
        _db.AiStudioPromptVersions.RemoveRange(await _db.AiStudioPromptVersions.Where(v => v.ProjectId == id).ToListAsync(ct));
        foreach (var b in new[] { p.SourceBlobId, p.SourceThumbBlobId, p.ReferenceImageBlobId, p.FinalBlobId, p.SourceAudioBlobId, p.NarrationBlobId }) await DeleteBlobAsync(b, ct);
        _db.AiStudioProjects.Remove(p);
        await _db.SaveChangesAsync(ct);
    }

    // ═══════════════════════════════ analysis ═══════════════════════════════

    public async Task<AiStudioProjectDto> StartAnalysisAsync(Guid id, bool force, CancellationToken ct)
    {
        var p = await EditableProjectAsync(id, ct);
        if (p.SourceDeleted || p.SourceBlobId == null) throw new InvalidOperationException("Video tham chiếu đã bị xoá khỏi kho (hết hạn lưu) — tải lên lại.");
        if (p.Status == "Analyzed" && !force)
            throw new InvalidOperationException("Video đã được phân tích — kết quả vẫn dùng được. Bấm \"Phân tích lại\" nếu muốn chạy lại.");
        if (await _db.AiStudioGenerations.AnyAsync(g => g.ProjectId == id && GenRunning.Contains(g.Status), ct))
            throw new InvalidOperationException("Đang tạo clip cho dự án này — chờ xong rồi mới phân tích lại.");
        p.Status = "Queued";
        p.CancelRequested = false;
        p.ErrorMessage = null;
        p.Stage = "queued";
        p.ProgressPercent = 0;
        p.ScenesDone = 0;
        p.ProgressText = "Chờ máy tính nhận việc…";
        await _db.SaveChangesAsync(ct);
        p.HangfireJobId = _jobs.Enqueue<AiStudioService>(s => s.RunAnalysisAsync(id, CancellationToken.None));
        await _db.SaveChangesAsync(ct);
        return await GetProjectAsync(id, ct);
    }

    public async Task<AiStudioProjectDto> CancelAnalysisAsync(Guid id, CancellationToken ct)
    {
        var p = await _db.AiStudioProjects.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        if (!AnalysisRunning.Contains(p.Status)) return await GetProjectAsync(id, ct);
        p.CancelRequested = true;
        if (p.Status == "Queued")
        {
            p.Status = await _db.AiStudioScenes.AnyAsync(s => s.ProjectId == id, ct) ? "Analyzed" : "Cancelled";
            p.ProgressText = "Đã huỷ.";
        }
        else p.ProgressText = "Đang huỷ…";
        await _db.SaveChangesAsync(ct);
        if (p.HangfireJobId != null && p.Status != "Analyzing")
        {
            try { _jobs.Delete(p.HangfireJobId); } catch (Exception ex) { _logger.LogInformation("AI Studio cancel: {Message}", ex.Message); }
        }
        return await GetProjectAsync(id, ct);
    }

    /// <summary>Analyses the reference video on the PC (queue <see cref="PcQueue"/>).</summary>
    [Queue(PcQueue)]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task RunAnalysisAsync(Guid id, CancellationToken ct)
    {
        if (!await AnalysisLock.WaitAsync(0, ct))
        {
            var waiting = await _db.AiStudioProjects.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (waiting is { Status: "Queued" })
            {
                waiting.ProgressText = "Chờ video trước phân tích xong (máy tính làm lần lượt)…";
                await _db.SaveChangesAsync(ct);
            }
            await AnalysisLock.WaitAsync(ct);
        }
        try
        {
            await RunAnalysisCoreAsync(id, ct);
        }
        finally
        {
            AnalysisLock.Release();
        }
    }

    private async Task RunAnalysisCoreAsync(Guid id, CancellationToken ct)
    {
        var p = await _db.AiStudioProjects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) return;
        if (p.Status != "Queued" || p.CancelRequested)
        {
            if (p.Status == "Queued") { p.Status = "Cancelled"; p.ProgressText = "Đã huỷ."; await _db.SaveChangesAsync(CancellationToken.None); }
            return;
        }
        var settings = await AiStudioSettingsStore.GetAsync(_db, ct);
        p.Status = "Analyzing";
        p.Stage = "preparing";
        p.ProgressText = "Đang chuẩn bị video trên máy tính…";
        p.ProgressPercent = 1;
        p.AnalysisStartedAtUtc = DateTime.UtcNow;
        p.AnalysisFinishedAtUtc = null;
        p.WorkerMachine = Environment.MachineName;
        await _db.SaveChangesAsync(ct);

        // the project's work folder stays after a failure / cancel so the next run resumes from the finished scenes
        var root = _engine.Paths.WorkRoot(settings);
        var work = Path.Combine(root, id.ToString("N"));
        var finished = false;
        try
        {
            if (!_engine.Paths.Installed) throw new InvalidOperationException(AiStudioPaths.NotInstalled);
            Directory.CreateDirectory(work);
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
            var need = p.SourceSizeBytes * 2 + 1024L * 1024 * 1024;
            if (drive.AvailableFreeSpace < need)
                throw new InvalidOperationException($"Ổ {drive.Name} chỉ còn {drive.AvailableFreeSpace / 1073741824.0:0.0} GB trống — cần khoảng {need / 1073741824.0:0.0} GB.");
            var source = Path.Combine(work, "source." + p.SourceContainer);
            if (!File.Exists(source) || new FileInfo(source).Length != p.SourceSizeBytes)
                if (!await _files.CopyToAsync(p.SourceBlobId!.Value, source, ct)) throw new InvalidOperationException("Video tham chiếu đã mất khỏi kho file — tải lên lại.");
            var framesDir = Path.Combine(work, "frames");
            DeleteDir(framesDir);

            var threads = settings.Threads > 0 ? settings.Threads : Math.Clamp(Environment.ProcessorCount - 2, 2, 14);
            var job = new JsonObject
            {
                ["ffmpeg"] = _media.Ffmpeg, ["home"] = _engine.Paths.Home, ["input"] = source, ["work"] = work,
                ["duration"] = p.DurationSeconds, ["width"] = p.Width, ["height"] = p.Height, ["model"] = p.AnalysisModel,
                ["threads"] = threads, ["gpu"] = settings.Gpu, ["hasAudio"] = p.HasAudio, ["transcribe"] = settings.Transcribe,
                ["maxFramesPerScene"] = settings.MaxFramesPerScene,
                // the childhood channel is Vietnamese speech: no language guessing on a music intro
                ["language"] = p.Topic == AiStudioTopics.VietnamChildhood ? "vi" : "auto"
            };
            var jobPath = Path.Combine(work, "job.json");
            await File.WriteAllTextAsync(jobPath, job.ToJsonString(), ct);

            var lastSave = DateTime.MinValue;
            var result = await _engine.AnalyzeAsync(jobPath, settings.MaxRamMb, async progress =>
            {
                var cancel = await _db.AiStudioProjects.AsNoTracking().Where(x => x.Id == id).Select(x => x.CancelRequested).FirstOrDefaultAsync(CancellationToken.None);
                if (progress != null && DateTime.UtcNow - lastSave > TimeSpan.FromSeconds(3))
                {
                    lastSave = DateTime.UtcNow;
                    p.Stage = progress.Stage;
                    p.ProgressText = progress.Message;
                    p.ProgressPercent = (int)Math.Clamp(progress.Percent ?? p.ProgressPercent, 1, 99);
                    if (progress.Stage == "vision") { p.ScenesDone = progress.Done; p.ScenesTotal = progress.Total; }
                    if (progress.Stage == "scenes") p.ScenesTotal = progress.Total;
                    await _db.SaveChangesAsync(CancellationToken.None);
                }
                return cancel;
            }, ct);

            var resultPath = result["result"]?.GetValue<string>() ?? throw new InvalidOperationException("Bộ phân tích không trả file kết quả.");
            await IngestAsync(p, resultPath, ct);
            finished = true;
        }
        catch (OperationCanceledException)
        {
            var hasScenes = await _db.AiStudioScenes.AnyAsync(s => s.ProjectId == id, CancellationToken.None);
            p.Status = hasScenes ? "Analyzed" : "Cancelled";
            p.ProgressText = hasScenes ? "Đã huỷ lần phân tích lại — giữ kết quả trước." : "Đã huỷ. Bấm Phân tích để chạy tiếp (các cảnh đã xong được giữ lại).";
            p.Stage = null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI Studio analysis {Id} failed", id);
            p.Status = "Failed";
            p.ErrorMessage = ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message;
            p.ProgressText = "Lỗi — xem chi tiết. Bấm Phân tích để chạy lại (cảnh đã xong được giữ lại).";
            p.Stage = null;
        }
        finally
        {
            p.CancelRequested = false;
            p.AnalysisFinishedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            if (finished) DeleteDir(work);
        }
    }

    /// <summary>Stores an analyzer result file for a project (used by the PC run; public for the tests).</summary>
    public async Task IngestResultAsync(Guid projectId, string resultPath, CancellationToken ct)
    {
        var p = await _db.AiStudioProjects.FirstOrDefaultAsync(x => x.Id == projectId, ct) ?? throw new KeyNotFoundException("Không tìm thấy dự án.");
        await IngestAsync(p, resultPath, ct);
    }

    /// <summary>Stores the analyzer's result: keyframes / thumbnails to the bucket, scenes, bibles, then the prompts.</summary>
    private async Task IngestAsync(AiStudioProject p, string resultPath, CancellationToken ct)
    {
        var result = JsonNode.Parse(await File.ReadAllTextAsync(resultPath, ct)) as JsonObject ?? throw new InvalidOperationException("Kết quả phân tích hỏng.");
        var scenesNode = result["scenes"] as JsonArray ?? new JsonArray();
        if (scenesNode.Count == 0) throw new InvalidOperationException("Không tìm thấy cảnh nào trong video.");

        await ClearAnalysisAsync(p, ct);
        var order = 0;
        var created = new List<AiStudioScene>();
        foreach (var node in scenesNode.OfType<JsonObject>())
        {
            var start = node["start"]?.GetValue<double>() ?? 0;
            var end = node["end"]?.GetValue<double>() ?? start;
            var frames = new List<AiStudioFrameDto>();
            foreach (var f in (node["frames"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                var path = f["path"]?.GetValue<string>();
                if (path == null || !File.Exists(path)) continue;
                var blob = await StoreFileAsync(path, "image/jpeg", ct);
                frames.Add(new AiStudioFrameDto { T = f["t"]?.GetValue<double>() ?? start, Reason = f["reason"]?.GetValue<string>() ?? "", BlobId = blob.Id });
            }
            Guid? thumb = null;
            if (node["thumb"]?.GetValue<string>() is { } tp && File.Exists(tp)) thumb = (await StoreFileAsync(tp, "image/jpeg", ct)).Id;
            var measured = node["measured"]?.DeepClone() as JsonObject ?? new JsonObject();
            if (node["colors"] is JsonObject colors)
                foreach (var (k, v) in colors) measured[k] = v?.DeepClone();
            var analysis = node["analysis"] as JsonObject ?? new JsonObject();
            var scene = new AiStudioScene
            {
                ProjectId = p.Id, Index = node["index"]?.GetValue<int>() ?? order + 1, Order = order++,
                StartSeconds = Math.Round(start, 3), EndSeconds = Math.Round(end, 3),
                IsContinuation = node["continuation"]?.GetValue<bool>() == true,
                TargetSeconds = SeedanceVideoClient.ClampSeconds((int)Math.Round(end - start)),
                ThumbBlobId = thumb ?? frames.FirstOrDefault()?.BlobId,
                FramesJson = JsonSerializer.Serialize(frames, Json),
                MeasuredJson = measured.ToJsonString(),
                AnalysisJson = analysis.ToJsonString(),
                Confidence = node["confidence"]?.GetValue<double>() ?? 0,
                SummaryVi = Truncate(AiStudioPromptEngine.Field(analysis, "summary_vi"), 1000),
                Transcript = Truncate(node["transcript"]?.GetValue<string>(), 2000)
            };
            _db.AiStudioScenes.Add(scene);
            created.Add(scene);
        }

        var bibles = result["bibles"] as JsonObject ?? new JsonObject();
        p.OverviewJson = (bibles["overview"] ?? new JsonObject()).ToJsonString();
        p.CharacterBibleJson = (bibles["characters"] ?? new JsonArray()).ToJsonString();
        p.EnvironmentBibleJson = (bibles["environments"] ?? new JsonArray()).ToJsonString();
        p.StyleBibleJson = (bibles["style"] ?? new JsonObject()).ToJsonString();
        p.PaletteJson = (result["measuredPalette"] ?? new JsonArray()).ToJsonString();
        p.TranscriptJson = result["transcript"] is JsonObject tr ? tr.ToJsonString() : null;
        // the narration of each scene (read again by the narrator) and a narrator that sounds like the reference speaker
        FillNarration(p, created);
        if (!AiStudioNarration.IsVoice(p.NarratorVoice) || string.IsNullOrWhiteSpace(p.NarratorStyle)) await SuggestNarratorAsync(p, ct);
        p.AnalysisMetaJson = (result["engine"] ?? new JsonObject()).ToJsonString();
        // the global prompt follows the new style bible unless the user wrote / restored one themselves
        var userGlobal = await _db.AiStudioPromptVersions.AnyAsync(v => v.ProjectId == p.Id && v.SceneId == null && v.Source != "engine", ct);
        if (string.IsNullOrWhiteSpace(p.GlobalPrompt) || !userGlobal)
        {
            var global = AiStudioPromptEngine.DefaultGlobalPrompt(ParseObj(p.StyleBibleJson));
            if (global != p.GlobalPrompt)
            {
                p.GlobalPrompt = global;
                await AddGlobalVersionAsync(p, "engine", ct);
            }
        }
        if (string.IsNullOrWhiteSpace(p.NegativePrompt)) p.NegativePrompt = AiStudioPromptEngine.DefaultNegative;
        p.Status = "Analyzed";
        p.Stage = "done";
        p.ProgressPercent = 100;
        p.ScenesTotal = p.ScenesDone = scenesNode.Count;
        p.ProgressText = $"Xong: {scenesNode.Count} cảnh.";
        p.ErrorMessage = null;
        await _db.SaveChangesAsync(ct);
        await FitNewAnalysisAsync(p, created, ct); // over a minute → condensed to one
        await TryReviewLogicAsync(p, created, ct); // every action made concrete, possible and safe before the prompts
        await RebuildPromptsAsync(p, overwriteEdited: true, source: "engine", ct);
    }

    // ═══════════════════════════════ prompts ═══════════════════════════════

    /// <summary><paramref name="previousEnabled"/>: the scene just before is in the video (else a "continues the previous shot" scene starts fresh).</summary>
    private ScenePromptInput PromptInput(AiStudioProject p, AiStudioScene s, bool hasPrevious, bool previousEnabled = true) => new(
        s.Index, s.StartSeconds, s.EndSeconds, s.TargetSeconds, s.IsContinuation && previousEnabled, hasPrevious,
        ParseObj(s.AnalysisJson), ParseObj(s.MeasuredJson), ParseArr(p.CharacterBibleJson), ParseArr(p.EnvironmentBibleJson), ParseObj(p.StyleBibleJson),
        p.PromptMode, ReadVariation(p.VariationJson), p.GlobalPrompt, p.NegativePrompt, p.Topic,
        AiStudioNarration.PromptText(s.Narration, s.NarrationEn, s.NarrationEdited), s.ReviewedAction, s.ReviewedRules, s.ReviewedSetting);

    /// <summary>The engine's prompt for one scene, with its neighbours as they are now.</summary>
    private async Task<string> EnginePromptAsync(AiStudioProject p, AiStudioScene s, CancellationToken ct)
    {
        var earlier = await _db.AiStudioScenes.AnyAsync(x => x.ProjectId == p.Id && x.Enabled && x.Order < s.Order, ct);
        var previousEnabled = await _db.AiStudioScenes.AnyAsync(x => x.ProjectId == p.Id && x.Enabled && x.Order == s.Order - 1, ct);
        return AiStudioPromptEngine.Build(PromptInput(p, s, earlier, previousEnabled));
    }

    /// <summary>Rebuilds the prompts of the scenes (not those the user wrote by hand, unless asked) and saves a version of each change.</summary>
    private async Task RebuildPromptsAsync(AiStudioProject p, bool overwriteEdited, string source, CancellationToken ct)
    {
        var scenes = await _db.AiStudioScenes.Where(s => s.ProjectId == p.Id).OrderBy(s => s.Order).ToListAsync(ct);
        var first = true;
        var previousEnabled = false;
        foreach (var s in scenes)
        {
            var hasPrevious = !first;
            var before = previousEnabled;
            if (s.Enabled) first = false;
            previousEnabled = s.Enabled;
            if (s.PromptEdited && !overwriteEdited) continue;
            await SetScenePromptAsync(p, s, AiStudioPromptEngine.Build(PromptInput(p, s, hasPrevious, before)), source, edited: false, ct);
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task SetScenePromptAsync(AiStudioProject p, AiStudioScene s, string text, string source, bool edited, CancellationToken ct)
    {
        text = text.Trim();
        s.PromptEdited = edited;
        if (text == s.Prompt) return;
        var last = await _db.AiStudioPromptVersions.Where(v => v.SceneId == s.Id).MaxAsync(v => (int?)v.Version, ct) ?? 0;
        s.Prompt = text;
        s.PromptVersion = last + 1;
        _db.AiStudioPromptVersions.Add(new AiStudioPromptVersion { ProjectId = p.Id, SceneId = s.Id, Version = last + 1, Text = text, Source = source, Mode = p.PromptMode });
    }

    private async Task<(AiStudioProject P, AiStudioScene S)> SceneAsync(Guid projectId, Guid sceneId, CancellationToken ct)
    {
        var p = await EditableProjectAsync(projectId, ct);
        var s = await _db.AiStudioScenes.FirstOrDefaultAsync(x => x.Id == sceneId && x.ProjectId == projectId, ct) ?? throw new KeyNotFoundException("Không tìm thấy cảnh.");
        return (p, s);
    }

    public async Task<AiStudioProjectDto> SetPromptSettingsAsync(Guid id, AiStudioPromptSettingsDto input, CancellationToken ct)
    {
        var p = await EditableProjectAsync(id, ct);
        var mode = AiStudioPromptEngine.NormalizeMode(input.Mode);
        var variation = Trim(input.Variation);
        if (mode == AiStudioPromptEngine.Creative && !AiStudioPromptEngine.HasVariation(variation))
            throw new ArgumentException("Chế độ Biến tấu cần ít nhất một thay đổi (nhân vật, trang phục, bối cảnh, góc quay, ánh sáng hoặc phong cách).");
        p.PromptMode = mode;
        if (input.Topic != null && AiStudioTopics.Normalize(input.Topic) != p.Topic)
        {
            var speaker = AiStudioNarration.SpeakerOf(p.TranscriptJson);
            var oldSuggestion = AiStudioNarration.Suggest(speaker, p.Topic);
            p.Topic = AiStudioTopics.Normalize(input.Topic);
            p.AudioMode = DefaultAudioMode(p);
            // a narrator the user did not change follows the new theme's mood
            if (p.NarratorStyle == oldSuggestion.Style || AiStudioNarration.IsOldSuggestion(p.NarratorStyle)) await SuggestNarratorAsync(p, ct);
        }
        p.VariationJson = JsonSerializer.Serialize(variation, Json);
        if (input.GlobalPrompt != null && input.GlobalPrompt.Trim() != p.GlobalPrompt)
        {
            p.GlobalPrompt = Truncate(input.GlobalPrompt.Trim(), 1500) ?? "";
            await AddGlobalVersionAsync(p, "user", ct);
        }
        if (input.NegativePrompt != null) p.NegativePrompt = Truncate(input.NegativePrompt.Trim(), 800) ?? "";
        await _db.SaveChangesAsync(ct);
        await RebuildPromptsAsync(p, input.OverwriteEdited, "engine", ct);
        return await GetProjectAsync(id, ct);
    }

    private async Task AddGlobalVersionAsync(AiStudioProject p, string source, CancellationToken ct)
    {
        var last = await _db.AiStudioPromptVersions.Where(v => v.ProjectId == p.Id && v.SceneId == null).MaxAsync(v => (int?)v.Version, ct) ?? 0;
        _db.AiStudioPromptVersions.Add(new AiStudioPromptVersion { ProjectId = p.Id, SceneId = null, Version = last + 1, Text = p.GlobalPrompt, Source = source, Mode = p.PromptMode });
    }

    public async Task<AiStudioProjectDto> SetBiblesAsync(Guid id, AiStudioBiblesDto input, CancellationToken ct)
    {
        var p = await EditableProjectAsync(id, ct);
        if (input.Characters != null)
        {
            if (input.Characters is not JsonArray chars) throw new ArgumentException("Character Bible phải là danh sách.");
            p.CharacterBibleJson = chars.ToJsonString();
        }
        if (input.Environments != null)
        {
            if (input.Environments is not JsonArray envs) throw new ArgumentException("Environment Bible phải là danh sách.");
            p.EnvironmentBibleJson = envs.ToJsonString();
        }
        if (input.Style != null)
        {
            if (input.Style is not JsonObject style) throw new ArgumentException("Visual Style Bible phải là một đối tượng.");
            p.StyleBibleJson = style.ToJsonString();
        }
        if (p.CharacterBibleJson.Length + p.EnvironmentBibleJson.Length + p.StyleBibleJson.Length > 60_000) throw new ArgumentException("Bible quá dài.");
        await _db.SaveChangesAsync(ct);
        await RebuildPromptsAsync(p, input.OverwriteEdited, "engine", ct);
        return await GetProjectAsync(id, ct);
    }

    public async Task<AiStudioSceneDto> EditScenePromptAsync(Guid projectId, Guid sceneId, string text, CancellationToken ct)
    {
        var (p, s) = await SceneAsync(projectId, sceneId, ct);
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Prompt trống.");
        if (text.Length > 4000) throw new ArgumentException("Prompt quá dài (tối đa 4000 ký tự).");
        await SetScenePromptAsync(p, s, text, "user", edited: true, ct);
        await _db.SaveChangesAsync(ct);
        return await SceneDtoAsync(p, s, ct);
    }

    public async Task<AiStudioSceneDto> RegenerateScenePromptAsync(Guid projectId, Guid sceneId, CancellationToken ct)
    {
        var (p, s) = await SceneAsync(projectId, sceneId, ct);
        await SetScenePromptAsync(p, s, await EnginePromptAsync(p, s, ct), "engine", edited: false, ct);
        await _db.SaveChangesAsync(ct);
        return await SceneDtoAsync(p, s, ct);
    }

    public async Task<List<AiStudioPromptVersionDto>> VersionsAsync(Guid projectId, Guid? sceneId, CancellationToken ct) =>
        await _db.AiStudioPromptVersions.AsNoTracking().Where(v => v.ProjectId == projectId && v.SceneId == sceneId)
            .OrderByDescending(v => v.Version).Take(50)
            .Select(v => new AiStudioPromptVersionDto { Id = v.Id, Version = v.Version, Text = v.Text, Source = v.Source, Mode = v.Mode, CreatedAtUtc = v.CreatedAtUtc })
            .ToListAsync(ct);

    public async Task<AiStudioProjectDto> RestoreVersionAsync(Guid projectId, Guid versionId, CancellationToken ct)
    {
        var p = await EditableProjectAsync(projectId, ct);
        var v = await _db.AiStudioPromptVersions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == versionId && x.ProjectId == projectId, ct)
                ?? throw new KeyNotFoundException("Không tìm thấy phiên bản.");
        if (v.SceneId is { } sceneId)
        {
            var s = await _db.AiStudioScenes.FirstOrDefaultAsync(x => x.Id == sceneId, ct) ?? throw new KeyNotFoundException("Cảnh không còn.");
            await SetScenePromptAsync(p, s, v.Text, "restore", edited: true, ct);
        }
        else
        {
            p.GlobalPrompt = v.Text;
            await AddGlobalVersionAsync(p, "restore", ct);
            await _db.SaveChangesAsync(ct);
            await RebuildPromptsAsync(p, false, "engine", ct);
        }
        await _db.SaveChangesAsync(ct);
        return await GetProjectAsync(projectId, ct);
    }

    public async Task<AiStudioSceneDto> UpdateSceneAsync(Guid projectId, Guid sceneId, AiStudioSceneUpdateDto input, CancellationToken ct)
    {
        var (p, s) = await SceneAsync(projectId, sceneId, ct);
        if (input.Enabled != null) s.Enabled = input.Enabled.Value;
        if (input.TargetSeconds != null)
        {
            s.TargetSeconds = SeedanceVideoClient.ClampSeconds(input.TargetSeconds.Value);
            if (!s.PromptEdited) // the timed sequence follows the new length
            {
                await SetScenePromptAsync(p, s, await EnginePromptAsync(p, s, ct), "engine", edited: false, ct);
            }
        }
        await _db.SaveChangesAsync(ct);
        return await SceneDtoAsync(p, s, ct);
    }

    public async Task<AiStudioProjectDto> ReorderAsync(Guid projectId, List<Guid> sceneIds, CancellationToken ct)
    {
        var p = await EditableProjectAsync(projectId, ct);
        var scenes = await _db.AiStudioScenes.Where(s => s.ProjectId == projectId).ToListAsync(ct);
        if (sceneIds.Count != scenes.Count || sceneIds.Distinct().Count() != scenes.Count || scenes.Any(s => !sceneIds.Contains(s.Id)))
            throw new ArgumentException("Danh sách cảnh không khớp dự án.");
        for (var i = 0; i < sceneIds.Count; i++) scenes.First(s => s.Id == sceneIds[i]).Order = i;
        await _db.SaveChangesAsync(ct);
        await RebuildPromptsAsync(p, false, "engine", ct); // "continues the previous scene" depends on the order
        return await GetProjectAsync(projectId, ct);
    }

    // ═══════════════════════════════ mapping ═══════════════════════════════

    private static void FillSummary(AiStudioProjectSummaryDto dto, AiStudioProject p)
    {
        dto.Id = p.Id;
        dto.Title = p.Title;
        dto.Status = p.Status;
        dto.DurationSeconds = p.DurationSeconds;
        dto.Width = p.Width;
        dto.Height = p.Height;
        dto.AspectRatio = p.AspectRatio;
        dto.ScenesTotal = p.ScenesTotal;
        dto.ProgressPercent = p.ProgressPercent;
        dto.ProgressText = p.ProgressText;
        dto.HasThumb = p.SourceThumbBlobId != null;
        dto.CreatedAtUtc = p.CreatedAtUtc;
        dto.UpdatedAtUtc = p.UpdatedAtUtc;
    }

    private AiStudioProjectDto ToDto(AiStudioProject p, List<AiStudioScene> scenes, List<AiStudioGeneration> gens)
    {
        var dto = new AiStudioProjectDto();
        FillSummary(dto, p);
        dto.ClipsDone = gens.Count(g => g.Status == "Done");
        dto.CostUsd = Math.Round(gens.Sum(g => g.CostUsd), 4);
        dto.SourceFileName = p.SourceFileName;
        dto.SourceContainer = p.SourceContainer;
        dto.SourceSizeBytes = p.SourceSizeBytes;
        dto.FpsText = p.FpsText;
        dto.Fps = p.Fps;
        dto.VideoCodec = p.VideoCodec;
        dto.BitrateKbps = p.BitrateKbps;
        dto.HasAudio = p.HasAudio;
        dto.AudioTracks = Deserialize<List<AiStudioAudioTrackDto>>(p.AudioJson) ?? new();
        dto.HasSource = p.SourceBlobId != null && !p.SourceDeleted;
        dto.SourceDeleted = p.SourceDeleted;
        dto.AnalysisModel = p.AnalysisModel;
        dto.Stage = p.Stage;
        dto.ScenesDone = p.ScenesDone;
        dto.CancelRequested = p.CancelRequested;
        dto.WorkerMachine = p.WorkerMachine;
        dto.AnalysisStartedAtUtc = p.AnalysisStartedAtUtc;
        dto.AnalysisFinishedAtUtc = p.AnalysisFinishedAtUtc;
        dto.ErrorMessage = p.ErrorMessage;
        dto.AnalysisMeta = ParseNode(p.AnalysisMetaJson);
        dto.Overview = ParseNode(p.OverviewJson);
        dto.Characters = ParseNode(p.CharacterBibleJson);
        dto.Environments = ParseNode(p.EnvironmentBibleJson);
        dto.Style = ParseNode(p.StyleBibleJson);
        dto.Palette = Deserialize<List<string>>(p.PaletteJson) ?? new();
        dto.Transcript = p.TranscriptJson != null ? ParseNode(p.TranscriptJson) : null;
        dto.PromptMode = p.PromptMode;
        dto.Topic = p.Topic;
        dto.AudioMode = p.AudioMode;
        dto.HasSourceAudio = p.SourceAudioBlobId != null;
        (dto.NarratorVoice, dto.NarratorStyle) = NarratorOf(p);
        dto.SpeakerProfileVi = AiStudioNarration.Describe(AiStudioNarration.SpeakerOf(p.TranscriptJson));
        dto.Variation = ReadVariation(p.VariationJson);
        dto.GlobalPrompt = p.GlobalPrompt;
        dto.NegativePrompt = p.NegativePrompt;
        dto.VideoModel = p.VideoModel;
        dto.Resolution = p.Resolution;
        dto.GenAspectRatio = p.GenAspectRatio;
        dto.Seed = p.Seed;
        dto.Continuity = p.Continuity;
        dto.HasReferenceImage = p.ReferenceImageBlobId != null;
        dto.HasFinal = p.FinalBlobId != null;
        dto.FinalStatus = p.FinalStatus;
        dto.FinalError = p.FinalError;
        dto.FinalUpdatedAtUtc = p.FinalUpdatedAtUtc;
        var latest = gens.GroupBy(g => g.SceneId).ToDictionary(g => g.Key, g => g.First());
        var orders = scenes.ToDictionary(s => s.Id, s => s.Order);
        dto.FacebookAccountId = p.FacebookAccountId;
        dto.Watermark = p.Watermark;
        dto.Caption = p.Caption;
        var enabledScenes = scenes.Where(s => s.Enabled).ToList();
        dto.VideoSeconds = enabledScenes.Sum(s => s.TargetSeconds);
        dto.MaxVideoSeconds = AiStudioDuration.MaxVideoSeconds;
        dto.NarrationEstimateSeconds = Math.Round(AiStudioDuration.SpeechSeconds(FullNarration(scenes), AiStudioNarration.SpeakerOf(p.TranscriptJson)?.SyllablesPerSecond), 1);
        dto.HasNarrationTake = p.NarrationBlobId != null;
        dto.NarrationSeconds = p.NarrationSeconds;
        dto.NarrationCurrent = p.NarrationBlobId != null && p.NarrationKey == NarrationKeyOf(p, scenes);
        dto.Scenes = scenes.Select(s => SceneDto(p, s, latest.TryGetValue(s.Id, out var g) ? GenDto(g, p.Title, orders) : null)).ToList();
        return dto;
    }

    private async Task<AiStudioSceneDto> SceneDtoAsync(AiStudioProject p, AiStudioScene s, CancellationToken ct)
    {
        var g = await _db.AiStudioGenerations.AsNoTracking().Where(x => x.SceneId == s.Id).OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        return SceneDto(p, s, g == null ? null : GenDto(g, p.Title, new Dictionary<Guid, int> { [s.Id] = s.Order }));
    }

    private AiStudioSceneDto SceneDto(AiStudioProject p, AiStudioScene s, AiStudioGenerationDto? latest) => new()
    {
        Id = s.Id, Index = s.Index, Order = s.Order, StartSeconds = s.StartSeconds, EndSeconds = s.EndSeconds, IsContinuation = s.IsContinuation,
        Enabled = s.Enabled, TargetSeconds = s.TargetSeconds, HasThumb = s.ThumbBlobId != null, Frames = ReadFrames(s.FramesJson),
        Measured = ParseNode(s.MeasuredJson), Analysis = ParseNode(s.AnalysisJson), Confidence = s.Confidence, SummaryVi = s.SummaryVi,
        Transcript = s.Transcript, Narration = s.Narration, NarrationEn = s.NarrationEn, NarrationEdited = s.NarrationEdited,
        ReviewedAction = s.ReviewedAction, ReviewedRules = s.ReviewedRules, ReviewedSetting = s.ReviewedSetting,
        Prompt = s.Prompt, PromptVersion = s.PromptVersion, PromptEdited = s.PromptEdited,
        EstimatedCostUsd = _video.EstimateCost(p.VideoModel, p.Resolution, s.TargetSeconds), Latest = latest
    };

    private static AiStudioGenerationDto GenDto(AiStudioGeneration g, string? title, IReadOnlyDictionary<Guid, int> orders) => new()
    {
        Id = g.Id, ProjectId = g.ProjectId, ProjectTitle = title, SceneId = g.SceneId, SceneOrder = orders.TryGetValue(g.SceneId, out var o) ? o : null,
        BatchId = g.BatchId, Status = g.Status, Model = g.Model, Resolution = g.Resolution, AspectRatio = g.AspectRatio, Seconds = g.Seconds, Seed = g.Seed,
        Prompt = g.Prompt, ImageSource = g.ImageSource, EstimatedCostUsd = g.EstimatedCostUsd, CostUsd = g.CostUsd, HasClip = g.ClipBlobId != null && !g.FilesDeleted,
        Paid = g.ProviderOperation != null, ErrorMessage = g.ErrorMessage, Attempts = g.Attempts, CreatedAtUtc = g.CreatedAtUtc, StartedAtUtc = g.StartedAtUtc,
        FinishedAtUtc = g.FinishedAtUtc, FilesDeleted = g.FilesDeleted
    };

    // ═══════════════════════════════ JSON helpers ═══════════════════════════════

    internal static List<AiStudioFrameDto> ReadFrames(string? json) => Deserialize<List<AiStudioFrameDto>>(json) ?? new();

    internal static AiStudioVariationDto ReadVariation(string? json) => Deserialize<AiStudioVariationDto>(json) ?? new();

    private static AiStudioVariationDto Trim(AiStudioVariationDto? v) => new()
    {
        Character = Truncate(v?.Character?.Trim(), 500), Wardrobe = Truncate(v?.Wardrobe?.Trim(), 500), Setting = Truncate(v?.Setting?.Trim(), 500),
        Camera = Truncate(v?.Camera?.Trim(), 300), Lighting = Truncate(v?.Lighting?.Trim(), 300), Style = Truncate(v?.Style?.Trim(), 300)
    };

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); } catch (JsonException) { return null; }
    }

    private static JsonNode? ParseNode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json); } catch (JsonException) { return null; }
    }

    internal static JsonObject ParseObj(string? json) => ParseNode(json) as JsonObject ?? new JsonObject();
    internal static JsonArray ParseArr(string? json) => ParseNode(json) as JsonArray ?? new JsonArray();

    private static string? Truncate(string? s, int max) => string.IsNullOrEmpty(s) ? null : s.Length > max ? s[..max] : s;
}

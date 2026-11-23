using System.Globalization;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.FilmFactory;

namespace NewsStudio.Infrastructure.Studio;

/// <summary>
/// AI video studio (text-to-video): script → Gemini plan (a character / location bible with fixed looks and TTS voices,
/// then shots) → each spoken line read by Gemini TTS → one Seedance clip per shot through the Vercel AI Gateway (silent;
/// its length fitted to the line) → joined with the voices, ducked music and the Page watermark at 1080p → posted to a
/// Facebook Page. Every paid call is recorded in StudioCostRecords and checked against the monthly studio budget first.
/// Small files (voices, music) live in the database; clips and final videos on the app disk (see StudioFileStore).
/// </summary>
public class StudioService
{
    public const string BudgetKey = "Studio.MonthlyBudgetUsd";
    public const string DeliveryResolution = "1080p";
    /// <summary>A spoken line needs this much video after it (seconds) so the cut doesn't clip the last word.</summary>
    private const double VoiceTail = 0.6;
    private const int MaxAttemptsPerRun = 2;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly SeedanceVideoClient _video;
    private readonly FilmGeminiClient _gemini;
    private readonly StudioMedia _media;
    private readonly StudioFileStore _files;
    private readonly StudioVoices _voices;
    private readonly IFacebookPublishingService _facebook;
    private readonly IBackgroundJobClient _jobs;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StudioService> _logger;

    public StudioService(IApplicationDbContext db, SeedanceVideoClient video, FilmGeminiClient gemini, StudioMedia media, StudioFileStore files,
        StudioVoices voices, IFacebookPublishingService facebook, IBackgroundJobClient jobs, IConfiguration configuration, ILogger<StudioService> logger)
    {
        _db = db;
        _video = video;
        _gemini = gemini;
        _media = media;
        _files = files;
        _voices = voices;
        _facebook = facebook;
        _jobs = jobs;
        _configuration = configuration;
        _logger = logger;
    }

    // ═══════════════════════════════ Bible ═══════════════════════════════

    /// <summary>The video's characters (look + voice) and locations, shared by every shot.</summary>
    public sealed class Bible
    {
        public List<StudioCharacterDto> Characters { get; set; } = new();
        public List<StudioLocationDto> Locations { get; set; } = new();

        public StudioCharacterDto? Character(string? name) =>
            string.IsNullOrWhiteSpace(name) ? null : Characters.FirstOrDefault(c => SameName(c.Name, name));

        public StudioLocationDto? Location(string? name) =>
            string.IsNullOrWhiteSpace(name) ? null : Locations.FirstOrDefault(l => SameName(l.Name, name));
    }

    public static bool SameName(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    public static Bible ReadBible(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Bible();
        try { return JsonSerializer.Deserialize<Bible>(json, Json) ?? new Bible(); }
        catch (JsonException) { return new Bible(); }
    }

    private static string WriteBible(Bible b) => JsonSerializer.Serialize(b, Json);

    /// <summary>Cleans a bible from the user / planner: named entries only, known voices, one voice per character where possible.</summary>
    public static Bible NormalizeBible(IEnumerable<StudioCharacterDto>? characters, IEnumerable<StudioLocationDto>? locations)
    {
        var b = new Bible();
        foreach (var c in characters ?? Enumerable.Empty<StudioCharacterDto>())
        {
            if (string.IsNullOrWhiteSpace(c.Name) || b.Character(c.Name) != null) continue;
            var gender = c.Gender?.Trim().ToLowerInvariant() is "female" or "f" or "nữ" ? "female" : "male";
            var voice = StudioVoices.IsKnown(c.Voice) ? c.Voice : StudioVoices.Pick(gender, b.Characters.Select(x => x.Voice));
            b.Characters.Add(new StudioCharacterDto { Name = c.Name.Trim(), Gender = gender, Look = (c.Look ?? "").Trim(), Voice = voice });
        }
        foreach (var l in locations ?? Enumerable.Empty<StudioLocationDto>())
        {
            if (string.IsNullOrWhiteSpace(l.Name) || b.Location(l.Name) != null) continue;
            b.Locations.Add(new StudioLocationDto { Name = l.Name.Trim(), Look = (l.Look ?? "").Trim() });
        }
        return b;
    }

    /// <summary>Whether a speaker is the off-screen narrator (nobody talks on screen).</summary>
    public static bool IsNarrator(string? speaker) =>
        string.IsNullOrWhiteSpace(speaker) || speaker.Trim().ToLowerInvariant() is "lời dẫn" or "người dẫn chuyện" or "dẫn chuyện" or "narrator" or "voiceover";

    /// <summary>The TTS voice of a line's speaker (narrator / unknown speaker → the narrator's or default voice).</summary>
    public static string VoiceFor(Bible bible, string? speaker) =>
        bible.Character(speaker)?.Voice is { Length: > 0 } v ? v
        : IsNarrator(speaker) && bible.Character(StudioVoices.NarratorName)?.Voice is { Length: > 0 } n ? n
        : StudioVoices.DefaultNarratorVoice;

    // ═══════════════════════════════ Music ═══════════════════════════════

    public async Task<List<StudioMusicDto>> ListMusicAsync(CancellationToken ct) =>
        (await _db.StudioMusicTracks.AsNoTracking().OrderBy(m => m.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<StudioMusicDto> AddMusicAsync(string name, string? mood, byte[] data, string mimeType, string extension, CancellationToken ct)
    {
        if (data.Length == 0) throw new ArgumentException("File nhạc trống.");
        var seconds = await _media.ProbeSecondsAsync(data, extension, ct);
        if (seconds <= 0) throw new ArgumentException("Không đọc được file nhạc (hãy dùng MP3, M4A, WAV hoặc OGG).");
        // the music store lives in the (small) database: keep each track compact — AAC 128 kb/s, at most 5 minutes
        data = await _media.CompactMusicAsync(data, extension, ct);
        seconds = Math.Min(seconds, StudioMedia.MaxMusicSeconds);
        var blob = new StudioBlob { MimeType = "audio/mp4", Data = data, SizeBytes = data.Length };
        var track = new StudioMusicTrack
        {
            Name = Required(name, "Tên bài"), Mood = (mood ?? "").Trim(), BlobId = blob.Id, MimeType = blob.MimeType,
            SizeBytes = data.Length, DurationSeconds = Math.Round(seconds, 1)
        };
        _db.StudioBlobs.Add(blob);
        _db.StudioMusicTracks.Add(track);
        await _db.SaveChangesAsync(ct);
        return ToDto(track);
    }

    public async Task<StudioMusicDto> UpdateMusicAsync(Guid id, string? name, string? mood, CancellationToken ct)
    {
        var m = await _db.StudioMusicTracks.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy bài nhạc.");
        if (!string.IsNullOrWhiteSpace(name)) m.Name = name.Trim();
        if (mood != null) m.Mood = mood.Trim();
        await _db.SaveChangesAsync(ct);
        return ToDto(m);
    }

    public async Task DeleteMusicAsync(Guid id, CancellationToken ct)
    {
        var m = await _db.StudioMusicTracks.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy bài nhạc.");
        await DeleteBlobAsync(m.BlobId, ct);
        _db.StudioMusicTracks.Remove(m);
        await _db.SaveChangesAsync(ct);
    }

    // ═══════════════════════════════ Files ═══════════════════════════════

    /// <summary>The file with its bytes, wherever it lives (database row or app disk); null when gone.</summary>
    public async Task<StudioBlob?> GetBlobAsync(Guid blobId, CancellationToken ct)
    {
        var blob = await _db.StudioBlobs.AsNoTracking().FirstOrDefaultAsync(b => b.Id == blobId, ct);
        if (blob == null || blob.Data.Length > 0) return blob;
        var bytes = await _files.ReadAsync(blobId, ct);
        if (bytes == null) return null;
        blob.Data = bytes;
        return blob;
    }

    /// <summary>Path of a file kept on disk (clips, final videos), or null when it lives in the database / is gone.</summary>
    public async Task<(string Path, string MimeType)?> DiskFileAsync(Guid blobId, CancellationToken ct)
    {
        var meta = await _db.StudioBlobs.AsNoTracking().Where(b => b.Id == blobId)
            .Select(b => new { b.MimeType, InDb = b.Data.Length > 0 }).FirstOrDefaultAsync(ct);
        if (meta == null || meta.InDb || !_files.Exists(blobId)) return null;
        return (_files.PathFor(blobId), meta.MimeType);
    }

    /// <summary>Clips and final videos go to the app disk, never into the database (see <see cref="StudioFileStore"/>).</summary>
    private async Task<StudioBlob> StoreOnDiskAsync(byte[] data, string mimeType, CancellationToken ct)
    {
        var blob = new StudioBlob { MimeType = mimeType, Data = Array.Empty<byte>(), SizeBytes = data.Length };
        await _files.SaveAsync(blob.Id, data, ct);
        _db.StudioBlobs.Add(blob);
        return blob;
    }

    /// <summary>
    /// The app disk is wiped on redeploy: a clip / final video whose file is gone is marked missing (shot back to
    /// "to do", final cleared) so the page shows what to redo instead of failing later.
    /// </summary>
    private async Task ReconcileLostFilesAsync(Guid videoId, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == videoId, ct);
        if (v == null || v.Status is "Generating" or "Composing" or "Publishing") return;
        var ids = v.Shots.Where(s => s.ClipBlobId != null).Select(s => s.ClipBlobId!.Value).ToList();
        if (v.FinalBlobId != null) ids.Add(v.FinalBlobId.Value);
        if (ids.Count == 0) return;
        var onDisk = await _db.StudioBlobs.AsNoTracking().Where(b => ids.Contains(b.Id) && b.Data.Length == 0).Select(b => b.Id).ToListAsync(ct);
        var known = await _db.StudioBlobs.AsNoTracking().Where(b => ids.Contains(b.Id)).Select(b => b.Id).ToListAsync(ct);
        bool Lost(Guid id) => !known.Contains(id) || (onDisk.Contains(id) && !_files.Exists(id));
        var changed = false;
        foreach (var s in v.Shots.Where(s => s.ClipBlobId is { } c && Lost(c)))
        {
            await DeleteBlobAsync(s.ClipBlobId!.Value, ct);
            s.ClipBlobId = null;
            s.Status = "Pending";
            s.ErrorMessage = "Clip đã mất do server khởi động lại — bấm \"Lấy lại clip đã trả tiền\" (miễn phí) hoặc tạo lại shot.";
            changed = true;
        }
        if (v.FinalBlobId is { } f && Lost(f))
        {
            await DeleteBlobAsync(f, ct);
            v.FinalBlobId = null;
            if (v.Status == "Ready") v.Status = "Planned";
            v.ErrorMessage = "Video cuối đã mất do server khởi động lại — bấm Ghép lại (hoặc lấy lại clip trước).";
            changed = true;
        }
        if (changed) await _db.SaveChangesAsync(ct);
    }

    public async Task<Guid?> MusicBlobIdAsync(Guid musicId, CancellationToken ct) =>
        await _db.StudioMusicTracks.Where(a => a.Id == musicId).Select(a => (Guid?)a.BlobId).FirstOrDefaultAsync(ct);

    public async Task<Guid?> ShotClipBlobIdAsync(Guid shotId, CancellationToken ct) =>
        await _db.StudioShots.Where(s => s.Id == shotId).Select(s => s.ClipBlobId).FirstOrDefaultAsync(ct);

    public async Task<Guid?> ShotVoiceBlobIdAsync(Guid shotId, CancellationToken ct) =>
        await _db.StudioShots.Where(s => s.Id == shotId).Select(s => s.VoiceBlobId).FirstOrDefaultAsync(ct);

    public async Task<Guid?> FinalBlobIdAsync(Guid videoId, CancellationToken ct) =>
        await _db.StudioVideos.Where(v => v.Id == videoId).Select(v => v.FinalBlobId).FirstOrDefaultAsync(ct);

    // ═══════════════════════════════ Videos ═══════════════════════════════

    /// <summary>
    /// Frees videos left in a background step by a run that was cut off (server restart, database outage): planning
    /// after 10 min, composing / publishing after 30 min, generating after 45 min without any progress (every clip
    /// saved refreshes the timestamp, and one clip never waits longer than 15 min).
    /// </summary>
    private async Task ReleaseStalePlanningAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var plan = now - StalePlanning;
        var short_ = now - TimeSpan.FromMinutes(30);
        var gen = now - TimeSpan.FromMinutes(45);
        var stuck = await _db.StudioVideos.Include(v => v.Shots)
            .Where(v => (v.Status == "Planning" && (v.UpdatedAtUtc ?? v.CreatedAtUtc) < plan)
                        || ((v.Status == "Composing" || v.Status == "Publishing") && (v.UpdatedAtUtc ?? v.CreatedAtUtc) < short_)
                        || (v.Status == "Generating" && (v.UpdatedAtUtc ?? v.CreatedAtUtc) < gen))
            .ToListAsync(ct);
        if (stuck.Count == 0) return;
        foreach (var v in stuck)
        {
            var step = v.Status;
            foreach (var s in v.Shots.Where(s => s.Status == "Generating")) s.Status = "Pending"; // its paid job is kept → resumed
            v.Status = step switch
            {
                "Publishing" => "Ready",
                _ when v.FinalBlobId != null => "Ready",
                _ => v.Shots.Count > 0 ? (v.Shots.Any(s => s.ClipBlobId == null) ? "Failed" : "Planned") : "Draft"
            };
            v.Progress = null;
            v.ErrorMessage = step switch
            {
                "Planning" => "Lập kế hoạch lần trước bị ngắt giữa chừng — bấm lập kế hoạch lại.",
                "Composing" => "Ghép video bị ngắt giữa chừng — bấm \"Ghép lại\".",
                "Publishing" => "Đăng Facebook bị ngắt giữa chừng — kiểm tra Page rồi bấm đăng lại nếu chưa thấy bài.",
                _ => "Tạo clip bị ngắt giữa chừng — bấm \"Tạo các clip còn thiếu\" (clip đã trả tiền dở sẽ được lấy tiếp, không trả lại)."
            };
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<StudioVideoSummaryDto>> ListVideosAsync(CancellationToken ct)
    {
        await ReleaseStalePlanningAsync(ct);
        var pages = await PageNamesAsync(ct);
        var videos = await _db.StudioVideos.AsNoTracking().Include(v => v.Shots).OrderByDescending(v => v.CreatedAtUtc).Take(200).ToListAsync(ct);
        return videos.Select(v => FillSummary(new StudioVideoSummaryDto(), v, pages)).ToList();
    }

    public async Task<StudioVideoDto> GetVideoAsync(Guid id, CancellationToken ct)
    {
        await ReleaseStalePlanningAsync(ct);
        await ReconcileLostFilesAsync(id, ct);
        var v = await _db.StudioVideos.AsNoTracking().Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException("Không tìm thấy video.");
        var pages = await PageNamesAsync(ct);
        var bible = ReadBible(v.BibleJson);
        var dto = FillSummary(new StudioVideoDto(), v, pages);
        dto.Script = v.Script;
        dto.VideoModel = SeedanceVideoClient.NormalizeModel(v.VideoModel);
        dto.AspectRatio = v.AspectRatio;
        dto.Resolution = SeedanceVideoClient.NormalizeResolution(v.Resolution);
        dto.StyleNotes = v.StyleNotes;
        dto.Characters = bible.Characters;
        dto.Locations = bible.Locations;
        dto.MusicMode = v.MusicMode;
        dto.MusicTrackId = v.MusicTrackId;
        dto.MusicName = v.MusicTrackId == null ? null
            : await _db.StudioMusicTracks.Where(m => m.Id == v.MusicTrackId).Select(m => m.Name).FirstOrDefaultAsync(ct);
        dto.MusicVolume = v.MusicVolume;
        dto.PlanNotes = v.PlanNotes;
        dto.Caption = v.Caption;
        dto.FinalDurationSeconds = v.FinalDurationSeconds;
        dto.ErrorMessage = v.ErrorMessage;
        dto.PublishedPostId = v.PublishedPostId;
        dto.Shots = v.Shots.OrderBy(s => s.Index).Select(s => new StudioShotDto
        {
            Id = s.Id, Index = s.Index, DurationSeconds = s.DurationSeconds, Action = s.Action, Camera = s.Camera,
            Location = s.Location, Cast = Names(s.CastJson), Speaker = s.Speaker, Dialogue = s.Dialogue, Sound = s.Sound,
            HasVoice = s.VoiceBlobId != null, VoiceSeconds = s.VoiceSeconds, Status = s.Status,
            HasClip = s.ClipBlobId != null, CostUsd = s.CostUsd, Attempts = s.Attempts, ErrorMessage = s.ErrorMessage,
            EstimatedCostUsd = ShotCost(v, s)
        }).ToList();
        return dto;
    }

    public async Task<StudioVideoDto> CreateVideoAsync(StudioVideoInputDto input, CancellationToken ct)
    {
        var v = new StudioVideo
        {
            Resolution = SeedanceVideoClient.NormalizeResolution(_configuration["Studio:DefaultResolution"]),
            AspectRatio = _configuration["Studio:DefaultAspectRatio"] ?? "9:16",
            VideoModel = _video.DefaultModel,
            Seed = NewSeed()
        };
        Apply(v, input);
        if (string.IsNullOrWhiteSpace(v.Title)) v.Title = "Video " + DateTime.UtcNow.AddHours(7).ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
        _db.StudioVideos.Add(v);
        await _db.SaveChangesAsync(ct);
        return await GetVideoAsync(v.Id, ct);
    }

    private static int NewSeed() => Random.Shared.Next(1, int.MaxValue);

    public async Task<StudioVideoDto> UpdateVideoAsync(Guid id, StudioVideoInputDto input, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        EnsureNotBusy(v);
        var pageBefore = v.FacebookAccountId;
        var bibleBefore = ReadBible(v.BibleJson);
        Apply(v, input);
        // a character given another voice: their lines must be read again
        var bibleAfter = ReadBible(v.BibleJson);
        foreach (var s in v.Shots.Where(s => s.VoiceBlobId != null && VoiceFor(bibleBefore, s.Speaker) != VoiceFor(bibleAfter, s.Speaker)))
        {
            await DeleteBlobAsync(s.VoiceBlobId!.Value, ct);
            s.VoiceBlobId = null;
            s.VoiceSeconds = null;
        }
        if (v.Shots.Count > 0) v.TargetDurationSeconds = v.Shots.Sum(s => SeedanceVideoClient.ClampSeconds(s.DurationSeconds)); // real length wins
        v.EstimatedCostUsd = EstimateVideoCost(v);
        // the final video carries the Page's name: a different Page means composing it again with the new watermark
        var rebrand = v.FacebookAccountId != pageBefore && v.FinalBlobId != null && v.Status != "Published";
        if (rebrand)
        {
            if (v.Shots.Count == 0 || v.Shots.Any(s => s.ClipBlobId == null))
                throw new InvalidOperationException("Video đã ghép với tên Page cũ nhưng các clip đã bị xoá nên không ghép lại được tên Page mới. Hãy giữ Page cũ hoặc tạo video mới.");
            v.Status = "Composing";
            v.Progress = "Đổi Page — đang ghép lại video với tên Page mới…";
        }
        await _db.SaveChangesAsync(ct);
        if (rebrand) _jobs.Enqueue<StudioService>(s => s.RunComposeAsync(id, CancellationToken.None));
        return await GetVideoAsync(id, ct);
    }

    public async Task DeleteVideoAsync(Guid id, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        EnsureNotBusy(v);
        foreach (var s in v.Shots)
        {
            if (s.ClipBlobId != null) await DeleteBlobAsync(s.ClipBlobId.Value, ct);
            if (s.VoiceBlobId != null) await DeleteBlobAsync(s.VoiceBlobId.Value, ct);
            if (s.KeyframeBlobId != null) await DeleteBlobAsync(s.KeyframeBlobId.Value, ct);
        }
        if (v.FinalBlobId != null) await DeleteBlobAsync(v.FinalBlobId.Value, ct);
        _db.StudioVideos.Remove(v);
        await _db.SaveChangesAsync(ct);
    }

    private static void Apply(StudioVideo v, StudioVideoInputDto i)
    {
        if (i.Title != null) v.Title = i.Title.Trim();
        if (i.FacebookAccountId != null) v.FacebookAccountId = i.FacebookAccountId == Guid.Empty ? null : i.FacebookAccountId;
        if (i.Script != null) v.Script = i.Script.Trim();
        // only a rough estimate from the script until shots exist; then it is the shots' total
        if (i.TargetDurationSeconds != null) v.TargetDurationSeconds = Math.Clamp(i.TargetDurationSeconds.Value, 1, 3600);
        if (i.VideoModel != null) v.VideoModel = SeedanceVideoClient.NormalizeModel(i.VideoModel);
        if (i.AspectRatio != null) v.AspectRatio = i.AspectRatio == "16:9" ? "16:9" : "9:16";
        if (i.Resolution != null) v.Resolution = SeedanceVideoClient.NormalizeResolution(i.Resolution);
        if (i.StyleNotes != null) v.StyleNotes = i.StyleNotes.Trim();
        if (i.Characters != null || i.Locations != null)
        {
            var current = ReadBible(v.BibleJson);
            v.BibleJson = WriteBible(NormalizeBible(i.Characters ?? current.Characters, i.Locations ?? current.Locations));
        }
        if (i.MusicMode != null) v.MusicMode = i.MusicMode is "Manual" or "None" ? i.MusicMode : "Auto";
        if (i.MusicTrackId != null) v.MusicTrackId = i.MusicTrackId == Guid.Empty ? null : i.MusicTrackId;
        if (i.MusicVolume != null) v.MusicVolume = Math.Clamp(i.MusicVolume.Value, 0, 1);
        if (i.Caption != null) v.Caption = i.Caption.Trim();
    }

    // ═══════════════════════════════ Planning ═══════════════════════════════

    /// <summary>
    /// Queues the planning (Gemini writes the character / location bible and cuts the script into clip-sized shots,
    /// replacing the current list). It runs in the background: a long script can take Gemini minutes.
    /// </summary>
    public async Task<StudioVideoDto> StartPlanAsync(Guid id, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        EnsureNotBusy(v);
        if (string.IsNullOrWhiteSpace(v.Script)) throw new InvalidOperationException("Hãy nhập kịch bản trước khi lập kế hoạch.");
        if (v.Shots.Any(s => s.ClipBlobId != null))
            throw new InvalidOperationException("Video đã có clip được tạo — lập kế hoạch lại sẽ bỏ các clip đó. Hãy tạo video mới nếu muốn làm lại từ đầu.");
        v.Status = "Planning";
        v.Progress = "Gemini đang đọc kịch bản, dựng hồ sơ nhân vật và chia shot (kịch bản dài có thể mất 1–3 phút)…";
        v.ErrorMessage = null;
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<StudioService>(s => s.RunPlanAsync(id, CancellationToken.None));
        return await GetVideoAsync(id, ct);
    }

    [Queue("critical")]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunPlanAsync(Guid id, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (v == null || v.Status != "Planning") return;
        try
        {
            var music = await _db.StudioMusicTracks.AsNoTracking().OrderBy(m => m.UseCount).ToListAsync(ct);
            var (system, user) = BuildPlanPrompt(v, music);
            var (result, planModel) = await PlanWithFallbackAsync(v, system, user, ct);
            var plan = ParsePlan(result.Text, music.Select(m => m.Id).ToHashSet());

            foreach (var old in v.Shots)
            {
                if (old.VoiceBlobId != null) await DeleteBlobAsync(old.VoiceBlobId.Value, ct);
                if (old.KeyframeBlobId != null) await DeleteBlobAsync(old.KeyframeBlobId.Value, ct);
            }
            _db.StudioShots.RemoveRange(v.Shots);
            v.Shots.Clear();
            var index = 1;
            foreach (var s in plan.Shots)
            {
                // Added through the DbSet: a new entity already carries its Guid, so attaching it via the navigation
                // would make EF treat it as an existing row (UPDATE → "expected to affect 1 row").
                _db.StudioShots.Add(new StudioShot
                {
                    StudioVideoId = v.Id, Index = index++, DurationSeconds = s.Duration,
                    Action = s.Action, Camera = s.Camera, Location = s.Location, CastJson = JsonSerializer.Serialize(s.Cast),
                    Speaker = NullIfEmpty(s.Speaker), Dialogue = NullIfEmpty(s.Dialogue), Sound = NullIfEmpty(s.Sound)
                });
            }
            v.BibleJson = WriteBible(plan.Bible);
            if (v.Seed == 0) v.Seed = NewSeed();
            if (string.IsNullOrWhiteSpace(v.Caption)) v.Caption = plan.Caption;
            v.PlanNotes = plan.Notes;
            v.TargetDurationSeconds = Math.Max(1, plan.Shots.Sum(s => s.Duration));
            if (v.MusicMode == "Auto") v.MusicTrackId = plan.MusicTrackId ?? music.FirstOrDefault()?.Id;
            v.EstimatedCostUsd = EstimateVideoCost(v);
            v.Status = "Planned";
            v.Progress = null;
            _db.StudioCostRecords.Add(new StudioCostRecord
            {
                StudioVideoId = v.Id, Kind = "Plan", Model = planModel, Tokens = result.Tokens, CostUsd = result.CostUsd,
                Note = result.FreeKey ? "khoá miễn phí" : null
            });
            v.ActualCostUsd += result.CostUsd;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // whatever happens (Gemini error, timeout, server shutting down) the video must not stay stuck in "Planning"
            _logger.LogWarning(ex, "Studio planning failed for {Id}", id);
            v.Status = v.Shots.Count > 0 ? "Planned" : "Draft";
            v.Progress = null;
            v.ErrorMessage = ex is OperationCanceledException
                ? "Lập kế hoạch bị ngắt giữa chừng (Gemini trả lời quá lâu hoặc server khởi động lại) — bấm lập kế hoạch lại."
                : "Lập kế hoạch thất bại: " + ex.Message;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
    }

    /// <summary>Per-model wait: a healthy model answers a long script in well under a minute with light thinking.</summary>
    private static readonly TimeSpan PlanAttemptTimeout = TimeSpan.FromSeconds(150);

    /// <summary>Models tried in turn for the plan (Studio:PlanModels, comma-separated; default: app model, then older Flashes).</summary>
    public List<string> PlanModels()
    {
        var configured = (_configuration["Studio:PlanModels"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var models = configured.Length > 0 ? configured.ToList() : new List<string> { _gemini.TextModel, "gemini-3.5-flash", "gemini-2.5-flash" };
        return models.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Asks each plan model in turn, with light thinking and a time limit per model: an overloaded model (503 / 429,
    /// or one that hangs) no longer sinks the whole plan — the next one takes over.
    /// </summary>
    private async Task<(FilmGeminiClient.TextResult Result, string Model)> PlanWithFallbackAsync(StudioVideo v, string system, string user, CancellationToken ct)
    {
        var errors = new List<string>();
        var models = PlanModels();
        for (var i = 0; i < models.Count; i++)
        {
            var model = models[i];
            v.Progress = i == 0
                ? $"Gemini ({model}) đang đọc kịch bản, dựng hồ sơ nhân vật và chia shot…"
                : $"{models[i - 1]} không trả lời được — đang thử {model} ({i + 1}/{models.Count})…";
            await _db.SaveChangesAsync(CancellationToken.None);
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(PlanAttemptTimeout);
            try
            {
                var result = await _gemini.GenerateJsonAsync(system, user, 32768, 0.6, attempt.Token, model, lightThinking: true);
                ParsePlan(result.Text, new HashSet<Guid>()); // must at least be a usable plan
                return (result, model);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                errors.Add($"{model}: quá {PlanAttemptTimeout.TotalSeconds:0}s không trả lời");
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException)
            {
                errors.Add($"{model}: {ex.Message}");
            }
            _logger.LogWarning("Studio plan: {Error}", errors[^1]);
        }
        throw new InvalidOperationException("Không model Gemini nào lập được kế hoạch — " + string.Join(" | ", errors));
    }

    public static (string System, string User) BuildPlanPrompt(StudioVideo v, IReadOnlyList<StudioMusicTrack> music)
    {
        var system =
            "Bạn là đạo diễn phim ngắn người thật cho mạng xã hội. Bạn chia kịch bản thành các shot để model TEXT-TO-VIDEO Seedance " +
            "tạo (mỗi shot là 1 clip 2–12 giây, video KHÔNG có tiếng — lời thoại được lồng bằng giọng đọc AI, nhạc nền thêm sau). Chỉ trả về JSON hợp lệ.";
        var sb = new StringBuilder();
        sb.AppendLine($"KỊCH BẢN (giữ nguyên lời thoại):\n{v.Script}\n");
        sb.AppendLine($"Khung hình {(v.AspectRatio == "16:9" ? "ngang 16:9" : "dọc 9:16")}. Độ dài video do KỊCH BẢN quyết định — không có thời lượng mục tiêu.");
        if (!string.IsNullOrWhiteSpace(v.StyleNotes)) sb.AppendLine($"Phong cách / ghi chú của người dùng: {v.StyleNotes}");
        sb.AppendLine("\nGIỌNG ĐỌC CÓ SẴN (id | giới tính | chất giọng):");
        foreach (var voice in StudioVoices.All) sb.AppendLine($"- {voice.Id} | {voice.Gender} | {voice.Description}");
        sb.AppendLine("\nKHO NHẠC NỀN (chọn 1 bài hợp không khí, hoặc null):");
        if (music.Count == 0) sb.AppendLine("- (trống)");
        foreach (var m in music) sb.AppendLine($"- id={m.Id} | {m.Name} | tâm trạng: {m.Mood} | {m.DurationSeconds:0}s");
        sb.AppendLine(@"
HỒ SƠ NHÂN VẬT & BỐI CẢNH (model video không có ảnh tham chiếu: cùng một mô tả được chép vào MỌI shot để nhân vật trông giống nhau):
- ""characters"": mọi nhân vật có thoại hoặc xuất hiện ≥ 2 shot. ""name"" đúng tên trong kịch bản; ""gender"": male/female;
  ""look"": mô tả ngoại hình bằng TIẾNG ANH, cụ thể và CỐ ĐỊNH, 25–45 từ: tuổi, dáng người, khuôn mặt, tóc / râu, trang phục với
  MÀU SẮC rõ ràng, phụ kiện đặc trưng (vd ""a stout 50-year-old Song dynasty judge, dark tanned face with a crescent mark on the
  forehead, long black beard, purple official robe embroidered with gold dragons, black winged hat"");
  ""voice"": id giọng hợp tuổi, giới tính và tính cách — mỗi nhân vật một giọng KHÁC nhau.
- Kịch bản có lời dẫn chuyện thì thêm nhân vật name ""Lời dẫn"" (không xuất hiện trong hình) với giọng phù hợp.
- ""locations"": mỗi bối cảnh một mục: ""name"" ngắn, ""look"" bằng TIẾNG ANH 20–40 từ: nơi chốn, thời đại, kiến trúc, đồ vật, ánh sáng, giờ.

SHOT:
- ""location"": tên một bối cảnh trong locations. ""cast"": tên các nhân vật NHÌN THẤY trong shot (đúng tên trong characters, kể cả
  người nghe). Vai phụ / quần chúng không cần hồ sơ: tả trong ""action"".
- Bám sát kịch bản: mỗi câu thoại và mỗi hành động / cảnh trong kịch bản thành shot tương ứng, đúng thứ tự. KHÔNG thêm shot độn,
  KHÔNG bỏ hay rút gọn phần nào. Mỗi shot tối đa 1 người nói. ""dialogue"" là câu thoại nguyên văn tiếng Việt (không dịch, không thêm);
  ""speaker"" là tên người nói (lời dẫn chuyện → ""Lời dẫn""); shot không thoại → null.
- ""duration"" (giây, số nguyên 2–12): shot có thoại >= số chữ / 3 + 1; câu dài hơn 30 chữ thì tách thành 2 shot liên tiếp.
- Giọng được LỒNG SAU nên miệng không khớp từng chữ: ưu tiên góc trung cảnh / toàn cảnh, người nói diễn bằng nét mặt và cử chỉ,
  KHÔNG dùng cận cảnh miệng (extreme close-up).
- ""camera"": shot đầu mỗi cảnh là wide establishing shot; còn lại ưu tiên medium-wide / two-shot thấy rõ người và bối cảnh;
  close-up tối đa 1/4 số shot (chỉ cho biểu cảm / đòn gây cười).
- ""action"", ""camera"", ""sound"" viết bằng TIẾNG ANH, cụ thể, dễ quay: ai làm gì, cảm xúc, cử chỉ có chủ đích; cỡ cảnh + chuyển động máy.
- ""caption"": chú thích đăng Facebook bằng tiếng Việt, 1–3 câu cuốn hút + 2–4 hashtag.

Trả về JSON:
{ ""caption"": ""..."", ""notes"": ""ghi chú đạo diễn ngắn bằng tiếng Việt"", ""musicTrackId"": ""id hoặc null"",
  ""characters"": [ { ""name"": ""..."", ""gender"": ""male"", ""look"": ""..."", ""voice"": ""gemini:..."" } ],
  ""locations"": [ { ""name"": ""..."", ""look"": ""..."" } ],
  ""shots"": [ { ""duration"": 6, ""location"": ""..."", ""cast"": [""...""], ""action"": ""..."", ""camera"": ""..."", ""speaker"": ""tên hoặc null"", ""dialogue"": ""... hoặc null"", ""sound"": ""..."" } ] }");
        return (system, sb.ToString());
    }

    public sealed record PlannedShot(int Duration, string? Location, List<string> Cast, string Action, string Camera, string? Speaker, string? Dialogue, string? Sound);
    public sealed record Plan(string? Caption, string? Notes, Guid? MusicTrackId, Bible Bible, List<PlannedShot> Shots);

    /// <summary>Reads the planner's JSON: bible (known voices, one per character), shots with valid durations and names.</summary>
    public static Plan ParsePlan(string json, ISet<Guid> knownMusic)
    {
        var text = json.Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidOperationException("Gemini không trả về kế hoạch dạng JSON.");
        using var doc = JsonDocument.Parse(text[start..(end + 1)]);
        var root = doc.RootElement;
        string? S(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()?.Trim() : null;

        var characters = new List<StudioCharacterDto>();
        if (root.TryGetProperty("characters", out var ca) && ca.ValueKind == JsonValueKind.Array)
            foreach (var c in ca.EnumerateArray())
                characters.Add(new StudioCharacterDto { Name = S(c, "name") ?? "", Gender = S(c, "gender") ?? "male", Look = S(c, "look") ?? "", Voice = S(c, "voice") ?? "" });
        var locations = new List<StudioLocationDto>();
        if (root.TryGetProperty("locations", out var la) && la.ValueKind == JsonValueKind.Array)
            foreach (var l in la.EnumerateArray())
                locations.Add(new StudioLocationDto { Name = S(l, "name") ?? "", Look = S(l, "look") ?? "" });
        // the planner sometimes gives two characters the same voice: keep the first, re-pick for the others
        var seen = new HashSet<string>();
        foreach (var c in characters)
            if (!StudioVoices.IsKnown(c.Voice) || !seen.Add(c.Voice)) c.Voice = "";
        var bible = NormalizeBible(characters, locations);

        Guid? musicId = Guid.TryParse(S(root, "musicTrackId"), out var mid) && knownMusic.Contains(mid) ? mid : null;
        var shots = new List<PlannedShot>();
        if (root.TryGetProperty("shots", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in arr.EnumerateArray())
            {
                var action = S(s, "action") ?? "";
                var dialogue = NullIfNull(S(s, "dialogue"));
                if (action.Length == 0 && dialogue == null) continue;
                var duration = s.TryGetProperty("duration", out var d) && d.TryGetDouble(out var dv) ? (int)Math.Round(dv) : 6;
                // the planner often under-times lines: never give a line less time than it takes to say it
                duration = SeedanceVideoClient.ClampSeconds(Math.Max(duration, SecondsToSay(dialogue)));
                var cast = new List<string>();
                if (s.TryGetProperty("cast", out var c) && c.ValueKind == JsonValueKind.Array)
                    foreach (var x in c.EnumerateArray())
                        if (x.ValueKind == JsonValueKind.String && bible.Character(x.GetString()) is { } who && !StudioVoices.NarratorName.Equals(who.Name)
                            && !cast.Contains(who.Name)) cast.Add(who.Name);
                var location = bible.Location(S(s, "location"))?.Name ?? NullIfNull(S(s, "location"));
                shots.Add(new PlannedShot(duration, location, cast, action, S(s, "camera") ?? "", NullIfNull(S(s, "speaker")), dialogue, NullIfNull(S(s, "sound"))));
            }
        }
        if (shots.Count == 0) throw new InvalidOperationException("Gemini trả về kế hoạch không có shot nào.");
        return new Plan(S(root, "caption"), S(root, "notes"), musicId, bible, shots);
    }

    /// <summary>Time an actor needs for a Vietnamese line: ~3 words (syllables) a second plus a 1 s breath, rounded up.</summary>
    public static int SecondsToSay(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return 0;
        var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return (int)Math.Ceiling(words / 3.0 + 1);
    }

    private static string? NullIfNull(string? s) => string.IsNullOrWhiteSpace(s) || s.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : s;

    public async Task<StudioVideoDto> ReplaceShotsAsync(Guid id, List<StudioShotInputDto> shots, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        EnsureNotBusy(v);
        var existing = v.Shots.ToDictionary(s => s.Id);
        var keep = new HashSet<Guid>();
        var index = 1;
        foreach (var input in shots)
        {
            var castJson = JsonSerializer.Serialize(input.Cast.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct());
            StudioShot shot;
            if (input.Id is { } sid && existing.TryGetValue(sid, out var found))
            {
                shot = found;
                var lineChanged = shot.Dialogue != NullIfEmpty(input.Dialogue) || shot.Speaker != NullIfEmpty(input.Speaker);
                var changed = lineChanged || shot.DurationSeconds != SeedanceVideoClient.ClampSeconds(input.DurationSeconds) || shot.Action != input.Action.Trim()
                              || shot.Camera != input.Camera.Trim() || shot.Sound != NullIfEmpty(input.Sound) || shot.CastJson != castJson
                              || shot.Location != NullIfEmpty(input.Location);
                if (changed && shot.Status == "Done") shot.Status = "Pending"; // edited → the clip no longer matches; regenerate
                if (changed) shot.InteractionId = null; // a pending job made from the old text must not be picked up
                if (lineChanged && shot.VoiceBlobId != null)
                {
                    await DeleteBlobAsync(shot.VoiceBlobId.Value, ct);
                    shot.VoiceBlobId = null;
                    shot.VoiceSeconds = null;
                }
            }
            else
            {
                shot = new StudioShot { StudioVideoId = v.Id };
                _db.StudioShots.Add(shot); // via the DbSet: see RunPlanAsync
            }
            keep.Add(shot.Id);
            shot.Index = index++;
            shot.DurationSeconds = SeedanceVideoClient.ClampSeconds(input.DurationSeconds);
            shot.Action = input.Action.Trim();
            shot.Camera = input.Camera.Trim();
            shot.Location = NullIfEmpty(input.Location);
            shot.CastJson = castJson;
            shot.Speaker = NullIfEmpty(input.Speaker);
            shot.Dialogue = NullIfEmpty(input.Dialogue);
            shot.Sound = NullIfEmpty(input.Sound);
        }
        foreach (var gone in v.Shots.Where(s => !keep.Contains(s.Id)).ToList())
        {
            if (gone.ClipBlobId != null) await DeleteBlobAsync(gone.ClipBlobId.Value, ct);
            if (gone.VoiceBlobId != null) await DeleteBlobAsync(gone.VoiceBlobId.Value, ct);
            if (gone.KeyframeBlobId != null) await DeleteBlobAsync(gone.KeyframeBlobId.Value, ct);
            _db.StudioShots.Remove(gone);
            v.Shots.Remove(gone);
        }
        v.EstimatedCostUsd = EstimateVideoCost(v);
        if (v.Shots.Count > 0) v.TargetDurationSeconds = v.Shots.Sum(s => SeedanceVideoClient.ClampSeconds(s.DurationSeconds));
        if (v.Status is "Draft" or "Ready" or "Failed") v.Status = v.Shots.Count > 0 ? "Planned" : "Draft";
        await _db.SaveChangesAsync(ct);
        return await GetVideoAsync(id, ct);
    }

    private decimal EstimateVideoCost(StudioVideo v) => Math.Round(v.Shots.Sum(s => ShotCost(v, s)), 4);

    /// <summary>Clip seconds × the model's price, plus reading the line when it has no voice yet.</summary>
    private decimal ShotCost(StudioVideo v, StudioShot s) =>
        _video.EstimateCost(v.VideoModel, v.Resolution, s.DurationSeconds)
        + (s.Dialogue != null && s.VoiceBlobId == null ? Math.Round(SecondsToSay(s.Dialogue) * _voices.PricePerSecond, 4) : 0m);

    // ═══════════════════════════════ Voices ═══════════════════════════════

    /// <summary>Reads the lines aloud (all missing ones, or one shot's again) so they can be heard before paying for clips.</summary>
    public async Task<StudioVideoDto> StartVoicesAsync(Guid id, Guid? shotId, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        EnsureNotBusy(v);
        var todo = v.Shots.Where(s => s.Dialogue != null && (shotId != null ? s.Id == shotId : s.VoiceBlobId == null)).ToList();
        if (todo.Count == 0)
            throw new InvalidOperationException(shotId != null ? "Shot này không có lời thoại." : "Mọi câu thoại đều đã có giọng đọc — bấm \"Đọc lại\" ở từng shot nếu muốn đổi.");
        v.Status = "Generating";
        v.Progress = $"Đang đọc {todo.Count} câu thoại bằng giọng AI…";
        v.ErrorMessage = null;
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<StudioService>(s => s.RunVoicesAsync(id, shotId, CancellationToken.None));
        return await GetVideoAsync(id, ct);
    }

    [Queue("critical")]
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(1800)]
    public async Task RunVoicesAsync(Guid id, Guid? shotId, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (v == null || v.Status != "Generating") return;
        var bible = ReadBible(v.BibleJson);
        var todo = v.Shots.OrderBy(s => s.Index).Where(s => s.Dialogue != null && (shotId != null ? s.Id == shotId : s.VoiceBlobId == null)).ToList();
        var errors = new List<string>();
        var n = 0;
        foreach (var s in todo)
        {
            v.Progress = $"Đang đọc câu thoại {++n}/{todo.Count} (shot {s.Index})…";
            await _db.SaveChangesAsync(ct);
            try
            {
                await MakeVoiceAsync(v, bible, s, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                s.ErrorMessage = "Giọng đọc: " + ex.Message;
                errors.Add($"shot {s.Index}");
            }
            await _db.SaveChangesAsync(ct);
        }
        v.TargetDurationSeconds = Math.Max(1, v.Shots.Sum(s => s.DurationSeconds));
        v.EstimatedCostUsd = EstimateVideoCost(v);
        v.Status = v.FinalBlobId != null ? "Ready" : "Planned";
        v.Progress = null;
        v.ErrorMessage = errors.Count == 0 ? null : $"Chưa đọc được câu thoại của {string.Join(", ", errors)} — xem lỗi ở từng shot.";
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Reads a shot's line in its speaker's voice and fits the shot to it (the clip must outlast the line). A line too
    /// long for one clip is refused before any video is paid for.
    /// </summary>
    private async Task MakeVoiceAsync(StudioVideo v, Bible bible, StudioShot s, CancellationToken ct)
    {
        if (s.Dialogue == null) return;
        var voice = VoiceFor(bible, s.Speaker);
        var (audio, seconds, fromCache) = await _voices.SpeakAsync(v.Id, s.Dialogue, voice, ct);
        if (seconds + VoiceTail > SeedanceVideoClient.MaxSeconds)
            throw new InvalidOperationException($"Câu thoại đọc mất {seconds:0.0}s — dài hơn 1 clip ({SeedanceVideoClient.MaxSeconds}s). Tách câu này thành 2 shot.");
        if (s.VoiceBlobId != null) await DeleteBlobAsync(s.VoiceBlobId.Value, ct);
        var blob = new StudioBlob { MimeType = "audio/mp4", Data = audio, SizeBytes = audio.Length };
        _db.StudioBlobs.Add(blob);
        s.VoiceBlobId = blob.Id;
        s.VoiceSeconds = seconds;
        s.DurationSeconds = SeedanceVideoClient.ClampSeconds(Math.Max(s.DurationSeconds, (int)Math.Ceiling(seconds + VoiceTail)));
        if (s.ErrorMessage?.StartsWith("Giọng đọc") == true) s.ErrorMessage = null;
        var cost = fromCache ? 0m : Math.Round((decimal)seconds * _voices.PricePerSecond, 5);
        s.CostUsd += cost;
        v.ActualCostUsd += cost;
        _db.StudioCostRecords.Add(new StudioCostRecord
        {
            StudioVideoId = v.Id, StudioShotId = s.Id, Kind = "Voice", Model = voice, Seconds = seconds, CostUsd = cost,
            Note = $"giọng shot {s.Index}" + (fromCache ? " (dùng lại, miễn phí)" : "")
        });
        await _db.SaveChangesAsync(ct);
    }

    // ═══════════════════════════════ Generation ═══════════════════════════════

    /// <summary>Queues clip generation for every shot without a clip (or only <paramref name="shotId"/>), then composing.</summary>
    public async Task<StudioVideoDto> StartGenerateAsync(Guid id, Guid? shotId, CancellationToken ct)
    {
        if (!_video.IsConfigured) throw new InvalidOperationException("Chưa có khoá Vercel AI Gateway (biến AI_GATEWAY_API_KEY trên Railway) — chưa tạo được video.");
        await ReconcileLostFilesAsync(id, ct);
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        EnsureNotBusy(v);
        if (v.Shots.Count == 0) throw new InvalidOperationException("Video chưa có shot nào — hãy lập kế hoạch trước.");

        var todo = shotId != null ? v.Shots.Where(s => s.Id == shotId).ToList() : v.Shots.Where(s => s.Status != "Done" || s.ClipBlobId == null).ToList();
        if (shotId != null && todo.Count == 0) throw new KeyNotFoundException("Không tìm thấy shot.");
        var cost = todo.Sum(s => ShotCost(v, s));
        await EnsureBudgetAsync(cost, ct);

        foreach (var s in todo)
        {
            s.Status = "Pending";
            s.ErrorMessage = null;
        }
        v.Status = "Generating";
        v.ErrorMessage = null;
        v.Progress = $"Chờ tạo {todo.Count} clip (~{cost:0.00} USD)…";
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<StudioService>(s => s.RunGenerationAsync(id, shotId, CancellationToken.None));
        return await GetVideoAsync(id, ct);
    }

    [Queue("render")]
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(7200)]
    public async Task RunGenerationAsync(Guid id, Guid? shotId, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct);
        // a second copy of this job (Hangfire re-queues long jobs / jobs cut by a restart) must not pay again for a
        // run that already finished: only a video still marked "Generating" is worked on
        if (v == null || v.Status != "Generating") return;
        if (v.Seed == 0) v.Seed = NewSeed();
        var bible = ReadBible(v.BibleJson);
        var todo = v.Shots.OrderBy(s => s.Index)
            .Where(s => shotId != null ? s.Id == shotId : s.Status != "Done" || s.ClipBlobId == null).ToList();
        var failed = new List<string>();
        var done = 0;
        string? accountProblem = null;

        foreach (var shot in todo)
        {
            v.Progress = $"Đang tạo clip {++done}/{todo.Count} (shot {shot.Index})…";
            // a job already paid for and not turned into a clip yet (restart, timeout, failed download) is picked up
            // again instead of paying for a new one
            var resume = shot.ClipBlobId == null ? shot.InteractionId : null;
            shot.Status = "Generating";
            await _db.SaveChangesAsync(ct);

            // 1. the line first: its length decides the clip's length
            if (shot.Dialogue != null && shot.VoiceBlobId == null)
            {
                try { await MakeVoiceAsync(v, bible, shot, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    shot.Status = "Failed";
                    shot.ErrorMessage = "Giọng đọc: " + ex.Message;
                    failed.Add($"shot {shot.Index}: {shot.ErrorMessage}");
                    await _db.SaveChangesAsync(ct);
                    continue; // no clip paid for a shot whose line can't be voiced
                }
            }

            var seconds = SeedanceVideoClient.ClampSeconds(shot.DurationSeconds);
            try
            {
                await EnsureBudgetAsync(_video.EstimateCost(v.VideoModel, v.Resolution, seconds), ct);
            }
            catch (InvalidOperationException ex)
            {
                shot.Status = "Failed";
                shot.ErrorMessage = ex.Message;
                failed.Add($"shot {shot.Index}: {ex.Message}");
                break; // budget reached: stop spending
            }

            var prompt = BuildShotPrompt(v, bible, shot);
            for (var attempt = 1; attempt <= MaxAttemptsPerRun; attempt++)
            {
                shot.Attempts++;
                try
                {
                    var clip = await _video.GenerateAsync(v.VideoModel, prompt, v.AspectRatio, v.Resolution, seconds, v.Seed, ct, resume, async operation =>
                    {
                        shot.InteractionId = operation; // stored at once: the job is billed from here on
                        await _db.SaveChangesAsync(CancellationToken.None);
                    });
                    if (shot.ClipBlobId != null) await DeleteBlobAsync(shot.ClipBlobId.Value, ct);
                    var b = await StoreOnDiskAsync(clip.Video, clip.MimeType, ct);
                    shot.ClipBlobId = b.Id;
                    shot.InteractionId = clip.RequestId;
                    shot.CostUsd += clip.CostUsd;
                    shot.Status = "Done";
                    shot.ErrorMessage = null;
                    v.ActualCostUsd += clip.CostUsd;
                    _db.StudioCostRecords.Add(new StudioCostRecord
                    {
                        StudioVideoId = v.Id, StudioShotId = shot.Id, Kind = "Video", Model = SeedanceVideoClient.NormalizeModel(v.VideoModel),
                        Seconds = clip.Seconds, CostUsd = clip.CostUsd,
                        Note = $"shot {shot.Index}, {SeedanceVideoClient.NormalizeResolution(v.Resolution)}" + (clip.CostUsd == 0 ? ", lấy lại job đã trả tiền" : "")
                    });
                    await _db.SaveChangesAsync(ct);
                    break;
                }
                catch (VideoAccountException ex)
                {
                    // out of credits / key refused: every next shot would fail the same way — stop the whole run
                    shot.Status = "Pending";
                    shot.ErrorMessage = null;
                    accountProblem = ex.Message;
                    break;
                }
                catch (VideoRejectedException ex)
                {
                    // refused (moderation, invalid argument): retrying the same prompt only wastes time
                    shot.Status = "Failed";
                    shot.ErrorMessage = ex.Message;
                    break;
                }
                catch (VideoTransientException ex)
                {
                    // busy / internal error before anything billable: one more try after a pause
                    _logger.LogWarning(ex, "Video shot {Index} attempt {Attempt}: transient", shot.Index, attempt);
                    shot.Status = "Failed";
                    shot.ErrorMessage = ex.Message;
                    resume = null;
                    if (attempt < MaxAttemptsPerRun) await Task.Delay(TimeSpan.FromSeconds(20), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // a job may already be paid for (timeout, download): never auto-retry — "Tạo lại" resumes it
                    _logger.LogWarning(ex, "Video shot {Index} attempt {Attempt} failed", shot.Index, attempt);
                    shot.Status = "Failed";
                    shot.ErrorMessage = ex.Message;
                    break;
                }
            }
            if (accountProblem != null) break;
            if (shot.Status != "Done") failed.Add($"shot {shot.Index}: {shot.ErrorMessage}");
            await _db.SaveChangesAsync(ct);
        }

        v.TargetDurationSeconds = Math.Max(1, v.Shots.Sum(s => s.DurationSeconds));
        if (accountProblem != null)
        {
            var made = v.Shots.Count(s => s.ClipBlobId != null && s.Status == "Done");
            v.Status = "Failed";
            v.Progress = null;
            v.ErrorMessage = $"Dừng ở {made}/{v.Shots.Count} clip: {accountProblem}";
            await _db.SaveChangesAsync(ct);
            return;
        }

        if (failed.Count > 0)
        {
            v.Status = "Failed";
            v.Progress = null;
            v.ErrorMessage = $"{failed.Count} clip lỗi — sửa shot rồi bấm tạo lại: " + string.Join(" | ", failed.Take(3));
            await _db.SaveChangesAsync(ct);
            return;
        }
        await ComposeInternalAsync(v, ct);
    }

    /// <summary>
    /// The text-to-video prompt of one shot. With no reference images, the look of every visible character and of the
    /// location is copied word for word from the bible into each shot, and the same seed is used, so people stay alike.
    /// The video is silent: dialogue is dubbed later, so the speaker acts the line without mouth close-ups.
    /// </summary>
    public static string BuildShotPrompt(StudioVideo v, Bible bible, StudioShot shot)
    {
        var sb = new StringBuilder();
        sb.Append($"Live-action cinematic film shot, photorealistic, {(v.AspectRatio == "16:9" ? "horizontal 16:9" : "vertical 9:16")} framing. ");
        var location = bible.Location(shot.Location);
        if (location != null) sb.Append("Setting: ").Append(location.Look.TrimEnd('.')).Append(". ");
        else if (!string.IsNullOrWhiteSpace(shot.Location)) sb.Append("Setting: ").Append(shot.Location.TrimEnd('.')).Append(". ");
        var cast = Names(shot.CastJson).Select(n => bible.Character(n)).Where(c => c != null && !c.Name.Equals(StudioVoices.NarratorName)).ToList();
        if (cast.Count > 0)
        {
            sb.Append($"Characters in frame ({cast.Count}, each appears exactly once): ");
            sb.Append(string.Join("; ", cast.Select(c => $"{c!.Name} — {c.Look.TrimEnd('.')}"))).Append(". ");
        }
        sb.Append("Action: ").Append(shot.Action.TrimEnd('.')).Append(". ");
        if (!string.IsNullOrWhiteSpace(shot.Camera)) sb.Append("Camera: ").Append(shot.Camera.TrimEnd('.')).Append(". ");
        if (shot.Dialogue != null && !IsNarrator(shot.Speaker))
            sb.Append($"{shot.Speaker} is talking with expressive face and hand gestures while the others react; keep the framing medium or wide, no extreme close-up of the mouth. ");
        else
            sb.Append("Nobody is talking to the camera. ");
        if (!string.IsNullOrWhiteSpace(v.StyleNotes)) sb.Append("Style: ").Append(v.StyleNotes!.TrimEnd('.')).Append(". ");
        sb.Append("Continuous natural motion for the whole clip (people keep acting, background alive, smooth camera); no frozen frames, no slow motion. ");
        sb.Append("Consistent faces and costumes, realistic hands. No text, no subtitles, no captions, no logos, no watermark.");
        return sb.ToString();
    }

    // ═══════════════════════════════ Recovering ═══════════════════════════════

    /// <summary>
    /// Gets back clips whose files were lost (app disk wiped by a redeploy / restart) by downloading the already-paid
    /// jobs again — free, nothing new is generated. Allowed while a compose run is stuck, since that run is dead.
    /// </summary>
    public async Task<StudioVideoDto> StartRecoverAsync(Guid id, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        if (v.Status is "Planning" or "Generating" or "Publishing")
            throw new InvalidOperationException($"Video đang trong bước \"{v.Status}\" — chờ xong rồi thao tác tiếp.");
        if (!v.Shots.Any(s => !string.IsNullOrEmpty(s.InteractionId)))
            throw new InvalidOperationException("Video chưa có clip nào từng được tạo để lấy lại.");
        v.Status = "Generating";
        v.Progress = "Đang lấy lại các clip đã trả tiền (miễn phí)…";
        v.ErrorMessage = null;
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<StudioService>(s => s.RunRecoverAsync(id, CancellationToken.None));
        return await GetVideoAsync(id, ct);
    }

    [Queue("render")]
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(3600)]
    public async Task RunRecoverAsync(Guid id, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (v == null || v.Status != "Generating") return;
        var shots = v.Shots.OrderBy(s => s.Index).ToList();
        var lost = new List<StudioShot>();
        foreach (var s in shots)
            if (s.ClipBlobId == null || await GetBlobAsync(s.ClipBlobId.Value, ct) == null) lost.Add(s);

        int recovered = 0, done = 0;
        var gone = new List<int>();
        foreach (var s in lost)
        {
            v.Progress = $"Đang lấy lại clip {++done}/{lost.Count} (shot {s.Index}) — miễn phí…";
            await _db.SaveChangesAsync(ct);
            (byte[] Bytes, string Mime)? file = null;
            if (!string.IsNullOrEmpty(s.InteractionId))
            {
                try { file = await _video.TryRecoverAsync(v.VideoModel, s.InteractionId, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Recover shot {Index}", s.Index); }
            }
            if (s.ClipBlobId != null) await DeleteBlobAsync(s.ClipBlobId.Value, ct);
            if (file is { } f)
            {
                var b = await StoreOnDiskAsync(f.Bytes, f.Mime, ct);
                s.ClipBlobId = b.Id;
                s.Status = "Done";
                s.ErrorMessage = null;
                recovered++;
            }
            else
            {
                s.ClipBlobId = null;
                s.Status = "Pending";
                s.InteractionId = null; // nothing left to fetch: the next "Tạo" makes a new (paid) clip
                s.ErrorMessage = "Nhà cung cấp không còn giữ clip này (link hết hạn) — cần tạo lại shot (tốn tiền).";
                gone.Add(s.Index);
            }
            await _db.SaveChangesAsync(ct);
        }

        if (gone.Count == 0)
        {
            if (v.FinalBlobId != null && await GetBlobAsync(v.FinalBlobId.Value, ct) == null)
            {
                await DeleteBlobAsync(v.FinalBlobId.Value, ct);
                v.FinalBlobId = null;
            }
            v.Progress = $"Đã lấy lại {recovered} clip — đang ghép video…";
            await _db.SaveChangesAsync(ct);
            await ComposeInternalAsync(v, ct);
            return;
        }
        v.Status = "Failed";
        v.Progress = null;
        var cost = gone.Sum(i => ShotCost(v, shots.First(s => s.Index == i)));
        v.ErrorMessage = $"Lấy lại được {recovered}/{lost.Count} clip miễn phí. Shot {string.Join(", ", gone)} đã hết hạn — " +
                         $"bấm \"Tạo các clip còn thiếu\" để tạo lại (~{cost:0.00} USD).";
        await _db.SaveChangesAsync(ct);
    }

    // ═══════════════════════════════ Composing ═══════════════════════════════

    public async Task<StudioVideoDto> StartComposeAsync(Guid id, CancellationToken ct)
    {
        await ReconcileLostFilesAsync(id, ct);
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        EnsureNotBusy(v);
        if (v.Shots.Count == 0 || v.Shots.Any(s => s.ClipBlobId == null))
            throw new InvalidOperationException("Còn shot chưa có clip — tạo đủ clip trước khi ghép.");
        v.Status = "Composing";
        v.Progress = "Đang ghép video…";
        v.ErrorMessage = null;
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<StudioService>(s => s.RunComposeAsync(id, CancellationToken.None));
        return await GetVideoAsync(id, ct);
    }

    [Queue("render")]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunComposeAsync(Guid id, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (v != null) await ComposeInternalAsync(v, ct);
    }

    private async Task ComposeInternalAsync(StudioVideo v, CancellationToken ct)
    {
        v.Status = "Composing";
        v.Progress = "Đang ghép video, giọng đọc và nhạc nền…";
        await _db.SaveChangesAsync(ct);
        var dir = StudioMedia.TempDir();
        try
        {
            var clips = new List<string>();
            var voices = new List<string?>();
            foreach (var s in v.Shots.OrderBy(s => s.Index))
            {
                var blob = s.ClipBlobId == null ? null : await GetBlobAsync(s.ClipBlobId.Value, ct);
                if (blob == null) throw new InvalidOperationException($"Shot {s.Index} chưa có clip.");
                var path = Path.Combine(dir, $"clip_{s.Index:D3}.mp4");
                await File.WriteAllBytesAsync(path, blob.Data, ct);
                clips.Add(path);
                string? voicePath = null;
                if (s.VoiceBlobId != null && await GetBlobAsync(s.VoiceBlobId.Value, ct) is { } vb)
                {
                    voicePath = Path.Combine(dir, $"voice_{s.Index:D3}.m4a");
                    await File.WriteAllBytesAsync(voicePath, vb.Data, ct);
                }
                voices.Add(voicePath);
            }

            string? musicPath = null;
            if (v.MusicMode != "None" && v.MusicTrackId != null)
            {
                var track = await _db.StudioMusicTracks.FirstOrDefaultAsync(m => m.Id == v.MusicTrackId, ct);
                var mblob = track == null ? null : await GetBlobAsync(track.BlobId, ct);
                if (track != null && mblob != null)
                {
                    musicPath = Path.Combine(dir, "music" + ExtensionFor(mblob.MimeType));
                    await File.WriteAllBytesAsync(musicPath, mblob.Data, ct);
                    track.UseCount++;
                    track.LastUsedAtUtc = DateTime.UtcNow;
                }
            }

            // brand = the Page the video is for (moving watermark against re-uploads); same fallback as news videos
            var (brand, logo) = await BrandAsync(v.FacebookAccountId, ct);
            var watermark = Path.Combine(dir, "watermark.png");
            await _media.CreateWatermarkAsync(brand, logo, watermark, ct);

            var output = Path.Combine(dir, "final.mp4");
            // delivered at 1080p whatever the clip resolution (Facebook keeps a sharper copy of 1080p uploads)
            var duration = await _media.ComposeAsync(clips, musicPath, v.MusicVolume, v.AspectRatio, DeliveryResolution, output, ct, watermark, brand, voices);
            var bytes = await File.ReadAllBytesAsync(output, ct);
            if (v.FinalBlobId != null) await DeleteBlobAsync(v.FinalBlobId.Value, ct);
            var final = await StoreOnDiskAsync(bytes, "video/mp4", ct);
            v.FinalBlobId = final.Id;
            v.FinalDurationSeconds = Math.Round(duration, 1);
            v.Status = "Ready";
            v.Progress = null;
            v.ErrorMessage = null;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Studio compose failed for {Id}", v.Id);
            v.Status = "Failed";
            v.Progress = null;
            v.ErrorMessage = "Ghép video thất bại: " + ex.Message;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            StudioMedia.TryDelete(dir);
        }
    }

    // ═══════════════════════════════ Publishing ═══════════════════════════════

    public async Task<StudioVideoDto> StartPublishAsync(Guid id, string? caption, CancellationToken ct)
    {
        await ReconcileLostFilesAsync(id, ct);
        var v = await _db.StudioVideos.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        EnsureNotBusy(v);
        if (v.FinalBlobId == null) throw new InvalidOperationException("Video chưa ghép xong.");
        if (v.FacebookAccountId == null) throw new InvalidOperationException("Chưa chọn Page để đăng.");
        if (caption != null) v.Caption = caption.Trim();
        v.Status = "Publishing";
        v.Progress = "Đang đăng lên Facebook…";
        v.ErrorMessage = null;
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<StudioService>(s => s.RunPublishAsync(id, CancellationToken.None));
        return await GetVideoAsync(id, ct);
    }

    /// <summary>The caption always ends with an AI disclosure (Meta requires labelling realistic AI video).</summary>
    public static string CaptionWithDisclosure(string? caption)
    {
        var text = (caption ?? "").Trim();
        const string disclosure = "🤖 Video được tạo bằng AI.";
        return text.Contains("tạo bằng AI", StringComparison.OrdinalIgnoreCase) ? text : (text + "\n\n" + disclosure).Trim();
    }

    [Queue("render")]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunPublishAsync(Guid id, CancellationToken ct)
    {
        var v = await _db.StudioVideos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (v == null || v.FinalBlobId == null || v.FacebookAccountId == null) return;
        var dir = StudioMedia.TempDir();
        try
        {
            var blob = await GetBlobAsync(v.FinalBlobId.Value, ct) ?? throw new InvalidOperationException("Mất file video.");
            var path = Path.Combine(dir, "post.mp4");
            await File.WriteAllBytesAsync(path, blob.Data, ct);
            var (_, postId) = await _facebook.PublishVideoFileAsync(v.FacebookAccountId.Value, path, CaptionWithDisclosure(v.Caption),
                v.FinalDurationSeconds ?? 0, ct);
            v.PublishedPostId = postId;
            v.PublishedAtUtc = DateTime.UtcNow;
            v.Status = "Published";
            v.Progress = null;
            await _db.SaveChangesAsync(ct);
            // posted: the clips have done their job — free the disk (the final video stays until the next redeploy)
            var posted = await _db.StudioVideos.Include(x => x.Shots).FirstAsync(x => x.Id == id, ct);
            foreach (var s in posted.Shots.Where(s => s.ClipBlobId != null))
            {
                await DeleteBlobAsync(s.ClipBlobId!.Value, ct);
                s.ClipBlobId = null;
                s.InteractionId = null;
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Studio publish failed for {Id}", v.Id);
            v.Status = "Ready";
            v.Progress = null;
            v.ErrorMessage = "Đăng Facebook thất bại: " + ex.Message;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            StudioMedia.TryDelete(dir);
        }
    }

    /// <summary>Frees the disk space of the clips once the final video exists (it can still be downloaded).</summary>
    public async Task<StudioVideoDto> PurgeClipsAsync(Guid id, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        EnsureNotBusy(v);
        if (v.FinalBlobId == null) throw new InvalidOperationException("Chưa có video hoàn chỉnh — giữ lại các clip.");
        foreach (var s in v.Shots.Where(s => s.ClipBlobId != null))
        {
            await DeleteBlobAsync(s.ClipBlobId!.Value, ct);
            s.ClipBlobId = null;
            s.InteractionId = null;
        }
        await _db.SaveChangesAsync(ct);
        return await GetVideoAsync(id, ct);
    }

    // ═══════════════════════════════ Costs & budget ═══════════════════════════════

    public async Task<decimal> MonthlyBudgetAsync(CancellationToken ct)
    {
        var stored = await _db.SystemSettings.AsNoTracking().Where(s => s.SettingKey == BudgetKey).Select(s => s.SettingValue).FirstOrDefaultAsync(ct);
        if (decimal.TryParse(stored, NumberStyles.Number, CultureInfo.InvariantCulture, out var b)) return b;
        return decimal.TryParse(_configuration["Studio:MonthlyBudgetUsd"], NumberStyles.Number, CultureInfo.InvariantCulture, out var c) ? c : 30m;
    }

    public async Task<StudioSettingsDto> SetMonthlyBudgetAsync(decimal budget, CancellationToken ct)
    {
        budget = Math.Clamp(Math.Round(budget, 2), 0, 100000);
        var row = await _db.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == BudgetKey, ct);
        var value = budget.ToString(CultureInfo.InvariantCulture);
        if (row == null) _db.SystemSettings.Add(new SystemSetting { SettingKey = BudgetKey, SettingValue = value, Group = "Studio" });
        else row.SettingValue = value;
        await _db.SaveChangesAsync(ct);
        return await GetSettingsAsync(ct);
    }

    public async Task<StudioSettingsDto> GetSettingsAsync(CancellationToken ct) => new()
    {
        MonthlyBudgetUsd = await MonthlyBudgetAsync(ct),
        VideoConfigured = _video.IsConfigured,
        VideoModel = _video.DefaultModel,
        Models = ModelDtos(),
        Voices = StudioVoices.Dtos().ToList(),
        VoicePricePerSecondUsd = _voices.PricePerSecond
    };

    private List<StudioModelDto> ModelDtos() => _video.Models()
        .Select(m => new StudioModelDto { Id = m.Id, Label = m.Label, Note = m.Note, PricePerSecondUsd = m.PricePerSecond.ToDictionary(p => p.Key, p => p.Value) })
        .ToList();

    private static DateTime MonthStartUtc() { var n = DateTime.UtcNow; return new DateTime(n.Year, n.Month, 1, 0, 0, 0, DateTimeKind.Utc); }

    private async Task<decimal> MonthSpentAsync(CancellationToken ct)
    {
        var start = MonthStartUtc();
        return await _db.StudioCostRecords.Where(c => c.CreatedAtUtc >= start).SumAsync(c => (decimal?)c.CostUsd, ct) ?? 0m;
    }

    /// <summary>Refuses a paid step that would take this month's studio spending over the budget (0 = no cap).</summary>
    private async Task EnsureBudgetAsync(decimal cost, CancellationToken ct)
    {
        var budget = await MonthlyBudgetAsync(ct);
        if (budget <= 0) return;
        var spent = await MonthSpentAsync(ct);
        if (spent + cost > budget)
            throw new InvalidOperationException(
                $"Vượt ngân sách tháng của Studio: đã chi {spent:0.00} USD, bước này cần ~{cost:0.00} USD, trần {budget:0.00} USD. Tăng trần trong tab Chi phí nếu muốn làm tiếp.");
    }

    public async Task<StudioCostSummaryDto> GetCostsAsync(CancellationToken ct)
    {
        var start = MonthStartUtc();
        var budget = await MonthlyBudgetAsync(ct);
        var month = await MonthSpentAsync(ct);
        var titles = await _db.StudioVideos.AsNoTracking().Select(v => new { v.Id, v.Title, v.ActualCostUsd, v.CreatedAtUtc }).ToListAsync(ct);
        var titleById = titles.ToDictionary(t => t.Id, t => t.Title);
        var recent = await _db.StudioCostRecords.AsNoTracking().OrderByDescending(c => c.CreatedAtUtc).Take(50).ToListAsync(ct);
        return new StudioCostSummaryDto
        {
            MonthSpentUsd = Math.Round(month, 4),
            MonthlyBudgetUsd = budget,
            MonthRemainingUsd = budget <= 0 ? 0 : Math.Max(0, Math.Round(budget - month, 4)),
            AllTimeSpentUsd = Math.Round(await _db.StudioCostRecords.SumAsync(c => (decimal?)c.CostUsd, ct) ?? 0m, 4),
            MonthVideoSeconds = (int)Math.Round(await _db.StudioCostRecords.Where(c => c.CreatedAtUtc >= start && c.Kind == "Video").SumAsync(c => c.Seconds ?? 0, ct)),
            Models = ModelDtos(),
            Videos = titles.OrderByDescending(t => t.CreatedAtUtc).Take(100)
                .Select(t => new StudioCostVideoDto { Id = t.Id, Title = t.Title, CostUsd = Math.Round(t.ActualCostUsd, 4), CreatedAtUtc = t.CreatedAtUtc }).ToList(),
            Recent = recent.Select(c => new StudioCostRecordDto
            {
                CreatedAtUtc = c.CreatedAtUtc, Kind = c.Kind, Model = c.Model, Seconds = c.Seconds, CostUsd = c.CostUsd, Note = c.Note,
                VideoTitle = c.StudioVideoId != null && titleById.TryGetValue(c.StudioVideoId.Value, out var t) ? t : null
            }).ToList()
        };
    }

    // ═══════════════════════════════ Helpers ═══════════════════════════════

    /// <summary>Page name (fallback Branding:Name, then "Trạm Tin 24/7") and the Page's brand-kit logo, if any.</summary>
    private async Task<(string Name, byte[]? Logo)> BrandAsync(Guid? pageId, CancellationToken ct)
    {
        var name = pageId == null ? null
            : await _db.FacebookAccounts.AsNoTracking().Where(a => a.Id == pageId).Select(a => a.PageName).FirstOrDefaultAsync(ct);
        var logo = pageId == null ? null
            : await _db.AutoPostSettings.AsNoTracking().Where(s => s.FacebookAccountId == pageId).Select(s => s.BrandLogo).FirstOrDefaultAsync(ct);
        var brand = !string.IsNullOrWhiteSpace(name) ? name.Trim()
            : _configuration["Branding:Name"] is { Length: > 0 } configured ? configured : "Trạm Tin 24/7";
        return (brand, logo is { Length: > 0 } ? logo : null);
    }

    private async Task<Dictionary<Guid, string>> PageNamesAsync(CancellationToken ct) =>
        await _db.FacebookAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.PageName, ct);

    private static T FillSummary<T>(T dto, StudioVideo v, Dictionary<Guid, string> pages) where T : StudioVideoSummaryDto
    {
        dto.Id = v.Id;
        dto.Title = v.Title;
        dto.FacebookAccountId = v.FacebookAccountId;
        dto.PageName = v.FacebookAccountId != null && pages.TryGetValue(v.FacebookAccountId.Value, out var n) ? n : null;
        dto.TargetDurationSeconds = v.TargetDurationSeconds;
        dto.Status = v.Status;
        dto.Progress = v.Progress;
        dto.ShotCount = v.Shots.Count;
        dto.EstimatedCostUsd = v.EstimatedCostUsd;
        dto.ActualCostUsd = Math.Round(v.ActualCostUsd, 4);
        dto.HasFinal = v.FinalBlobId != null;
        dto.CreatedAtUtc = v.CreatedAtUtc;
        dto.PublishedAtUtc = v.PublishedAtUtc;
        return dto;
    }

    /// <summary>Planning never takes this long; a video still "Planning" after it was cut off (e.g. by a restart).</summary>
    private static readonly TimeSpan StalePlanning = TimeSpan.FromMinutes(10);

    private static void EnsureNotBusy(StudioVideo v)
    {
        if (v.Status == "Planning" && DateTime.UtcNow - (v.UpdatedAtUtc ?? v.CreatedAtUtc) > StalePlanning)
        {
            v.Status = v.Shots.Count > 0 ? "Planned" : "Draft"; // stuck: free it
            v.Progress = null;
            return;
        }
        if (v.Status is "Planning" or "Generating" or "Composing" or "Publishing")
            throw new InvalidOperationException($"Video đang trong bước \"{v.Status}\" — chờ xong rồi thao tác tiếp.");
    }

    private async Task DeleteBlobAsync(Guid id, CancellationToken ct)
    {
        var blob = await _db.StudioBlobs.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (blob != null) _db.StudioBlobs.Remove(blob);
        _files.Delete(id);
    }

    public static List<string> Names(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json, Json) ?? new List<string>(); }
        catch (JsonException) { return new List<string>(); }
    }

    private static string Required(string? value, string label) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{label} không được để trống.") : value.Trim();

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string ExtensionFor(string mime) => mime switch
    {
        "audio/mpeg" or "audio/mp3" => ".mp3",
        "audio/mp4" or "audio/x-m4a" or "audio/aac" => ".m4a",
        "audio/wav" or "audio/x-wav" or "audio/wave" => ".wav",
        "audio/ogg" => ".ogg",
        _ => ".bin"
    };

    private static StudioMusicDto ToDto(StudioMusicTrack m) => new()
    {
        Id = m.Id, Name = m.Name, Mood = m.Mood, DurationSeconds = m.DurationSeconds, SizeBytes = m.SizeBytes, UseCount = m.UseCount, CreatedAtUtc = m.CreatedAtUtc
    };
}

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
    /// <summary>
    /// The least room a line needs in its clip: it starts 0.25 s in (see the compose step) and keeps a short breath at
    /// the end. A line up to MaxSeconds − this still fits one clip; below it the full <see cref="VoiceTail"/> is given.
    /// </summary>
    public const double MinVoiceRoom = 0.4;
    private const int MaxAttemptsPerRun = 2;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly SeedanceVideoClient _video;
    private readonly FilmGeminiClient _gemini;
    private readonly StudioMedia _media;
    private readonly StudioFileStore _files;
    private readonly StudioVoices _voices;
    private readonly SoundEffectsClient _sfx;
    private readonly LyriaMusicClient _lyria;
    private readonly IFacebookPublishingService _facebook;
    private readonly IBackgroundJobClient _jobs;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StudioService> _logger;

    public StudioService(IApplicationDbContext db, SeedanceVideoClient video, FilmGeminiClient gemini, StudioMedia media, StudioFileStore files,
        StudioVoices voices, SoundEffectsClient sfx, LyriaMusicClient lyria, IFacebookPublishingService facebook, IBackgroundJobClient jobs,
        IConfiguration configuration, ILogger<StudioService> logger)
    {
        _lyria = lyria;
        _db = db;
        _video = video;
        _gemini = gemini;
        _media = media;
        _files = files;
        _voices = voices;
        _sfx = sfx;
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
            b.Characters.Add(new StudioCharacterDto
            {
                Name = c.Name.Trim(), Gender = gender, Look = (c.Look ?? "").Trim(), Voice = voice,
                Outfit = string.IsNullOrWhiteSpace(c.Outfit) ? null : c.Outfit.Trim(),
                Speed = string.IsNullOrWhiteSpace(c.Speed) ? null : StudioVoices.NormalizeSpeed(c.Speed)
            });
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

    /// <summary>Reading speed of a line's speaker (same lookup as <see cref="VoiceFor"/>).</summary>
    public static string SpeedFor(Bible bible, string? speaker) =>
        bible.Character(speaker)?.Speed is { Length: > 0 } s ? s
        : IsNarrator(speaker) && bible.Character(StudioVoices.NarratorName)?.Speed is { Length: > 0 } n ? n
        : "+0%";

    /// <summary>Gender of a line's speaker (the narrator's for narration).</summary>
    public static string? GenderOf(Bible bible, string? speaker) =>
        (bible.Character(speaker) ?? (IsNarrator(speaker) ? bible.Character(StudioVoices.NarratorName) : null))?.Gender;

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

    // ── AI-composed music (Google Lyria) ──

    public const string MusicJobKey = "Studio.MusicJob";

    private sealed class MusicJobState
    {
        public bool Running { get; set; }
        public int Total { get; set; }
        public int Done { get; set; }
        public DateTime StartedUtc { get; set; }
        public List<string> Added { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    private async Task<MusicJobState> ReadMusicJobAsync(CancellationToken ct)
    {
        var raw = await _db.SystemSettings.AsNoTracking().Where(s => s.SettingKey == MusicJobKey).Select(s => s.SettingValue).FirstOrDefaultAsync(ct);
        try { return string.IsNullOrWhiteSpace(raw) ? new MusicJobState() : JsonSerializer.Deserialize<MusicJobState>(raw, Json) ?? new MusicJobState(); }
        catch (JsonException) { return new MusicJobState(); }
    }

    private async Task SaveMusicJobAsync(MusicJobState state, CancellationToken ct)
    {
        var value = JsonSerializer.Serialize(state, Json);
        var row = await _db.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == MusicJobKey, ct);
        if (row == null) _db.SystemSettings.Add(new SystemSetting { SettingKey = MusicJobKey, SettingValue = value, Group = "Studio" });
        else row.SettingValue = value;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<StudioMusicAiDto> GetMusicAiAsync(CancellationToken ct)
    {
        var state = await ReadMusicJobAsync(ct);
        // a batch cut off by a restart never finishes: don't show it as running forever
        var running = state.Running && DateTime.UtcNow - state.StartedUtc < TimeSpan.FromMinutes(30);
        return new StudioMusicAiDto
        {
            Configured = _lyria.IsConfigured, Model = _lyria.Model, PricePerSongUsd = _lyria.PricePerSong,
            Briefs = LyriaMusicClient.Briefs.Select(b => new StudioMusicBriefDto { Id = b.Id, Name = b.Name, Mood = b.Mood }).ToList(),
            Running = running, Total = state.Total, Done = state.Done, Added = state.Added, Errors = state.Errors
        };
    }

    /// <summary>Queues original tracks composed by Lyria (ready-made briefs and / or the user's own wish) into the music store.</summary>
    public async Task<StudioMusicAiDto> StartMusicAiAsync(StudioMusicGenerateDto input, CancellationToken ct)
    {
        if (!_lyria.IsConfigured) throw new InvalidOperationException("Chưa có khoá Gemini trên server — chưa sáng tác nhạc được.");
        var current = await GetMusicAiAsync(ct);
        if (current.Running) throw new InvalidOperationException($"AI đang sáng tác ({current.Done}/{current.Total} bài) — chờ xong rồi tạo tiếp.");
        var ids = input.BriefIds.Where(id => LyriaMusicClient.Briefs.Any(b => b.Id == id)).Distinct().ToList();
        var wish = string.IsNullOrWhiteSpace(input.Wish) ? null : input.Wish.Trim();
        if (wish is { Length: > 500 }) wish = wish[..500];
        var count = ids.Count + (wish != null ? 1 : 0);
        if (count == 0) throw new ArgumentException("Chọn ít nhất 1 bài gợi ý hoặc ghi mong muốn của bạn.");
        if (count > 8) throw new ArgumentException("Mỗi lần sáng tác tối đa 8 bài.");
        await EnsureBudgetAsync(count * _lyria.PricePerSong, ct);
        await SaveMusicJobAsync(new MusicJobState { Running = true, Total = count, StartedUtc = DateTime.UtcNow }, ct);
        var name = string.IsNullOrWhiteSpace(input.Name) ? null : input.Name.Trim();
        var mood = string.IsNullOrWhiteSpace(input.Mood) ? null : input.Mood.Trim();
        _jobs.Enqueue<StudioService>(s => s.RunMusicAiAsync(ids, wish, name, mood, CancellationToken.None));
        return await GetMusicAiAsync(ct);
    }

    [Queue("default")]
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(3600)]
    public async Task RunMusicAiAsync(List<string> briefIds, string? wish, string? name, string? mood, CancellationToken ct)
    {
        var items = briefIds.Select(id => LyriaMusicClient.Briefs.FirstOrDefault(b => b.Id == id)).Where(b => b != null).Select(b => b!).ToList();
        if (wish != null)
            items.Add(new MusicBrief("wish", name ?? "Nhạc AI: " + (wish.Length > 40 ? wish[..40] + "…" : wish), mood ?? wish, LyriaMusicClient.CustomPrompt(wish)));
        var state = await ReadMusicJobAsync(ct);
        state.Running = true;
        state.Total = items.Count;
        state.Done = 0;
        state.Added.Clear();
        state.Errors.Clear();
        await SaveMusicJobAsync(state, ct);
        try
        {
            foreach (var brief in items)
            {
                try
                {
                    var (audio, extension, model) = await _lyria.GenerateAsync(brief.Prompt, ct);
                    var trackName = brief.Name;
                    var names = await _db.StudioMusicTracks.AsNoTracking().Select(m => m.Name).ToListAsync(ct);
                    for (var n = 2; names.Contains(trackName); n++) trackName = $"{brief.Name} ({n})";
                    var mime = extension switch { ".wav" => "audio/wav", ".m4a" => "audio/mp4", ".ogg" => "audio/ogg", _ => "audio/mpeg" };
                    await AddMusicAsync(trackName, brief.Mood, audio, mime, extension, ct);
                    _db.StudioCostRecords.Add(new StudioCostRecord
                    {
                        Kind = "Music", Model = model, CostUsd = _lyria.PricePerSong, Note = $"sáng tác nhạc nền \"{trackName}\""
                    });
                    await _db.SaveChangesAsync(ct);
                    state.Added.Add(trackName);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "AI music {Brief} failed", brief.Id);
                    state.Errors.Add($"{brief.Name}: {ex.Message}");
                }
                state.Done++;
                await SaveMusicJobAsync(state, CancellationToken.None);
            }
        }
        finally
        {
            state.Running = false;
            await SaveMusicJobAsync(state, CancellationToken.None);
        }
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

    public async Task<Guid?> ShotSfxBlobIdAsync(Guid shotId, CancellationToken ct) =>
        await _db.StudioShots.Where(s => s.Id == shotId).Select(s => s.SfxBlobId).FirstOrDefaultAsync(ct);

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
        dto.SfxEnabled = v.SfxEnabled;
        dto.SfxVolume = v.SfxVolume;
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
        dto.InstagramPermalink = v.InstagramPermalink;
        dto.InstagramError = v.InstagramError;
        var voiceFrom = new Dictionary<Guid, int>();
        foreach (var take in SceneTakes(v.Shots))
        {
            var owner = TakeOwner(take);
            if (owner.VoiceBlobId == null) continue;
            foreach (var s in take.Where(s => s != owner && s.Dialogue != null)) voiceFrom[s.Id] = owner.Index;
        }
        dto.Shots = v.Shots.OrderBy(s => s.Index).Select(s => new StudioShotDto
        {
            Id = s.Id, Index = s.Index, DurationSeconds = s.DurationSeconds, Action = s.Action, Camera = s.Camera,
            Location = s.Location, Cast = Names(s.CastJson), Speaker = s.Speaker, Dialogue = s.Dialogue, Sound = s.Sound,
            HasVoice = s.VoiceBlobId != null, VoiceSeconds = s.VoiceSeconds, HasSfx = s.SfxBlobId != null, Status = s.Status,
            Scene = s.Scene, VoiceStyle = s.VoiceStyle, VoiceFromShot = voiceFrom.TryGetValue(s.Id, out var from) ? from : null,
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
        foreach (var s in v.Shots.Where(s => s.VoiceBlobId != null &&
                     (VoiceFor(bibleBefore, s.Speaker) != VoiceFor(bibleAfter, s.Speaker) || SpeedFor(bibleBefore, s.Speaker) != SpeedFor(bibleAfter, s.Speaker))))
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
            if (s.SfxBlobId != null) await DeleteBlobAsync(s.SfxBlobId.Value, ct);
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
        if (i.SfxEnabled != null) v.SfxEnabled = i.SfxEnabled.Value;
        if (i.SfxVolume != null) v.SfxVolume = Math.Clamp(i.SfxVolume.Value, 0, 1.5);
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
                if (old.SfxBlobId != null) await DeleteBlobAsync(old.SfxBlobId.Value, ct);
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
    private Task<(FilmGeminiClient.TextResult Result, string Model)> PlanWithFallbackAsync(StudioVideo v, string system, string user, CancellationToken ct) =>
        AskWithFallbackAsync(v, system, user, text => ParsePlan(text, new HashSet<Guid>()), "Gemini đang đọc kịch bản, dựng hồ sơ nhân vật và chia shot", ct);

    /// <summary>Asks the plan models in turn (light thinking, time limit each); <paramref name="validate"/> must accept the answer.</summary>
    private async Task<(FilmGeminiClient.TextResult Result, string Model)> AskWithFallbackAsync(StudioVideo v, string system, string user,
        Action<string> validate, string doing, CancellationToken ct)
    {
        var errors = new List<string>();
        var models = PlanModels();
        for (var i = 0; i < models.Count; i++)
        {
            var model = models[i];
            v.Progress = i == 0
                ? $"{doing} ({model})…"
                : $"{models[i - 1]} không trả lời được — đang thử {model} ({i + 1}/{models.Count})…";
            await _db.SaveChangesAsync(CancellationToken.None);
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(PlanAttemptTimeout);
            try
            {
                var result = await _gemini.GenerateJsonAsync(system, user, 32768, 0.6, attempt.Token, model, lightThinking: true);
                validate(result.Text); // must at least be usable
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
        throw new InvalidOperationException("Không model Gemini nào trả lời được — " + string.Join(" | ", errors));
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
- ""action"", ""camera"" viết bằng TIẾNG ANH, cụ thể, dễ quay: ai làm gì, cảm xúc, cử chỉ có chủ đích; cỡ cảnh + chuyển động máy.
- ""sound"": hiệu ứng âm thanh của shot bằng TIẾNG ANH (AI sẽ tạo tiếng khớp hình): tiếng động và không khí cụ thể — gió, mưa, chim,
  côn trùng, bước chân, đập bàn, cửa mở, tiếng cười / xì xào đám đông, chợ, sông nước… KHÔNG ghi nhạc, KHÔNG ghi lời nói.
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
        var staleScenes = new HashSet<int>(); // scenes whose one-take narration no longer matches its words
        var index = 1;
        foreach (var input in shots)
        {
            var castJson = JsonSerializer.Serialize(input.Cast.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct());
            StudioShot shot;
            if (input.Id is { } sid && existing.TryGetValue(sid, out var found))
            {
                shot = found;
                var lineChanged = shot.Dialogue != NullIfEmpty(input.Dialogue) || shot.Speaker != NullIfEmpty(input.Speaker)
                                  || shot.VoiceStyle != NullIfEmpty(input.VoiceStyle) || shot.Scene != input.Scene;
                if (lineChanged)
                {
                    if (shot.Scene is { } before) staleScenes.Add(before);
                    if (input.Scene is { } after) staleScenes.Add(after);
                }
                var changed = lineChanged || shot.DurationSeconds != SeedanceVideoClient.ClampSeconds(input.DurationSeconds) || shot.Action != input.Action.Trim()
                              || shot.Camera != input.Camera.Trim() || shot.Sound != NullIfEmpty(input.Sound) || shot.CastJson != castJson
                              || shot.Location != NullIfEmpty(input.Location);
                if (changed && shot.Status == "Done") shot.Status = "Pending"; // edited → the clip no longer matches; regenerate
                if (changed) shot.InteractionId = null; // a pending job made from the old text must not be picked up
                if (changed && shot.SfxBlobId != null)
                {
                    await DeleteBlobAsync(shot.SfxBlobId.Value, ct);
                    shot.SfxBlobId = null;
                }
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
                if (input.Scene is { } scene && NullIfEmpty(input.Dialogue) != null) staleScenes.Add(scene);
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
            shot.Scene = input.Scene;
            shot.VoiceStyle = NullIfEmpty(input.VoiceStyle) is { } vs && vs.Length > 600 ? vs[..600] : NullIfEmpty(input.VoiceStyle);
        }
        foreach (var gone in v.Shots.Where(s => !keep.Contains(s.Id)).ToList())
        {
            if (gone.Scene is { } goneScene && gone.Dialogue != null) staleScenes.Add(goneScene);
            if (gone.ClipBlobId != null) await DeleteBlobAsync(gone.ClipBlobId.Value, ct);
            if (gone.VoiceBlobId != null) await DeleteBlobAsync(gone.VoiceBlobId.Value, ct);
            if (gone.SfxBlobId != null) await DeleteBlobAsync(gone.SfxBlobId.Value, ct);
            if (gone.KeyframeBlobId != null) await DeleteBlobAsync(gone.KeyframeBlobId.Value, ct);
            _db.StudioShots.Remove(gone);
            v.Shots.Remove(gone);
        }
        foreach (var s in v.Shots.Where(s => s.Scene is { } sc && staleScenes.Contains(sc) && s.VoiceBlobId != null))
        {
            await DeleteBlobAsync(s.VoiceBlobId!.Value, ct);
            s.VoiceBlobId = null;
            s.VoiceSeconds = null;
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
        + (s.Dialogue != null && s.VoiceBlobId == null ? Math.Round(SecondsToSay(s.Dialogue) * _voices.PricePerSecond, 4) : 0m)
        + (v.SfxEnabled && s.SfxBlobId == null ? _sfx.EstimateCost(s.DurationSeconds) : 0m);

    // ═══════════════════════════════ Import from the script library ═══════════════════════════════

    /// <summary>
    /// Creates a video from a finished library script, shot for shot (see <see cref="StudioScriptImporter"/>): no AI
    /// planning. A short background step then translates the sound notes for the effects model, writes the caption and
    /// picks the music; with <see cref="StudioScriptImportDto.AutoGenerate"/> the clips start right after it.
    /// </summary>
    public async Task<StudioVideoDto> ImportScriptAsync(StudioScriptImportDto input, CancellationToken ct)
    {
        if (input.Scenes.Count == 0) throw new ArgumentException("Kịch bản không có phân cảnh nào.");
        var planned = StudioScriptImporter.BuildShots(input);
        if (planned.Count == 0) throw new ArgumentException("Kịch bản không có phân cảnh nào dựng được (thiếu mô tả hình ảnh).");

        var narrator = StudioScriptImporter.NarratorFor(input.Mood);
        var designedVoices = await ReadDesignedVoicesAsync(ct);
        var chosenVoice = StudioVoices.IsKnown(input.NarratorVoice) ? input.NarratorVoice!
            : await ReadSettingAsync(DefaultNarratorKey, ct) is { Length: > 0 } preferred && designedVoices.Any(d => d.Id == preferred) ? preferred
            : narrator.Voice;
        var chosenGender = designedVoices.FirstOrDefault(d => d.Id == chosenVoice)?.Gender
                           ?? StudioVoices.All.FirstOrDefault(x => x.Id == chosenVoice)?.Gender ?? narrator.Gender;
        var v = new StudioVideo
        {
            Title = string.IsNullOrWhiteSpace(input.Title) ? "Video " + DateTime.UtcNow.AddHours(7).ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)
                : (input.Code is { Length: > 0 } code ? $"{input.Title.Trim()} ({code})" : input.Title.Trim()),
            Script = (input.Script ?? "").Trim(),
            VideoModel = SeedanceVideoClient.NormalizeModel(input.VideoModel ?? _video.DefaultModel),
            Resolution = SeedanceVideoClient.NormalizeResolution(input.Resolution ?? _configuration["Studio:DefaultResolution"]),
            AspectRatio = input.AspectRatio == "16:9" ? "16:9" : "9:16",
            StyleNotes = string.IsNullOrWhiteSpace(input.StyleNotes) ? StudioScriptImporter.StyleFor(input) : input.StyleNotes.Trim(),
            FacebookAccountId = input.FacebookAccountId is { } page && page != Guid.Empty ? page : null,
            MusicMode = input.MusicMode is "Manual" or "None" ? input.MusicMode : "Auto",
            MusicTrackId = input.MusicTrackId is { } mt && mt != Guid.Empty ? mt : null,
            MusicVolume = Math.Clamp(input.MusicVolume ?? 0.3, 0, 1),
            SfxEnabled = input.SfxEnabled ?? true,
            SfxVolume = Math.Clamp(input.SfxVolume ?? 0.7, 0, 1.5),
            Caption = input.Hook?.Trim(),
            PlanNotes = $"Dựng đúng theo kịch bản thư viện: {input.Scenes.Count} phân cảnh → {planned.Count} shot. Tâm trạng: {input.Mood}.",
            Seed = NewSeed(),
            BibleJson = WriteBible(NormalizeBible(new[]
            {
                new StudioCharacterDto
                {
                    Name = StudioVoices.NarratorName, Gender = chosenGender,
                    Voice = chosenVoice,
                    Speed = string.IsNullOrWhiteSpace(input.NarratorSpeed) ? narrator.Speed : input.NarratorSpeed,
                    Look = "Off-screen narrator — not visible in the picture."
                }
            }, null))
        };
        _db.StudioVideos.Add(v);
        var index = 1;
        foreach (var s in planned)
        {
            _db.StudioShots.Add(new StudioShot
            {
                StudioVideoId = v.Id, Index = index++, DurationSeconds = s.Duration, Action = s.Action, Camera = s.Camera,
                Speaker = s.Dialogue == null ? null : StudioVoices.NarratorName, Dialogue = s.Dialogue, Sound = s.Sound,
                Location = input.Scenes[s.Scene].Title is { Length: > 0 } t ? (t.Length > 200 ? t[..200] : t) : null,
                Scene = s.Scene + 1, // the scene's narration is read in one take across these shots
                VoiceStyle = StudioScriptImporter.NarratorStyleFor(input.Mood)
            });
        }
        v.TargetDurationSeconds = planned.Sum(s => s.Duration);
        await _db.SaveChangesAsync(ct);
        v = await _db.StudioVideos.Include(x => x.Shots).FirstAsync(x => x.Id == v.Id, ct);
        v.EstimatedCostUsd = EstimateVideoCost(v);

        if (input.AutoGenerate)
        {
            if (!_video.IsConfigured)
                throw new InvalidOperationException("Chưa có khoá Vercel AI Gateway (biến AI_GATEWAY_API_KEY trên Railway) — video đã được tạo, nhưng chưa tạo clip được.");
            await EnsureBudgetAsync(v.EstimatedCostUsd, ct);
        }
        var target = input.TargetSeconds is > 0 ? Math.Clamp(input.TargetSeconds.Value, 30, 600) : (int?)null;
        v.Status = "Planning";
        v.Progress = (target != null ? $"Đang rút gọn lời bình còn ~{target}s, " : "Đang ")
                     + "dựng hồ sơ nhân vật, bối cảnh và mô tả chi tiết từng shot" + (input.AutoGenerate ? ", rồi tạo video…" : "…");
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<StudioService>(x => x.RunPrepareImportAsync(v.Id, input.AutoGenerate, target, CancellationToken.None));
        return await GetVideoAsync(v.Id, ct);
    }

    [Queue("critical")]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunPrepareImportAsync(Guid id, bool autoGenerate, int? targetSeconds, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (v == null || v.Status != "Planning") return;
        var music = await _db.StudioMusicTracks.AsNoTracking().OrderBy(m => m.UseCount).ToListAsync(ct);
        string? condenseError = null;
        if (targetSeconds is { } target)
        {
            try
            {
                await CondenseAsync(v, target, ct);
                v = await _db.StudioVideos.Include(x => x.Shots).FirstAsync(x => x.Id == id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Studio condense failed for {Id}", id);
                condenseError = "Chưa rút gọn được lời bình (" + ex.Message + ") — video giữ độ dài của kịch bản. ";
            }
        }
        try
        {
            var (system, user) = BuildDirectionPrompt(v, music);
            var (result, model) = await AskWithFallbackAsync(v, system, user, text => ParseDirection(text, v.Shots.Count, new HashSet<Guid>()),
                "Gemini đang dựng hồ sơ nhân vật, bối cảnh và mô tả chi tiết từng shot", ct);
            var direction = ParseDirection(result.Text, v.Shots.Count, music.Select(m => m.Id).ToHashSet());
            ApplyDirection(v, direction);
            v.TargetDurationSeconds = Math.Max(1, v.Shots.Sum(s => s.DurationSeconds));
            if (!string.IsNullOrWhiteSpace(direction.Caption)) v.Caption = direction.Caption;
            if (v.MusicMode == "Auto" && v.MusicTrackId == null) v.MusicTrackId = direction.MusicTrackId ?? music.FirstOrDefault()?.Id;
            _db.StudioCostRecords.Add(new StudioCostRecord
            {
                StudioVideoId = v.Id, Kind = "Plan", Model = model, Tokens = result.Tokens, CostUsd = result.CostUsd,
                Note = "dựng hồ sơ & mô tả shot từ kịch bản thư viện" + (result.FreeKey ? " (khoá miễn phí)" : "")
            });
            v.ActualCostUsd += result.CostUsd;
            v.ErrorMessage = condenseError;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // the script itself is complete: carry on with its own prompts and the hook as caption
            _logger.LogWarning(ex, "Studio import direction failed for {Id}", id);
            if (v.MusicMode == "Auto" && v.MusicTrackId == null) v.MusicTrackId = music.FirstOrDefault()?.Id;
            v.ErrorMessage = condenseError + "Chưa dựng được hồ sơ nhân vật / mô tả chi tiết (" + ex.Message + ") — video vẫn dựng theo prompt gốc của kịch bản; " +
                             "nên bấm tạo lại video từ trang kịch bản để nhân vật đồng nhất hơn.";
        }
        v.EstimatedCostUsd = EstimateVideoCost(v);
        if (autoGenerate && _video.IsConfigured)
        {
            foreach (var s in v.Shots.Where(s => s.ClipBlobId == null)) { s.Status = "Pending"; s.ErrorMessage = null; }
            v.Status = "Generating";
            v.Progress = $"Chờ tạo {v.Shots.Count} clip (~{v.EstimatedCostUsd:0.00} USD)…";
            await _db.SaveChangesAsync(CancellationToken.None);
            _jobs.Enqueue<StudioService>(x => x.RunGenerationAsync(id, null, CancellationToken.None));
            return;
        }
        v.Status = "Planned";
        v.Progress = null;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    // ── shortening a library script ──

    /// <summary>Slow, emotional Vietnamese narration: about 2.5 words (syllables) a second, pauses included.</summary>
    public const double NarrationWordsPerSecond = 2.5;

    /// <summary>Words of narration that fit <paramref name="targetSeconds"/> of video (a breath per scene left aside).</summary>
    public static int WordBudget(int targetSeconds, int scenes) =>
        (int)Math.Round(Math.Max(20, targetSeconds - scenes * 1.0) * NarrationWordsPerSecond);

    /// <summary>
    /// Shortens an imported (not yet generated) video to about <paramref name="targetSeconds"/>: re-cut the narration
    /// and the direction again. Only before any clip is made — clips already paid for are never thrown away.
    /// </summary>
    public async Task<StudioVideoDto> StartCondenseAsync(Guid id, int targetSeconds, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        EnsureNotBusy(v);
        if (v.Shots.Count == 0 || v.Shots.Any(s => s.Scene == null))
            throw new InvalidOperationException("Chỉ rút gọn được video dựng từ trang kịch bản (các shot theo phân cảnh).");
        if (v.Shots.Any(s => s.ClipBlobId != null || !string.IsNullOrEmpty(s.InteractionId)))
            throw new InvalidOperationException("Video đã có clip (đã trả tiền) nên không rút gọn được — hãy dựng video mới từ trang kịch bản và chọn độ dài ở đó.");
        var target = Math.Clamp(targetSeconds, 30, 600);
        v.Status = "Planning";
        v.Progress = $"Đang rút gọn lời bình còn ~{target}s rồi dựng lại hồ sơ và mô tả shot…";
        v.ErrorMessage = null;
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<StudioService>(x => x.RunPrepareImportAsync(id, false, target, CancellationToken.None));
        return await GetVideoAsync(id, ct);
    }

    /// <summary>
    /// Gemini cuts the narration of each scene down to the word budget (keeping the hook, the ending and the most
    /// moving concrete images, the script's own wording, possibly dropping a minor scene), then the shots are built
    /// again from the shorter narration exactly as on import.
    /// </summary>
    private async Task CondenseAsync(StudioVideo v, int targetSeconds, CancellationToken ct)
    {
        var scenes = v.Shots.Where(s => s.Scene != null).OrderBy(s => s.Index).GroupBy(s => s.Scene!.Value).Select(g => g.ToList()).ToList();
        if (scenes.Count == 0 || v.Shots.Any(s => s.Scene == null)) return;
        string Narration(List<StudioShot> g) => string.Join(" ", g.Where(s => s.Dialogue != null).Select(s => s.Dialogue!.Trim()));
        var budget = WordBudget(targetSeconds, scenes.Count);
        if (scenes.Sum(g => WordCount(Narration(g))) <= budget) return; // already short enough

        var (system, user) = BuildCondensePrompt(v, scenes.Select(g => (g[0].Scene!.Value, g[0].Location, Narration(g))).ToList(), budget, targetSeconds);
        var numbers = scenes.Select(g => g[0].Scene!.Value).ToList();
        var (result, model) = await AskWithFallbackAsync(v, system, user, text => ParseCondense(text, numbers, budget),
            $"Gemini đang rút gọn lời bình còn ~{targetSeconds}s", ct);
        var shortened = ParseCondense(result.Text, numbers, budget);

        var kept = scenes.Where(g => shortened.TryGetValue(g[0].Scene!.Value, out var t) && t.Length > 0).ToList();
        var dto = new StudioScriptImportDto
        {
            Scenes = kept.Select(g => new StudioSceneImportDto
            {
                Title = g[0].Location, PromptEn = g[0].Action, Camera = g[0].Camera, Sound = g[0].Sound,
                Voiceover = shortened[g[0].Scene!.Value],
                DurationSeconds = (int)Math.Ceiling(WordCount(shortened[g[0].Scene!.Value]) / NarrationWordsPerSecond)
            }).ToList()
        };
        var built = StudioScriptImporter.BuildShots(dto);
        if (built.Count == 0) throw new InvalidOperationException("Lời bình rút gọn không dựng được shot nào.");

        foreach (var old in v.Shots)
        {
            if (old.VoiceBlobId != null) await DeleteBlobAsync(old.VoiceBlobId.Value, ct);
            if (old.SfxBlobId != null) await DeleteBlobAsync(old.SfxBlobId.Value, ct);
        }
        _db.StudioShots.RemoveRange(v.Shots);
        v.Shots.Clear();
        var index = 1;
        foreach (var s in built)
        {
            var source = kept[s.Scene];
            _db.StudioShots.Add(new StudioShot // through the DbSet: see RunPlanAsync
            {
                StudioVideoId = v.Id, Index = index++, DurationSeconds = s.Duration, Action = s.Action, Camera = s.Camera,
                Speaker = s.Dialogue == null ? null : StudioVoices.NarratorName, Dialogue = s.Dialogue, Sound = s.Sound,
                Location = source[0].Location, Scene = source[0].Scene,
                VoiceStyle = source.Select(x => x.VoiceStyle).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            });
        }
        v.TargetDurationSeconds = Math.Max(1, built.Sum(s => s.Duration));
        v.PlanNotes = $"Dựng theo kịch bản thư viện, rút gọn còn ~{targetSeconds}s: {kept.Count} phân cảnh → {built.Count} shot." +
                      (v.PlanNotes is { } notes && notes.Contains("Tâm trạng") ? " " + notes[notes.IndexOf("Tâm trạng", StringComparison.Ordinal)..] : "");
        _db.StudioCostRecords.Add(new StudioCostRecord
        {
            StudioVideoId = v.Id, Kind = "Plan", Model = model, Tokens = result.Tokens, CostUsd = result.CostUsd,
            Note = $"rút gọn lời bình còn ~{targetSeconds}s" + (result.FreeKey ? " (khoá miễn phí)" : "")
        });
        v.ActualCostUsd += result.CostUsd;
        await _db.SaveChangesAsync(ct);
    }

    public static (string System, string User) BuildCondensePrompt(StudioVideo v, IReadOnlyList<(int Scene, string? Title, string Narration)> scenes,
        int budget, int targetSeconds)
    {
        var system = "Bạn là biên tập viên lời bình cho video ngắn hoài niệm trên Facebook. Chỉ trả về JSON hợp lệ.";
        var sb = new StringBuilder();
        sb.AppendLine($"VIDEO: {v.Title}");
        if (!string.IsNullOrWhiteSpace(v.PlanNotes)) sb.AppendLine($"Ghi chú: {v.PlanNotes}");
        sb.AppendLine($"Lời bình hiện có {scenes.Sum(x => WordCount(x.Narration))} chữ — cần rút còn TỐI ĐA {budget} chữ để video dài khoảng {targetSeconds} giây.\n");
        foreach (var (scene, title, narration) in scenes)
            sb.AppendLine($"CẢNH {scene} — {title} ({WordCount(narration)} chữ):\n{narration}\n");
        sb.AppendLine($@"CÁCH RÚT GỌN:
- Tổng lời bình các cảnh ≤ {budget} chữ (đếm từng tiếng).
- Giữ câu mở đầu (hook) ở đầu cảnh đầu tiên và câu kết ở cuối cảnh cuối — được rút ngắn nhẹ nếu quá dài.
- CẮT câu / ý trùng lặp, liệt kê dài, câu chuyển ý; GIỮ những hình ảnh cụ thể và cảm động nhất, mạch truyện và cảm xúc cao trào.
- Giữ lời văn gốc của kịch bản, chỉ sửa tối thiểu cho câu liền mạch; KHÔNG thêm ý mới.
- Được bỏ hẳn cảnh kém quan trọng nhất (narration là """") nếu cần, nhưng giữ ít nhất 3 cảnh.

Trả về JSON: {{ ""scenes"": [ {{ ""scene"": 1, ""narration"": ""..."" }} ] }}");
        return (system, sb.ToString());
    }

    /// <summary>The shortened narration per scene ("" = scene dropped); it must fit the budget and keep the story.</summary>
    public static Dictionary<int, string> ParseCondense(string json, IReadOnlyList<int> scenes, int budget)
    {
        var text = json.Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidOperationException("Gemini không trả về JSON.");
        using var doc = JsonDocument.Parse(text[start..(end + 1)]);
        var result = scenes.ToDictionary(s => s, _ => "");
        if (doc.RootElement.TryGetProperty("scenes", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var x in arr.EnumerateArray())
            {
                var number = x.TryGetProperty("scene", out var n) && n.TryGetInt32(out var i) ? i : -1;
                if (result.ContainsKey(number) && x.TryGetProperty("narration", out var t) && t.ValueKind == JsonValueKind.String)
                    result[number] = System.Text.RegularExpressions.Regex.Replace(t.GetString() ?? "", @"\s+", " ").Trim();
            }
        var words = result.Values.Sum(WordCount);
        var kept = result.Values.Count(t => t.Length > 0);
        if (kept < Math.Min(2, scenes.Count)) throw new InvalidOperationException($"Gemini chỉ giữ {kept} cảnh.");
        if (words > budget * 1.2) throw new InvalidOperationException($"Lời bình rút gọn vẫn dài {words} chữ (cần ≤ {budget}).");
        if (words < budget * 0.35) throw new InvalidOperationException($"Lời bình rút gọn quá ngắn ({words} chữ).");
        return result;
    }

    /// <summary>
    /// The "director" step for a library script. Seedance has no reference images, so the only way to keep the same
    /// people and places from clip to clip is to describe them identically, in detail, in every clip: Gemini reads the
    /// whole script once and writes a fixed identity for each person (face, hair, build — no clothes), a fixed look
    /// for each place, each scene's time / weather / light and costumes (the same in every shot of the scene; costumes
    /// change only when the story moves on in time or place), and for every shot what the camera sees while its piece
    /// of narration is heard. Plus the narrator's acting direction per scene, the sound effects, caption and music.
    /// </summary>
    public static (string System, string User) BuildDirectionPrompt(StudioVideo v, IReadOnlyList<StudioMusicTrack> music)
    {
        var system =
            "Bạn là đạo diễn kiêm biên kịch hình ảnh cho phim ngắn NGƯỜI THẬT, chân thực như phim tài liệu, tạo bằng model TEXT-TO-VIDEO " +
            "Seedance (mỗi shot là 1 clip 2–12 giây, KHÔNG có ảnh tham chiếu, KHÔNG có tiếng — lời bình đọc bằng giọng AI và nhạc thêm sau). " +
            "Model chỉ nhìn thấy chữ bạn viết cho từng shot, nên muốn cùng một người / một nơi trông giống nhau ở mọi clip thì phải tả họ " +
            "Y HỆT nhau, thật chi tiết. Chỉ trả về JSON hợp lệ.";
        var sb = new StringBuilder();
        sb.AppendLine($"VIDEO: {v.Title}");
        if (!string.IsNullOrWhiteSpace(v.PlanNotes)) sb.AppendLine($"Ghi chú: {v.PlanNotes}");
        if (!string.IsNullOrWhiteSpace(v.StyleNotes)) sb.AppendLine($"Phong cách hình ảnh: {v.StyleNotes}");
        sb.AppendLine($"Khung hình: {(v.AspectRatio == "16:9" ? "ngang 16:9" : "dọc 9:16")}");
        if (!string.IsNullOrWhiteSpace(v.Script))
        {
            var script = v.Script!.Length > 14000 ? v.Script[..14000] + "…" : v.Script;
            sb.AppendLine($"\nKỊCH BẢN ĐẦY ĐỦ:\n{script}");
        }
        sb.AppendLine("\nDANH SÁCH SHOT (đã chia sẵn — giữ nguyên số shot và lời bình):");
        foreach (var scene in v.Shots.OrderBy(x => x.Index).GroupBy(x => x.Scene ?? 0))
        {
            var first = scene.First();
            sb.AppendLine($"\nCẢNH {scene.Key}: {first.Location}");
            sb.AppendLine($"  Mô tả hình gốc (EN): {first.Action}");
            if (!string.IsNullOrWhiteSpace(first.Camera)) sb.AppendLine($"  Góc máy gốc: {first.Camera}");
            if (!string.IsNullOrWhiteSpace(first.Sound)) sb.AppendLine($"  Âm thanh gốc: {first.Sound}");
            foreach (var shot in scene)
                sb.AppendLine($"  - shot {shot.Index} ({shot.DurationSeconds}s): " +
                              (shot.Dialogue != null ? $"lời bình \"{shot.Dialogue}\"" : "(không lời — nhịp hình lặng cuối cảnh)"));
        }
        sb.AppendLine("\nKHO NHẠC NỀN (chọn 1 bài hợp tâm trạng video, hoặc null):");
        if (music.Count == 0) sb.AppendLine("- (trống)");
        foreach (var m in music) sb.AppendLine($"- id={m.Id} | {m.Name} | tâm trạng: {m.Mood}");
        sb.AppendLine(@"
VIỆC CẦN LÀM (mọi mô tả hình viết bằng TIẾNG ANH):
1. ""characters"": MỌI người cụ thể xuất hiện trong hình (mẹ, bố, bà, đứa trẻ…), kể cả không có thoại. ""name"" tiếng Việt ngắn
   (vd ""Mẹ"", ""Bé Tí""). ""look"": 45–70 từ, CHỈ nhận dạng cố định, KHÔNG tả quần áo: người Việt Nam, tuổi chính xác, chiều cao /
   dáng người, khuôn mặt (hình dáng mặt, mắt, lông mày, mũi, môi), làn da (rám nắng, nếp nhăn…), tóc (dài ngắn, kiểu, màu, cách búi),
   dấu hiệu riêng (nốt ruồi, sẹo…). Người lao động bình thường, chân thực — không phải người mẫu. Cùng một người ở 2 độ tuổi khác hẳn
   (lúc bé / lúc lớn) thì là 2 mục riêng (vd ""Tí (8 tuổi)"", ""Tí (35 tuổi)"") nhưng giữ cùng nét mặt đặc trưng để nhận ra.
   ""outfit"": 20–35 từ, trang phục MẶC SUỐT video (màu, chất vải, độ cũ), NÓI RÕ đồ đội đầu (""always wearing a faded olive pith
   helmet"" hoặc ""bare-headed"") và giày dép — app chép y nguyên vào mọi shot để mũ nón không lúc có lúc không.
1b. ""props"": đồ vật quan trọng xuất hiện ở ≥ 2 shot (xe đạp, thùng kem, nồi cơm…): ""name"" ngắn tiếng Việt, ""look"" 20–40 từ
   tiếng Anh CỐ ĐỊNH (chất liệu, màu, kích thước, bên trong chứa gì) — vd thùng kem: ""a white styrofoam box tied on the bicycle rear
   rack, lid on top; inside, rows of colourful ice pops on wooden sticks"".
2. ""locations"": mỗi nơi chốn 1 mục, ""name"" ngắn tiếng Việt, ""look"" 40–70 từ CỐ ĐỊNH: loại nơi, kiến trúc & vật liệu, bố cục,
   đồ vật chính kèm màu sắc, cây cối, xung quanh, đúng thời đại và vùng miền. Cảnh quay lại nơi cũ thì dùng lại đúng ""name"" đó.
3. ""scenes"": mỗi CẢNH ở trên 1 mục:
   - ""scene"": số cảnh; ""location"": tên một mục trong locations;
   - ""time"": giờ trong ngày, thời tiết, chất ánh sáng — giống nhau cho mọi shot của cảnh, nối tiếp hợp lý với cảnh trước;
   - ""wardrobe"": CHỈ khi trang phục KHÁC ""outfit"" (câu chuyện sang ngày khác / nơi khác / tuổi khác): object { tên: trang phục mới,
     nói rõ đồ đội đầu }; còn lại để {} — app tự dùng ""outfit"" y nguyên;
   - ""voiceStyle"": 1–2 câu tiếng Anh chỉ đạo người đọc lời bình cảnh này (cảm xúc, nhịp, độ nhấn) đúng tâm trạng cảnh, giọng Việt
     mộc mạc, chân chất như kể chuyện với hàng xóm, giọng vững — KHÔNG bi lụy, KHÔNG nghẹn khóc, không đọc kiểu phát thanh viên;
   - ""sound"": âm nền chung của cảnh bằng tiếng Anh (không khí: ve sầu, gió, mưa…; KHÔNG lời nói, KHÔNG nhạc);
   - ""shots"": MỖI shot của cảnh 1 mục { ""shot"": số shot, ""cast"": [tên trong characters NHÌN THẤY trong hình],
     ""action"": 35–60 từ — đúng khoảnh khắc người xem thấy khi nghe lời bình của shot đó: minh hoạ đúng điều lời bình nói (hoặc
     phản ứng cảm xúc của nhân vật), gọi nhân vật bằng đúng ""name"", vị trí trong khung hình, MỘT hành động liền mạch, chậm, tự
     nhiên đủ cho 4–12 giây, nét mặt, người phụ / quần chúng phía sau tả ngắn; ""props"": [tên đồ vật trong props thấy trong hình];
     ""camera"": cỡ cảnh + chuyển động máy chậm, mượt; ""sound"": tiếng động CỦA RIÊNG shot này bằng tiếng Anh, cụ thể nguồn phát,
     chất liệu, nhịp (vd ""a small brass hand bell rung by hand, bright 'ting ting ting' in short bursts, from a slowly passing bicycle"")
     — tiếng đặc trưng chỉ đặt ở shot nó xảy ra, kèm âm nền của cảnh; KHÔNG lời nói, KHÔNG nhạc }.
4. Bám đúng nội dung, thời đại, vùng miền của kịch bản; không thêm chi tiết hiện đại; không chữ / biển hiệu trong hình. Tránh thứ
   AI video làm kém: đám đông cận mặt, thao tác tay phức tạp cận cảnh, chuyển động nhanh. Shot đầu mỗi cảnh: toàn cảnh (wide) cho
   thấy rõ nơi chốn; các shot sau xen trung cảnh / toàn trung; cận cảnh tối đa 1/4 số shot.
5. Viết như người quay phim tài liệu tả cảnh THẬT: chữ cụ thể, vật lý, dễ quay. KHÔNG dùng từ bay bổng / kỳ ảo (magical, dreamy,
   glowing, shimmering, timeless, ethereal, golden dust…) — chúng làm hình giống tranh vẽ. Ánh sáng tả như thật (nắng trưa gắt,
   bóng râm dưới tán cây, trời âm u).
6. Chi tiết trong lời bình khó hiện bằng hình hoặc dễ gây hiểu lầm (vd ""thùng xốp bọc chăn bông"") thì chọn khoảnh khắc người xem
   hiểu ngay: nắp thùng mở, hơi lạnh bốc lên, hàng que kem đủ màu bên trong — đừng bắt AI diễn thao tác lạ (lôi chăn ra…).
7. Âm thanh đúng thời đại và vùng miền Việt Nam (vd xe kem ngày xưa: chuông đồng lắc tay leng keng hoặc còi bóp ""tin tin"";
   trưa hè: ve sầu; bếp: củi nổ lách tách) — theo đúng ""âm thanh gốc"" của kịch bản.
8. ""caption"": chú thích Facebook tiếng Việt 1–3 câu gợi cảm xúc + 3–5 hashtag. ""musicTrackId"": id bài nhạc hợp nhất hoặc null.

Trả về JSON:
{ ""caption"": ""..."", ""musicTrackId"": ""id hoặc null"",
  ""characters"": [ { ""name"": ""..."", ""gender"": ""female"", ""look"": ""..."", ""outfit"": ""..."" } ],
  ""props"": [ { ""name"": ""..."", ""look"": ""..."" } ],
  ""locations"": [ { ""name"": ""..."", ""look"": ""..."" } ],
  ""scenes"": [ { ""scene"": 1, ""location"": ""..."", ""time"": ""..."", ""wardrobe"": {}, ""voiceStyle"": ""..."", ""sound"": ""..."",
                 ""shots"": [ { ""shot"": 1, ""cast"": [""Mẹ""], ""props"": [""...""], ""action"": ""..."", ""camera"": ""..."", ""sound"": ""..."" } ] } ] }");
        return (system, sb.ToString());
    }

    public sealed record DirectedShot(int Index, List<string> Cast, string? Action, string? Camera, List<string>? Props = null, string? Sound = null);
    public sealed record DirectedScene(int Scene, string? Location, string? Time, Dictionary<string, string> Wardrobe, string? VoiceStyle, string? Sound,
        List<DirectedShot> Shots);
    public sealed record Direction(string? Caption, Guid? MusicTrackId, List<StudioCharacterDto> Characters, List<StudioLocationDto> Locations,
        List<DirectedScene> Scenes, List<StudioLocationDto>? Props = null);

    /// <summary>Reads the director's JSON; it must describe most of the shots to be worth using.</summary>
    public static Direction ParseDirection(string json, int shotCount, ISet<Guid> knownMusic)
    {
        var text = json.Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidOperationException("Gemini không trả về JSON.");
        using var doc = JsonDocument.Parse(text[start..(end + 1)]);
        var root = doc.RootElement;
        static string? S(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? NullIfNull(p.GetString()?.Trim()) : null;
        static int? I(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i) ? i
            : e.TryGetProperty(n, out var q) && q.ValueKind == JsonValueKind.String && int.TryParse(q.GetString(), out var j) ? j : null;

        var characters = new List<StudioCharacterDto>();
        if (root.TryGetProperty("characters", out var ca) && ca.ValueKind == JsonValueKind.Array)
            foreach (var c in ca.EnumerateArray())
                if (S(c, "name") is { } name && !IsNarrator(name))
                    characters.Add(new StudioCharacterDto { Name = name, Gender = S(c, "gender") ?? "male", Look = S(c, "look") ?? "", Outfit = S(c, "outfit") });
        var locations = new List<StudioLocationDto>();
        if (root.TryGetProperty("locations", out var la) && la.ValueKind == JsonValueKind.Array)
            foreach (var l in la.EnumerateArray())
                if (S(l, "name") is { } name) locations.Add(new StudioLocationDto { Name = name, Look = S(l, "look") ?? "" });
        var props = new List<StudioLocationDto>();
        if (root.TryGetProperty("props", out var pa) && pa.ValueKind == JsonValueKind.Array)
            foreach (var x in pa.EnumerateArray())
                if (S(x, "name") is { } name && S(x, "look") is { } look) props.Add(new StudioLocationDto { Name = name, Look = look });

        var scenes = new List<DirectedScene>();
        if (root.TryGetProperty("scenes", out var sa) && sa.ValueKind == JsonValueKind.Array)
            foreach (var sc in sa.EnumerateArray())
            {
                if (I(sc, "scene") is not { } number) continue;
                var wardrobe = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (sc.TryGetProperty("wardrobe", out var wa) && wa.ValueKind == JsonValueKind.Object)
                    foreach (var w in wa.EnumerateObject())
                        if (w.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(w.Value.GetString()))
                            wardrobe[w.Name.Trim()] = w.Value.GetString()!.Trim();
                var shots = new List<DirectedShot>();
                if (sc.TryGetProperty("shots", out var sh) && sh.ValueKind == JsonValueKind.Array)
                    foreach (var x in sh.EnumerateArray())
                    {
                        if (I(x, "shot") is not { } index) continue;
                        List<string> Names(string field)
                        {
                            var list = new List<string>();
                            if (x.TryGetProperty(field, out var arr) && arr.ValueKind == JsonValueKind.Array)
                                foreach (var n in arr.EnumerateArray())
                                    if (n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString())) list.Add(n.GetString()!.Trim());
                            return list;
                        }
                        shots.Add(new DirectedShot(index, Names("cast"), S(x, "action"), S(x, "camera"), Names("props"), S(x, "sound")));
                    }
                scenes.Add(new DirectedScene(number, S(sc, "location"), S(sc, "time"), wardrobe, S(sc, "voiceStyle"), S(sc, "sound"), shots));
            }
        var described = scenes.SelectMany(x => x.Shots).Where(x => x.Action != null).Select(x => x.Index).Distinct().Count();
        if (scenes.Count == 0 || described < Math.Ceiling(shotCount * 0.7))
            throw new InvalidOperationException($"Gemini chỉ mô tả {described}/{shotCount} shot.");
        Guid? musicId = Guid.TryParse(S(root, "musicTrackId"), out var mid) && knownMusic.Contains(mid) ? mid : null;
        return new Direction(S(root, "caption"), musicId, characters, locations, scenes, props);
    }

    /// <summary>
    /// Puts the direction on the shots: the bible (narrator kept), each shot's location, visible cast and action —
    /// with the scene's costumes for the people in frame and its time / light written the same way in every shot of
    /// the scene — its camera, the scene's sound effects and the narrator's direction.
    /// </summary>
    public static void ApplyDirection(StudioVideo v, Direction d)
    {
        var narrator = ReadBible(v.BibleJson).Characters.Where(c => IsNarrator(c.Name));
        var bible = NormalizeBible(narrator.Concat(d.Characters), d.Locations);
        v.BibleJson = WriteBible(bible);
        foreach (var scene in d.Scenes)
        {
            var location = bible.Location(scene.Location)?.Name;
            foreach (var shot in v.Shots.Where(x => x.Scene == scene.Scene))
            {
                var directed = scene.Shots.FirstOrDefault(x => x.Index == shot.Index);
                if (location != null) shot.Location = location;
                var cast = (directed?.Cast ?? new List<string>())
                    .Select(n => bible.Character(n)).Where(c => c != null && !IsNarrator(c.Name)).Select(c => c!.Name).Distinct().ToList();
                if (directed != null) shot.CastJson = JsonSerializer.Serialize(cast);
                var action = (directed?.Action ?? shot.Action).Trim().TrimEnd('.');
                var wardrobe = cast.Select(n => scene.Wardrobe.FirstOrDefault(w => SameName(w.Key, n)))
                    .Where(w => !string.IsNullOrWhiteSpace(w.Value)).Select(w => $"{w.Key} wears {w.Value.TrimEnd('.')}").ToList();
                var sb = new StringBuilder(action).Append('.');
                if (wardrobe.Count > 0) sb.Append(" Wardrobe: ").Append(string.Join("; ", wardrobe)).Append('.');
                // key objects described the same way in every shot they appear in
                var objects = (directed?.Props ?? new List<string>())
                    .Select(n => d.Props?.FirstOrDefault(p => SameName(p.Name, n))).Where(p => p != null).Distinct().ToList();
                if (objects.Count > 0) sb.Append(" Key objects: ").Append(string.Join("; ", objects.Select(p => $"{p!.Name} — {p.Look.TrimEnd('.')}"))).Append('.');
                if (!string.IsNullOrWhiteSpace(scene.Time)) sb.Append(" Time and light: ").Append(scene.Time!.TrimEnd('.')).Append('.');
                shot.Action = sb.ToString();
                if (directed?.Camera is { Length: > 0 } camera) shot.Camera = camera.Length > 500 ? camera[..500] : camera;
                var shotSound = directed?.Sound is { Length: > 0 } own
                    ? (scene.Sound is { Length: > 0 } bed && !own.Contains(bed, StringComparison.OrdinalIgnoreCase) ? $"{own.TrimEnd('.')}; background: {bed}" : own)
                    : scene.Sound;
                if (shotSound is { Length: > 0 } sound) shot.Sound = sound.Length > 500 ? sound[..500] : sound;
                if (scene.VoiceStyle is { Length: > 0 } style) shot.VoiceStyle = style.Length > 600 ? style[..600] : style;
            }
        }
    }

    // ═══════════════════════════════ Voices ═══════════════════════════════

    /// <summary>Reads the lines aloud (all missing ones, or one shot's again) so they can be heard before paying for clips.</summary>
    public async Task<StudioVideoDto> StartVoicesAsync(Guid id, Guid? shotId, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        EnsureNotBusy(v);
        var todo = VoiceUnits(v.Shots, shotId);
        if (todo.Count == 0)
            throw new InvalidOperationException(shotId != null ? "Shot này không có lời thoại." : "Mọi câu thoại đều đã có giọng đọc — bấm \"Đọc lại\" ở từng shot nếu muốn đổi.");
        v.Status = "Generating";
        v.Progress = $"Đang đọc {todo.Count} đoạn lời bằng giọng AI…";
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
        var todo = VoiceUnits(v.Shots, shotId);
        var errors = new List<string>();
        var n = 0;
        foreach (var unit in todo)
        {
            var take = InSceneTake(unit[0]);
            v.Progress = take
                ? $"Đang đọc lời bình {++n}/{todo.Count} (cảnh {unit[0].Scene}, liền một mạch)…"
                : $"Đang đọc câu thoại {++n}/{todo.Count} (shot {unit[0].Index})…";
            await _db.SaveChangesAsync(ct);
            try
            {
                if (take) await MakeSceneVoiceAsync(v, bible, unit, ct);
                else await MakeVoiceAsync(v, bible, unit[0], ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                foreach (var s in unit.Where(s => s.Dialogue != null)) s.ErrorMessage = "Giọng đọc: " + ex.Message;
                errors.Add(take ? $"cảnh {unit[0].Scene}" : $"shot {unit[0].Index}");
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
        var (audio, seconds, fromCache, _) = await _voices.SpeakAsync(v.Id, s.Dialogue, voice, SpeedFor(bible, s.Speaker), ct,
            fallbackVoice: StudioVoices.FallbackFor(GenderOf(bible, s.Speaker)));
        if (!FitsOneClip(seconds))
            throw new InvalidOperationException($"Câu thoại đọc mất {seconds:0.0}s — dài hơn 1 clip ({SeedanceVideoClient.MaxSeconds}s). Tách câu này thành 2 shot.");
        if (s.VoiceBlobId != null) await DeleteBlobAsync(s.VoiceBlobId.Value, ct);
        var blob = new StudioBlob { MimeType = "audio/mp4", Data = audio, SizeBytes = audio.Length };
        _db.StudioBlobs.Add(blob);
        s.VoiceBlobId = blob.Id;
        s.VoiceSeconds = seconds;
        s.DurationSeconds = ClipSecondsFor(s.DurationSeconds, seconds);
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

    // ═══════════════════════════════ Sound effects ═══════════════════════════════

    /// <summary>
    /// Sound effects for one clip (ElevenLabs via the Vercel AI Gateway) from the shot's sound notes — for a library
    /// script these are the script's own "âm thanh" lines, translated to English on import — exactly as long as the
    /// clip. A failure is shown on the shot but never fails the clip: the video is still made, without that shot's effects.
    /// </summary>
    private async Task TryMakeSfxAsync(StudioVideo v, StudioShot s, byte[] clip, CancellationToken ct)
    {
        try
        {
            var seconds = await _media.ProbeSecondsAsync(clip, ".mp4", ct);
            if (seconds <= 0) seconds = s.DurationSeconds;
            var result = await _sfx.GenerateAsync(SoundEffectsClient.BuildPrompt(s.Sound, s.Action), seconds, ct);
            var audio = await _media.ExtractAudioAsync(result, ct, ".mp3");
            if (s.SfxBlobId != null) await DeleteBlobAsync(s.SfxBlobId.Value, ct);
            var blob = new StudioBlob { MimeType = "audio/mp4", Data = audio, SizeBytes = audio.Length };
            _db.StudioBlobs.Add(blob);
            s.SfxBlobId = blob.Id;
            if (s.ErrorMessage?.StartsWith("Hiệu ứng âm thanh") == true) s.ErrorMessage = null;
            var cost = _sfx.EstimateCost(seconds);
            s.CostUsd += cost;
            v.ActualCostUsd += cost;
            _db.StudioCostRecords.Add(new StudioCostRecord
            {
                StudioVideoId = v.Id, StudioShotId = s.Id, Kind = "Sfx", Model = SoundEffectsClient.Model, Seconds = seconds, CostUsd = cost,
                Note = $"hiệu ứng âm thanh shot {s.Index}"
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Sound effects failed for shot {Index}", s.Index);
            s.ErrorMessage = "Hiệu ứng âm thanh: " + ex.Message;
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Adds sound effects to clips that have none (or redoes one shot's), then composes the video again.</summary>
    public async Task<StudioVideoDto> StartEffectsAsync(Guid id, Guid? shotId, CancellationToken ct)
    {
        if (!_sfx.IsConfigured) throw new InvalidOperationException("Chưa có khoá Vercel AI Gateway (AI_GATEWAY_API_KEY trên Railway) — chưa tạo được hiệu ứng âm thanh.");
        await ReconcileLostFilesAsync(id, ct);
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        EnsureNotBusy(v);
        var todo = v.Shots.Where(s => s.ClipBlobId != null && (shotId != null ? s.Id == shotId : s.SfxBlobId == null)).ToList();
        if (todo.Count == 0)
            throw new InvalidOperationException(shotId != null ? "Shot này chưa có clip — tạo clip trước." : "Mọi clip đều đã có hiệu ứng âm thanh (hoặc chưa có clip nào).");
        var cost = todo.Sum(s => _sfx.EstimateCost(s.DurationSeconds));
        await EnsureBudgetAsync(cost, ct);
        v.SfxEnabled = true;
        v.Status = "Generating";
        v.Progress = $"Đang tạo hiệu ứng âm thanh cho {todo.Count} clip (~{cost:0.000} USD)…";
        v.ErrorMessage = null;
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<StudioService>(s => s.RunEffectsAsync(id, shotId, CancellationToken.None));
        return await GetVideoAsync(id, ct);
    }

    [Queue("render")]
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(1800)]
    public async Task RunEffectsAsync(Guid id, Guid? shotId, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (v == null || v.Status != "Generating") return;
        var todo = v.Shots.OrderBy(s => s.Index).Where(s => s.ClipBlobId != null && (shotId != null ? s.Id == shotId : s.SfxBlobId == null)).ToList();
        var n = 0;
        foreach (var s in todo)
        {
            v.Progress = $"Đang tạo hiệu ứng âm thanh {++n}/{todo.Count} (shot {s.Index})…";
            await _db.SaveChangesAsync(ct);
            if (await GetBlobAsync(s.ClipBlobId!.Value, ct) is { } clip) await TryMakeSfxAsync(v, s, clip.Data, ct);
        }
        if (v.Shots.Count > 0 && v.Shots.All(s => s.ClipBlobId != null))
        {
            await ComposeInternalAsync(v, ct); // put the new effects into the final video
            return;
        }
        v.Status = "Planned";
        v.Progress = null;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>A shot whose words belong to its scene's single narration take (library scripts; not a character's own line).</summary>
    public static bool InSceneTake(StudioShot s) => s.Scene != null && (s.Dialogue == null || IsNarrator(s.Speaker));

    /// <summary>The shots of each scene read in one take (in order), for scenes that have narration.</summary>
    public static List<List<StudioShot>> SceneTakes(IEnumerable<StudioShot> shots) =>
        shots.Where(InSceneTake).OrderBy(s => s.Index).GroupBy(s => s.Scene!.Value)
            .Select(g => g.ToList()).Where(g => g.Any(s => s.Dialogue != null)).ToList();

    /// <summary>The shot holding a take's audio: the scene's first spoken shot (the take starts there).</summary>
    public static StudioShot TakeOwner(IReadOnlyList<StudioShot> take) => take.First(s => s.Dialogue != null);

    /// <summary>
    /// What has to be read: scene takes and single lines without a voice yet — or, for <paramref name="shotId"/>, that
    /// shot's line again (its whole scene take when it is part of one).
    /// </summary>
    public static List<List<StudioShot>> VoiceUnits(IEnumerable<StudioShot> all, Guid? shotId)
    {
        var shots = all.OrderBy(s => s.Index).ToList();
        var takes = SceneTakes(shots);
        if (shotId != null)
        {
            var shot = shots.FirstOrDefault(s => s.Id == shotId);
            if (shot == null) return new();
            if (takes.FirstOrDefault(t => t.Contains(shot)) is { } take) return new() { take };
            return shot.Dialogue != null ? new() { new() { shot } } : new();
        }
        var units = takes.Where(t => TakeOwner(t).VoiceBlobId == null).ToList();
        units.AddRange(shots.Where(s => s.Dialogue != null && !InSceneTake(s) && s.VoiceBlobId == null).Select(s => new List<StudioShot> { s }));
        return units.OrderBy(u => u[0].Index).ToList();
    }

    public static int WordCount(string? text) => string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    /// Clip lengths for a scene read in one take of <paramref name="seconds"/>, from its first spoken shot on. Each
    /// spoken shot lasts about as long as its own words take to say (Vietnamese words ≈ syllables, so their share of
    /// the take is a good clock), rounded on the running total so the picture never drifts more than half a second
    /// from the words; the last spoken shot holds the end of the take plus a breath. Shots that already have a clip
    /// keep their length. A take longer than the scene can ever be is refused.
    /// </summary>
    public static int[] FitSceneTake(IReadOnlyList<(int Words, int Duration, bool Fixed)> shots, double seconds)
    {
        const double lead = 0.25; // the voice starts a quarter second into the first clip
        var total = Math.Max(1, shots.Sum(x => x.Words));
        var lastSpoken = Enumerable.Range(0, shots.Count).Where(i => shots[i].Words > 0).DefaultIfEmpty(shots.Count - 1).Last();
        var d = new int[shots.Count];
        double spoken = lead;
        var placed = 0;
        for (var k = 0; k < shots.Count; k++)
        {
            var x = shots[k];
            if (x.Words > 0) spoken += seconds * x.Words / total;
            if (x.Fixed) d[k] = x.Duration;
            else if (x.Words > 0)
            {
                var end = k == lastSpoken ? (int)Math.Ceiling(lead + seconds + VoiceTail - 1e-6) : (int)Math.Round(spoken, MidpointRounding.AwayFromZero);
                d[k] = SeedanceVideoClient.ClampSeconds(end - placed);
            }
            else d[k] = SeedanceVideoClient.ClampSeconds(x.Duration);
            placed += d[k];
        }
        var needed = seconds + MinVoiceRoom; // MinVoiceRoom already holds the 0.25 s lead and a short breath
        for (var k = lastSpoken; k >= 0 && d.Sum() < needed - 1e-6; k--)
            if (!shots[k].Fixed) d[k] = Math.Min(SeedanceVideoClient.MaxSeconds, d[k] + (int)Math.Ceiling(needed - d.Sum() - 1e-6));
        if (d.Sum() < needed - 1e-6)
            throw new InvalidOperationException($"Lời bình của cảnh đọc mất {seconds:0.0}s — dài hơn cả cảnh có thể chứa ({d.Sum()}s). " +
                                                "Thêm 1 shot cho cảnh này hoặc rút gọn lời bình.");
        return d;
    }

    /// <summary>
    /// Reads a scene's narration in ONE take (its own acting direction, so it flows and keeps its emotion instead of
    /// restarting at every cut) and times the scene's clips to it. The audio sits on the first spoken shot and runs on
    /// over the following shots when the video is put together.
    /// </summary>
    private async Task<List<StudioShot>> MakeSceneVoiceAsync(StudioVideo v, Bible bible, List<StudioShot> take, CancellationToken ct)
    {
        var owner = TakeOwner(take);
        var span = take.Skip(take.IndexOf(owner)).ToList();
        var text = string.Join(" ", span.Where(s => s.Dialogue != null).Select(s => s.Dialogue!.Trim()));
        var voice = VoiceFor(bible, owner.Speaker);
        var style = !string.IsNullOrWhiteSpace(owner.VoiceStyle) ? owner.VoiceStyle : take.Select(s => s.VoiceStyle).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        var (audio, seconds, fromCache, delivery) = await _voices.SpeakAsync(v.Id, text, voice, SpeedFor(bible, owner.Speaker), ct, style,
            StudioVoices.FallbackFor(GenderOf(bible, owner.Speaker)));

        // read slower than planned and longer than the scene's clips can ever be: add a shot (the same moment, another
        // framing) instead of stopping the whole video
        var added = new List<StudioShot>();
        bool Fixed(StudioShot s) => s.ClipBlobId != null && s.Status == "Done";
        double Capacity() => span.Sum(s => Fixed(s) ? s.DurationSeconds : SeedanceVideoClient.MaxSeconds);
        while (seconds + MinVoiceRoom > Capacity() + 1e-6 && added.Count < 3)
        {
            var last = span[^1];
            foreach (var later in v.Shots.Where(s => s.Index > last.Index)) later.Index++;
            var extra = new StudioShot
            {
                StudioVideoId = v.Id, Index = last.Index + 1, DurationSeconds = 6, Action = last.Action, Location = last.Location,
                CastJson = last.CastJson, Sound = last.Sound, Scene = last.Scene, VoiceStyle = last.VoiceStyle, Status = "Pending",
                Camera = "slow gentle push-in on the same moment, a slightly different angle, people keep acting naturally"
            };
            _db.StudioShots.Add(extra); // through the DbSet: see RunPlanAsync
            span.Add(extra);
            take.Add(extra);
            added.Add(extra);
        }
        var durations = FitSceneTake(span.Select(s => (WordCount(s.Dialogue), s.DurationSeconds, Fixed(s))).ToList(), seconds);

        foreach (var s in take.Where(s => s.VoiceBlobId != null))
        {
            await DeleteBlobAsync(s.VoiceBlobId!.Value, ct);
            s.VoiceBlobId = null;
            s.VoiceSeconds = null;
        }
        var blob = new StudioBlob { MimeType = "audio/mp4", Data = audio, SizeBytes = audio.Length };
        _db.StudioBlobs.Add(blob);
        owner.VoiceBlobId = blob.Id;
        owner.VoiceSeconds = seconds;
        for (var k = 0; k < span.Count; k++) span[k].DurationSeconds = durations[k];
        foreach (var s in take.Where(s => s.ErrorMessage?.StartsWith("Giọng đọc") == true)) s.ErrorMessage = null;
        var cost = fromCache ? 0m : Math.Round((decimal)seconds * _voices.PricePerSecond, 5);
        owner.CostUsd += cost;
        v.ActualCostUsd += cost;
        _db.StudioCostRecords.Add(new StudioCostRecord
        {
            StudioVideoId = v.Id, StudioShotId = owner.Id, Kind = "Voice", Model = voice, Seconds = seconds, CostUsd = cost,
            Note = $"lời bình cảnh {owner.Scene} (shot {span[0].Index}–{span[^1].Index}, liền một mạch" + (delivery != null ? ", " + delivery : "") + ")"
                   + (fromCache ? " (dùng lại, miễn phí)" : "") + (added.Count > 0 ? $" — thêm {added.Count} shot cho vừa lời bình" : "")
        });
        await _db.SaveChangesAsync(ct);
        return added;
    }

    /// <summary>Whether a line read in <paramref name="voiceSeconds"/> fits one clip (with a little tolerance for rounding).</summary>
    public static bool FitsOneClip(double voiceSeconds) => voiceSeconds + MinVoiceRoom <= SeedanceVideoClient.MaxSeconds + 1e-6;

    /// <summary>Clip length for a line: at least the planned length, long enough for the line plus a breath, at most 12 s.</summary>
    public static int ClipSecondsFor(int planned, double voiceSeconds) =>
        SeedanceVideoClient.ClampSeconds(Math.Max(planned, (int)Math.Ceiling(voiceSeconds + VoiceTail - 1e-6)));

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

        // 0. each scene's narration in one take first: its length sets the lengths of the scene's clips
        var noVoice = new HashSet<Guid>();
        foreach (var take in SceneTakes(v.Shots))
        {
            if (TakeOwner(take).VoiceBlobId != null || !take.Any(todo.Contains)) continue;
            v.Progress = $"Đang đọc lời bình cảnh {take[0].Scene} (liền một mạch)…";
            await _db.SaveChangesAsync(ct);
            try
            {
                var extra = await MakeSceneVoiceAsync(v, bible, take, ct);
                if (extra.Count > 0) todo = todo.Concat(extra).OrderBy(s => s.Index).ToList(); // the new shots are made in this run too
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                foreach (var s in take.Where(todo.Contains))
                {
                    s.Status = "Failed";
                    s.ErrorMessage = "Giọng đọc: " + ex.Message;
                    noVoice.Add(s.Id); // no clip paid for a scene whose narration can't be voiced
                }
                failed.Add($"cảnh {take[0].Scene}: Giọng đọc: {ex.Message}");
                await _db.SaveChangesAsync(ct);
            }
        }

        foreach (var shot in todo)
        {
            if (noVoice.Contains(shot.Id)) continue;
            v.Progress = $"Đang tạo clip {++done}/{todo.Count} (shot {shot.Index})…";
            // a job already paid for and not turned into a clip yet (restart, timeout, failed download) is picked up
            // again instead of paying for a new one
            var resume = shot.ClipBlobId == null ? shot.InteractionId : null;
            shot.Status = "Generating";
            await _db.SaveChangesAsync(ct);

            // 1. the line first: its length decides the clip's length (scene takes were read above)
            if (shot.Dialogue != null && shot.VoiceBlobId == null && !InSceneTake(shot))
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
                    if (shot.SfxBlobId != null)
                    {
                        await DeleteBlobAsync(shot.SfxBlobId.Value, ct); // made from the old clip
                        shot.SfxBlobId = null;
                    }
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
                    if (v.SfxEnabled && _sfx.IsConfigured) await TryMakeSfxAsync(v, shot, clip.Video, ct);
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
        sb.Append("Real camera footage of real places and ordinary people: natural skin texture, true-to-life proportions and materials, natural daylight colours, " +
                  "authentic period details; not an illustration, painting, cartoon, anime or CGI render. ");
        var location = bible.Location(shot.Location);
        if (location != null) sb.Append("Setting: ").Append(location.Look.TrimEnd('.')).Append(". ");
        else if (!string.IsNullOrWhiteSpace(shot.Location)) sb.Append("Setting: ").Append(shot.Location.TrimEnd('.')).Append(". ");
        var cast = Names(shot.CastJson).Select(n => bible.Character(n)).Where(c => c != null && !c.Name.Equals(StudioVoices.NarratorName)).ToList();
        if (cast.Count > 0)
        {
            sb.Append($"Characters in frame ({cast.Count}, each appears exactly once): ");
            // the fixed outfit, unless this shot's action dresses the character differently (a scene on another day)
            sb.Append(string.Join("; ", cast.Select(c => $"{c!.Name} — {c.Look.TrimEnd('.')}"
                + (!string.IsNullOrWhiteSpace(c.Outfit) && !shot.Action.Contains($"{c.Name} wears", StringComparison.OrdinalIgnoreCase)
                    ? ", " + (System.Text.RegularExpressions.Regex.IsMatch(c.Outfit!, @"^\s*(always\s+)?wear", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "" : "wearing ")
                      + c.Outfit!.Trim().TrimEnd('.')
                    : "")))).Append(". ");
        }
        sb.Append("Action: ").Append(shot.Action.TrimEnd('.')).Append(". ");
        if (!string.IsNullOrWhiteSpace(shot.Camera)) sb.Append("Camera: ").Append(shot.Camera.TrimEnd('.')).Append(". ");
        if (shot.Dialogue != null && !IsNarrator(shot.Speaker))
            sb.Append($"{shot.Speaker} is talking with expressive face and hand gestures while the others react; keep the framing medium or wide, no extreme close-up of the mouth. ");
        else
            sb.Append("Nobody is talking to the camera. ");
        if (!string.IsNullOrWhiteSpace(v.StyleNotes)) sb.Append("Style: ").Append(v.StyleNotes!.TrimEnd('.')).Append(". ");
        sb.Append("Continuous natural motion for the whole clip (people keep acting, background alive, smooth camera); no frozen frames, no slow motion. ");
        sb.Append("Same people and places as the rest of this film: keep every face, hairstyle, body and costume exactly as described. ");
        sb.Append("Realistic hands. No text, no subtitles, no captions, no logos, no watermark.");
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
        var neverMade = new List<int>();
        foreach (var s in lost)
        {
            if (string.IsNullOrEmpty(s.InteractionId) && s.ClipBlobId == null)
            {
                neverMade.Add(s.Index); // nothing was ever paid for: it is simply still to make
                continue;
            }
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

        if (gone.Count == 0 && neverMade.Count > 0)
        {
            v.Status = "Planned";
            v.Progress = null;
            var todoCost = neverMade.Sum(i => ShotCost(v, shots.First(s => s.Index == i)));
            v.ErrorMessage = $"Đã lấy lại {recovered} clip miễn phí. Còn shot {string.Join(", ", neverMade)} chưa từng được tạo — " +
                             $"bấm \"Tạo các clip còn thiếu\" (~{todoCost:0.00} USD) là video tự ghép.";
            await _db.SaveChangesAsync(ct);
            return;
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
        var cost = gone.Concat(neverMade).Sum(i => ShotCost(v, shots.First(s => s.Index == i)));
        v.ErrorMessage = $"Lấy lại được {recovered}/{lost.Count - neverMade.Count} clip miễn phí. Shot {string.Join(", ", gone)} đã hết hạn" +
                         (neverMade.Count > 0 ? $", shot {string.Join(", ", neverMade)} chưa từng tạo" : "") +
                         $" — bấm \"Tạo các clip còn thiếu\" (~{cost:0.00} USD).";
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
        v.Progress = "Đang ghép video, giọng đọc, hiệu ứng âm thanh và nhạc nền…";
        await _db.SaveChangesAsync(ct);
        var dir = StudioMedia.TempDir();
        try
        {
            var clips = new List<string>();
            var voices = new List<string?>();
            var effects = new List<string?>();
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
                string? fxPath = null;
                if (v.SfxEnabled && s.SfxBlobId != null && await GetBlobAsync(s.SfxBlobId.Value, ct) is { } fb)
                {
                    fxPath = Path.Combine(dir, $"sfx_{s.Index:D3}.m4a");
                    await File.WriteAllBytesAsync(fxPath, fb.Data, ct);
                }
                effects.Add(fxPath);
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
            var duration = await _media.ComposeAsync(clips, musicPath, v.MusicVolume, v.AspectRatio, DeliveryResolution, output, ct, watermark, brand, voices, effects, v.SfxVolume);
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
            // the same Reel on the linked Instagram (Facebook is already live: a failure is only noted)
            await PostToInstagramAsync(v, path, ct);
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

    /// <summary>
    /// Posts the final video on the Instagram account linked to the video's Page; the result (link or reason) is kept on
    /// the video. Instagram fetches the file from the app's public link, or it is uploaded.
    /// </summary>
    private async Task PostToInstagramAsync(StudioVideo v, string path, CancellationToken ct)
    {
        try
        {
            v.Progress = "Facebook xong — đang đăng lên Instagram…";
            await _db.SaveChangesAsync(ct);
            var publicUrl = PublicBaseUrl() is { } baseUrl ? $"{baseUrl}/api/studio/videos/{v.Id}/final" : null;
            var (_, permalink) = await _facebook.PublishInstagramVideoFileAsync(v.FacebookAccountId!.Value, path, CaptionWithDisclosure(v.Caption), publicUrl, ct);
            v.InstagramPermalink = permalink ?? "posted";
            v.InstagramError = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Studio Instagram post failed for {Id}: {Message}", v.Id, ex.Message);
            v.InstagramError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
        }
        v.Progress = null;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private string? PublicBaseUrl()
    {
        var configured = _configuration["App:PublicBaseUrl"];
        var domain = Environment.GetEnvironmentVariable("RAILWAY_PUBLIC_DOMAIN");
        return !string.IsNullOrWhiteSpace(configured) ? configured.TrimEnd('/')
            : !string.IsNullOrWhiteSpace(domain) ? $"https://{domain.Trim().TrimEnd('/')}"
            : null;
    }

    /// <summary>Posts an already published video to Instagram too (e.g. after linking Instagram), while its file still exists.</summary>
    public async Task<StudioVideoDto> StartInstagramAsync(Guid id, CancellationToken ct)
    {
        var v = await _db.StudioVideos.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        if (v.Status != "Published" || v.FacebookAccountId == null) throw new InvalidOperationException("Đăng Facebook trước — Instagram đăng kèm theo.");
        if (!string.IsNullOrEmpty(v.InstagramPermalink)) throw new InvalidOperationException("Video này đã có trên Instagram.");
        if (v.FinalBlobId == null || await GetBlobAsync(v.FinalBlobId.Value, ct) == null)
            throw new InvalidOperationException("File video không còn trên server (server đã khởi động lại) nên không đăng Instagram được.");
        v.Progress = "Đang đăng lên Instagram…";
        v.InstagramError = null;
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<StudioService>(s => s.RunInstagramAsync(id, CancellationToken.None));
        return await GetVideoAsync(id, ct);
    }

    [Queue("critical")]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunInstagramAsync(Guid id, CancellationToken ct)
    {
        var v = await _db.StudioVideos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (v == null || v.FinalBlobId == null || v.FacebookAccountId == null) return;
        var dir = StudioMedia.TempDir();
        try
        {
            var blob = await GetBlobAsync(v.FinalBlobId.Value, ct);
            if (blob == null)
            {
                v.Progress = null;
                v.InstagramError = "File video không còn trên server.";
                await _db.SaveChangesAsync(CancellationToken.None);
                return;
            }
            var path = Path.Combine(dir, "post.mp4");
            await File.WriteAllBytesAsync(path, blob.Data, ct);
            await PostToInstagramAsync(v, path, ct);
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

    public async Task<StudioSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var designed = await ReadDesignedVoicesAsync(ct);
        var defaultNarrator = await ReadSettingAsync(DefaultNarratorKey, ct);
        return new StudioSettingsDto
        {
            MonthlyBudgetUsd = await MonthlyBudgetAsync(ct),
            VideoConfigured = _video.IsConfigured,
            VideoModel = _video.DefaultModel,
            Models = ModelDtos(),
            // designed voices first: they are the ones made for this channel
            Voices = designed.Select(d => new StudioVoiceDto
                {
                    Id = d.Id, Name = d.Name, Gender = d.Gender, Designed = true,
                    Description = "Giọng thiết kế — " + (d.Description.Length > 90 ? d.Description[..90] + "…" : d.Description)
                })
                .Concat(StudioVoices.Dtos()).ToList(),
            VoicePricePerSecondUsd = _voices.PricePerSecond,
            SfxConfigured = _sfx.IsConfigured,
            SfxPricePerSecondUsd = _sfx.PricePerSecond,
            DefaultNarratorVoice = designed.Any(d => d.Id == defaultNarrator) ? defaultNarrator : null
        };
    }

    // ── designed voices (Gemini voice design) ──

    public const string DesignedVoicesKey = "Studio.DesignedVoices";
    public const string DefaultNarratorKey = "Studio.DefaultNarrator";

    public sealed class DesignedVoice
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Gender { get; set; } = "male";
        public string Description { get; set; } = string.Empty;
        public Guid? SampleBlobId { get; set; }
    }

    private async Task<string?> ReadSettingAsync(string key, CancellationToken ct) =>
        await _db.SystemSettings.AsNoTracking().Where(s => s.SettingKey == key).Select(s => s.SettingValue).FirstOrDefaultAsync(ct);

    private async Task WriteSettingAsync(string key, string? value, CancellationToken ct)
    {
        var row = await _db.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == key, ct);
        if (row == null) _db.SystemSettings.Add(new SystemSetting { SettingKey = key, SettingValue = value ?? "", Group = "Studio" });
        else row.SettingValue = value ?? "";
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<DesignedVoice>> ReadDesignedVoicesAsync(CancellationToken ct)
    {
        var raw = await ReadSettingAsync(DesignedVoicesKey, ct);
        try { return string.IsNullOrWhiteSpace(raw) ? new() : JsonSerializer.Deserialize<List<DesignedVoice>>(raw, Json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>Designs a voice from a description (e.g. a Southern Vietnamese storyteller) and adds it to every voice list.</summary>
    public async Task<StudioSettingsDto> DesignVoiceAsync(StudioVoiceDesignDto input, CancellationToken ct)
    {
        var name = Required(input.Name, "Tên giọng");
        var description = Required(input.Description, "Mô tả giọng");
        if (description.Length > 1500) description = description[..1500];
        var gender = input.Gender == "female" ? "female" : "male";
        var voices = await ReadDesignedVoicesAsync(ct);
        if (voices.Count >= 20) throw new InvalidOperationException("Đã có 20 giọng thiết kế — xoá bớt giọng không dùng rồi tạo tiếp.");
        var (id, sample) = await _voices.DesignAsync(name, gender, description, ct);
        Guid? sampleId = null;
        if (sample != null)
        {
            var blob = new StudioBlob { MimeType = "audio/mp4", Data = sample, SizeBytes = sample.Length };
            _db.StudioBlobs.Add(blob);
            sampleId = blob.Id;
        }
        voices.Insert(0, new DesignedVoice { Id = id, Name = name.Length > 60 ? name[..60] : name, Gender = gender, Description = description, SampleBlobId = sampleId });
        await WriteSettingAsync(DesignedVoicesKey, JsonSerializer.Serialize(voices, Json), ct);
        if (input.MakeDefault) await WriteSettingAsync(DefaultNarratorKey, id, ct);
        return await GetSettingsAsync(ct);
    }

    public async Task<StudioSettingsDto> DeleteDesignedVoiceAsync(string voiceId, CancellationToken ct)
    {
        var id = voiceId.StartsWith("gemini:") ? voiceId : "gemini:" + voiceId;
        var voices = await ReadDesignedVoicesAsync(ct);
        var voice = voices.FirstOrDefault(v => v.Id == id) ?? throw new KeyNotFoundException("Không tìm thấy giọng.");
        voices.Remove(voice);
        if (voice.SampleBlobId != null) await DeleteBlobAsync(voice.SampleBlobId.Value, ct);
        await WriteSettingAsync(DesignedVoicesKey, JsonSerializer.Serialize(voices, Json), ct);
        if (await ReadSettingAsync(DefaultNarratorKey, ct) == id) await WriteSettingAsync(DefaultNarratorKey, null, ct);
        return await GetSettingsAsync(ct);
    }

    public async Task<StudioSettingsDto> SetDefaultNarratorAsync(string? voiceId, CancellationToken ct)
    {
        var id = string.IsNullOrWhiteSpace(voiceId) ? null : voiceId.StartsWith("gemini:") ? voiceId : "gemini:" + voiceId;
        if (id != null && !(await ReadDesignedVoicesAsync(ct)).Any(v => v.Id == id)) throw new KeyNotFoundException("Không tìm thấy giọng.");
        await WriteSettingAsync(DefaultNarratorKey, id, ct);
        return await GetSettingsAsync(ct);
    }

    public async Task<Guid?> DesignedVoiceSampleBlobIdAsync(string voiceId, CancellationToken ct)
    {
        var id = voiceId.StartsWith("gemini:") ? voiceId : "gemini:" + voiceId;
        return (await ReadDesignedVoicesAsync(ct)).FirstOrDefault(v => v.Id == id)?.SampleBlobId;
    }

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
        // the name as it is on Facebook now: a renamed page must not stamp its old name on new videos
        var name = pageId == null ? null
            : await _facebook.RefreshPageNameAsync(pageId.Value, ct)
              ?? await _db.FacebookAccounts.AsNoTracking().Where(a => a.Id == pageId).Select(a => a.PageName).FirstOrDefaultAsync(ct);
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

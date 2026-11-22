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
/// AI video studio: script → shot plan (Gemini text) → one Grok Imagine clip per shot (each starting from an asset
/// image or the previous clip's last frame) → joined with music and upscaled to 1080p → posted to a Facebook Page. Every paid call is recorded in StudioCostRecords and checked against the monthly studio budget
/// before it is made. Files live in StudioBlobs (the host disk is wiped on deploy).
/// </summary>
public class StudioService
{
    public const string BudgetKey = "Studio.MonthlyBudgetUsd";
    public const string DeliveryResolution = "1080p";
    public const int MaxShotAssets = 3;
    private const int MaxAttemptsPerRun = 2;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly GrokVideoClient _video;
    private readonly FilmGeminiClient _gemini;
    private readonly StudioMedia _media;
    private readonly IFacebookPublishingService _facebook;
    private readonly IBackgroundJobClient _jobs;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StudioService> _logger;

    public StudioService(IApplicationDbContext db, GrokVideoClient video, FilmGeminiClient gemini, StudioMedia media,
        IFacebookPublishingService facebook, IBackgroundJobClient jobs, IConfiguration configuration, ILogger<StudioService> logger)
    {
        _db = db;
        _video = video;
        _gemini = gemini;
        _media = media;
        _facebook = facebook;
        _jobs = jobs;
        _configuration = configuration;
        _logger = logger;
    }

    // ═══════════════════════════════ Assets ═══════════════════════════════

    public async Task<List<StudioAssetDto>> ListAssetsAsync(CancellationToken ct) =>
        (await _db.StudioAssets.AsNoTracking().OrderBy(a => a.Kind).ThenBy(a => a.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<StudioAssetDto> AddAssetAsync(string name, string kind, string? description, byte[] data, string extension, CancellationToken ct)
    {
        if (data.Length == 0) throw new ArgumentException("File ảnh trống.");
        var jpeg = await _media.NormalizeImageAsync(data, extension, ct);
        var blob = new StudioBlob { MimeType = "image/jpeg", Data = jpeg, SizeBytes = jpeg.Length };
        var asset = new StudioAsset
        {
            Name = Required(name, "Tên"), Kind = NormalizeKind(kind), Description = (description ?? "").Trim(),
            BlobId = blob.Id, MimeType = blob.MimeType, SizeBytes = jpeg.Length
        };
        _db.StudioBlobs.Add(blob);
        _db.StudioAssets.Add(asset);
        await _db.SaveChangesAsync(ct);
        return ToDto(asset);
    }

    public async Task<StudioAssetDto> UpdateAssetAsync(Guid id, string? name, string? kind, string? description, CancellationToken ct)
    {
        var a = await _db.StudioAssets.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy ảnh.");
        if (!string.IsNullOrWhiteSpace(name)) a.Name = name.Trim();
        if (!string.IsNullOrWhiteSpace(kind)) a.Kind = NormalizeKind(kind);
        if (description != null) a.Description = description.Trim();
        await _db.SaveChangesAsync(ct);
        return ToDto(a);
    }

    public async Task DeleteAssetAsync(Guid id, CancellationToken ct)
    {
        var a = await _db.StudioAssets.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy ảnh.");
        await DeleteBlobAsync(a.BlobId, ct);
        _db.StudioAssets.Remove(a);
        await _db.SaveChangesAsync(ct);
    }

    // ═══════════════════════════════ Music ═══════════════════════════════

    public async Task<List<StudioMusicDto>> ListMusicAsync(CancellationToken ct) =>
        (await _db.StudioMusicTracks.AsNoTracking().OrderBy(m => m.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<StudioMusicDto> AddMusicAsync(string name, string? mood, byte[] data, string mimeType, string extension, CancellationToken ct)
    {
        if (data.Length == 0) throw new ArgumentException("File nhạc trống.");
        var seconds = await _media.ProbeSecondsAsync(data, extension, ct);
        if (seconds <= 0) throw new ArgumentException("Không đọc được file nhạc (hãy dùng MP3, M4A, WAV hoặc OGG).");
        var blob = new StudioBlob { MimeType = string.IsNullOrWhiteSpace(mimeType) ? "audio/mpeg" : mimeType, Data = data, SizeBytes = data.Length };
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

    public async Task<StudioBlob?> GetBlobAsync(Guid blobId, CancellationToken ct) =>
        await _db.StudioBlobs.AsNoTracking().FirstOrDefaultAsync(b => b.Id == blobId, ct);

    public async Task<Guid?> AssetBlobIdAsync(Guid assetId, CancellationToken ct) =>
        await _db.StudioAssets.Where(a => a.Id == assetId).Select(a => (Guid?)a.BlobId).FirstOrDefaultAsync(ct);

    public async Task<Guid?> MusicBlobIdAsync(Guid musicId, CancellationToken ct) =>
        await _db.StudioMusicTracks.Where(a => a.Id == musicId).Select(a => (Guid?)a.BlobId).FirstOrDefaultAsync(ct);

    public async Task<Guid?> ShotClipBlobIdAsync(Guid shotId, CancellationToken ct) =>
        await _db.StudioShots.Where(s => s.Id == shotId).Select(s => s.ClipBlobId).FirstOrDefaultAsync(ct);

    public async Task<Guid?> FinalBlobIdAsync(Guid videoId, CancellationToken ct) =>
        await _db.StudioVideos.Where(v => v.Id == videoId).Select(v => v.FinalBlobId).FirstOrDefaultAsync(ct);

    // ═══════════════════════════════ Videos ═══════════════════════════════

    public async Task<List<StudioVideoSummaryDto>> ListVideosAsync(CancellationToken ct)
    {
        var pages = await PageNamesAsync(ct);
        var videos = await _db.StudioVideos.AsNoTracking().Include(v => v.Shots).OrderByDescending(v => v.CreatedAtUtc).Take(200).ToListAsync(ct);
        return videos.Select(v => FillSummary(new StudioVideoSummaryDto(), v, pages)).ToList();
    }

    public async Task<StudioVideoDto> GetVideoAsync(Guid id, CancellationToken ct)
    {
        var v = await _db.StudioVideos.AsNoTracking().Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException("Không tìm thấy video.");
        var pages = await PageNamesAsync(ct);
        var dto = FillSummary(new StudioVideoDto(), v, pages);
        dto.Script = v.Script;
        dto.AspectRatio = v.AspectRatio;
        dto.Resolution = v.Resolution;
        dto.StyleNotes = v.StyleNotes;
        dto.AssetIds = Ids(v.AssetIdsJson);
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
            Speaker = s.Speaker, Dialogue = s.Dialogue, Sound = s.Sound, AssetIds = Ids(s.AssetIdsJson),
            ContinueFromPrevious = s.ContinueFromPrevious, Status = s.Status,
            HasClip = s.ClipBlobId != null, CostUsd = s.CostUsd, Attempts = s.Attempts, ErrorMessage = s.ErrorMessage,
            EstimatedCostUsd = ShotCost(v, s)
        }).ToList();
        return dto;
    }

    public async Task<StudioVideoDto> CreateVideoAsync(StudioVideoInputDto input, CancellationToken ct)
    {
        var v = new StudioVideo
        {
            Resolution = _configuration["Studio:DefaultResolution"] ?? "720p",
            AspectRatio = _configuration["Studio:DefaultAspectRatio"] ?? "9:16"
        };
        Apply(v, input);
        if (string.IsNullOrWhiteSpace(v.Title)) v.Title = "Video " + DateTime.UtcNow.AddHours(7).ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
        _db.StudioVideos.Add(v);
        await _db.SaveChangesAsync(ct);
        return await GetVideoAsync(v.Id, ct);
    }

    public async Task<StudioVideoDto> UpdateVideoAsync(Guid id, StudioVideoInputDto input, CancellationToken ct)
    {
        var v = await _db.StudioVideos.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        EnsureNotBusy(v);
        Apply(v, input);
        await _db.SaveChangesAsync(ct);
        return await GetVideoAsync(id, ct);
    }

    public async Task DeleteVideoAsync(Guid id, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        EnsureNotBusy(v);
        foreach (var s in v.Shots.Where(s => s.ClipBlobId != null)) await DeleteBlobAsync(s.ClipBlobId!.Value, ct);
        if (v.FinalBlobId != null) await DeleteBlobAsync(v.FinalBlobId.Value, ct);
        _db.StudioVideos.Remove(v);
        await _db.SaveChangesAsync(ct);
    }

    private static void Apply(StudioVideo v, StudioVideoInputDto i)
    {
        if (i.Title != null) v.Title = i.Title.Trim();
        if (i.FacebookAccountId != null) v.FacebookAccountId = i.FacebookAccountId == Guid.Empty ? null : i.FacebookAccountId;
        if (i.Script != null) v.Script = i.Script.Trim();
        if (i.TargetDurationSeconds != null) v.TargetDurationSeconds = Math.Clamp(i.TargetDurationSeconds.Value, 5, 600);
        if (i.AspectRatio != null) v.AspectRatio = i.AspectRatio == "16:9" ? "16:9" : "9:16";
        if (i.Resolution != null) v.Resolution = GrokVideoClient.NormalizeResolution(i.Resolution);
        if (i.StyleNotes != null) v.StyleNotes = i.StyleNotes.Trim();
        if (i.AssetIds != null) v.AssetIdsJson = JsonSerializer.Serialize(i.AssetIds.Distinct());
        if (i.MusicMode != null) v.MusicMode = i.MusicMode is "Manual" or "None" ? i.MusicMode : "Auto";
        if (i.MusicTrackId != null) v.MusicTrackId = i.MusicTrackId == Guid.Empty ? null : i.MusicTrackId;
        if (i.MusicVolume != null) v.MusicVolume = Math.Clamp(i.MusicVolume.Value, 0, 1);
        if (i.Caption != null) v.Caption = i.Caption.Trim();
    }

    // ═══════════════════════════════ Planning ═══════════════════════════════

    /// <summary>Asks Gemini to cut the script into clip-sized shots (replaces the current shot list).</summary>
    public async Task<StudioVideoDto> PlanAsync(Guid id, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        EnsureNotBusy(v);
        if (string.IsNullOrWhiteSpace(v.Script)) throw new InvalidOperationException("Hãy nhập kịch bản trước khi lập kế hoạch.");
        if (v.Shots.Any(s => s.ClipBlobId != null))
            throw new InvalidOperationException("Video đã có clip được tạo — lập kế hoạch lại sẽ bỏ các clip đó. Hãy xoá clip/tạo video mới nếu muốn làm lại từ đầu.");

        var assets = await LoadAssetsAsync(Ids(v.AssetIdsJson), ct);
        var music = await _db.StudioMusicTracks.AsNoTracking().OrderBy(m => m.UseCount).ToListAsync(ct);
        v.Status = "Planning";
        v.ErrorMessage = null;
        await _db.SaveChangesAsync(ct);

        try
        {
            var (system, user) = BuildPlanPrompt(v, assets, music);
            var result = await _gemini.GenerateJsonAsync(system, user, 8192, 0.6, ct);
            var plan = ParsePlan(result.Text, assets.Select(a => a.Id).ToHashSet(), music.Select(m => m.Id).ToHashSet());

            _db.StudioShots.RemoveRange(v.Shots);
            v.Shots.Clear();
            var index = 1;
            foreach (var s in plan.Shots)
            {
                // Added through the DbSet: a new entity already carries its Guid, so attaching it via the navigation
                // would make EF treat it as an existing row (UPDATE → "expected to affect 1 row").
                _db.StudioShots.Add(new StudioShot
                {
                    StudioVideoId = v.Id, ContinueFromPrevious = index > 1 && s.ContinueFromPrevious,
                    Index = index++, DurationSeconds = GrokVideoClient.ClampSeconds(s.Duration),
                    Action = s.Action, Camera = s.Camera, Speaker = NullIfEmpty(s.Speaker), Dialogue = NullIfEmpty(s.Dialogue),
                    Sound = NullIfEmpty(s.Sound), AssetIdsJson = JsonSerializer.Serialize(s.AssetIds)
                });
            }
            if (string.IsNullOrWhiteSpace(v.Caption)) v.Caption = plan.Caption;
            v.PlanNotes = plan.Notes;
            if (v.MusicMode == "Auto") v.MusicTrackId = plan.MusicTrackId ?? music.FirstOrDefault()?.Id;
            v.EstimatedCostUsd = EstimateVideoCost(v);
            v.Status = "Planned";
            _db.StudioCostRecords.Add(new StudioCostRecord
            {
                StudioVideoId = v.Id, Kind = "Plan", Model = _gemini.TextModel, Tokens = result.Tokens, CostUsd = result.CostUsd,
                Note = result.FreeKey ? "khoá miễn phí" : null
            });
            v.ActualCostUsd += result.CostUsd;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            v.Status = "Draft";
            v.ErrorMessage = "Lập kế hoạch thất bại: " + ex.Message;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        return await GetVideoAsync(id, ct);
    }

    public static (string System, string User) BuildPlanPrompt(StudioVideo v, IReadOnlyList<StudioAsset> assets, IReadOnlyList<StudioMusicTrack> music)
    {
        var system =
            "Bạn là đạo diễn phim ngắn người thật cho mạng xã hội. Bạn chia kịch bản thành các shot để model video Grok Imagine tạo " +
            "(mỗi shot là 1 clip 2–15 giây có cả hình, tiếng động và lời thoại khớp miệng, bắt đầu từ 1 ảnh mở đầu). Chỉ trả về JSON hợp lệ.";
        var sb = new StringBuilder();
        sb.AppendLine($"KỊCH BẢN (giữ nguyên lời thoại):\n{v.Script}\n");
        sb.AppendLine($"Thời lượng mục tiêu: khoảng {v.TargetDurationSeconds} giây. Khung hình {(v.AspectRatio == "16:9" ? "ngang 16:9" : "dọc 9:16")}.");
        if (!string.IsNullOrWhiteSpace(v.StyleNotes)) sb.AppendLine($"Phong cách / ghi chú của người dùng: {v.StyleNotes}");
        sb.AppendLine("\nẢNH CÓ SẴN (chỉ dùng các id này):");
        if (assets.Count == 0) sb.AppendLine("- (không có — mô tả nhân vật và bối cảnh thật chi tiết, nhất quán trong mọi shot)");
        foreach (var a in assets) sb.AppendLine($"- id={a.Id} | {a.Kind} | {a.Name} | {a.Description}");
        sb.AppendLine("\nKHO NHẠC NỀN (chọn 1 bài hợp không khí, hoặc null):");
        if (music.Count == 0) sb.AppendLine("- (trống)");
        foreach (var m in music) sb.AppendLine($"- id={m.Id} | {m.Name} | tâm trạng: {m.Mood} | {m.DurationSeconds:0}s");
        sb.AppendLine(@"
CÁCH MODEL VIDEO HOẠT ĐỘNG:
- Mỗi clip BẮT ĐẦU TỪ ĐÚNG 1 ẢNH rồi chuyển động tiếp. Model không có ảnh tham chiếu riêng cho từng nhân vật: nhân vật nào không có
  trong ảnh mở đầu thì model tự vẽ (dễ sai mặt). Vì vậy ảnh mở đầu nên chứa những nhân vật xuất hiện trong shot.
- ""assetIds"": id ĐẦU TIÊN là ẢNH MỞ ĐẦU của shot (ưu tiên ảnh có nhân vật đang nói đứng trong bối cảnh). Có thể thêm tối đa 2 id
  khác chỉ để mô tả bằng chữ. Không có ảnh phù hợp thì để [].
- ""continueFromPrevious"": true khi shot nối liền shot trước trong cùng 1 cảnh (cùng nơi, cùng người, máy gần như giữ góc) — clip sẽ
  bắt đầu từ khung hình cuối của clip trước nên mặt nhân vật liền mạch. false khi đổi góc máy mạnh, đổi cảnh, hoặc là shot đầu.
  Khi true thì ảnh mở đầu (assetIds) bị bỏ qua.

QUY TẮC:
- Tổng ""duration"" các shot gần bằng thời lượng mục tiêu (±10%). Mỗi shot 4–15 giây (số nguyên), tốt nhất 6–10 giây.
- Tiếng Việt nói khoảng 2,5 chữ/giây: duration của shot có thoại >= số chữ / 2,5 + 1. Câu quá dài thì tách sang shot sau.
- Mỗi shot tối đa 1 người nói. ""dialogue"" là câu thoại nguyên văn tiếng Việt trong kịch bản (không dịch, không thêm). Shot không thoại thì để null.
- ""action"", ""camera"", ""sound"" viết bằng TIẾNG ANH, cụ thể, dễ quay: ai làm gì, cảm xúc, cử chỉ nhỏ có chủ đích; cỡ cảnh + chuyển động máy; tiếng động môi trường (không nhạc).
- Giữ liên tục: cùng bối cảnh, trang phục, đạo cụ, thời điểm trong ngày giữa các shot.
- ""caption"": chú thích đăng Facebook bằng tiếng Việt, 1–3 câu cuốn hút + 2–4 hashtag.

Trả về JSON:
{ ""caption"": ""..."", ""notes"": ""ghi chú đạo diễn ngắn bằng tiếng Việt"", ""musicTrackId"": ""id hoặc null"",
  ""shots"": [ { ""duration"": 8, ""assetIds"": [""...""], ""continueFromPrevious"": false, ""action"": ""..."", ""camera"": ""..."", ""speaker"": ""tên hoặc null"", ""dialogue"": ""... hoặc null"", ""sound"": ""..."" } ] }");
        return (system, sb.ToString());
    }

    public sealed record PlannedShot(int Duration, List<Guid> AssetIds, string Action, string Camera, string? Speaker, string? Dialogue, string? Sound,
        bool ContinueFromPrevious = false);
    public sealed record Plan(string? Caption, string? Notes, Guid? MusicTrackId, List<PlannedShot> Shots);

    /// <summary>Reads the planner's JSON, keeping only known asset / music ids and valid clip durations.</summary>
    public static Plan ParsePlan(string json, ISet<Guid> knownAssets, ISet<Guid> knownMusic)
    {
        var text = json.Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidOperationException("Gemini không trả về kế hoạch dạng JSON.");
        using var doc = JsonDocument.Parse(text[start..(end + 1)]);
        var root = doc.RootElement;
        string? S(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()?.Trim() : null;

        Guid? musicId = Guid.TryParse(S(root, "musicTrackId"), out var mid) && knownMusic.Contains(mid) ? mid : null;
        var shots = new List<PlannedShot>();
        if (root.TryGetProperty("shots", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in arr.EnumerateArray())
            {
                var duration = s.TryGetProperty("duration", out var d) && d.TryGetDouble(out var dv) ? (int)Math.Round(dv) : 8;
                var ids = new List<Guid>();
                if (s.TryGetProperty("assetIds", out var a) && a.ValueKind == JsonValueKind.Array)
                    foreach (var x in a.EnumerateArray())
                        if (Guid.TryParse(x.GetString(), out var g) && knownAssets.Contains(g) && !ids.Contains(g)) ids.Add(g);
                var action = S(s, "action") ?? "";
                if (action.Length == 0 && string.IsNullOrWhiteSpace(S(s, "dialogue"))) continue;
                var cont = shots.Count > 0 && s.TryGetProperty("continueFromPrevious", out var c) && c.ValueKind == JsonValueKind.True;
                shots.Add(new PlannedShot(GrokVideoClient.ClampSeconds(duration), ids.Take(MaxShotAssets).ToList(),
                    action, S(s, "camera") ?? "", NullIfNull(S(s, "speaker")), NullIfNull(S(s, "dialogue")), NullIfNull(S(s, "sound")), cont));
            }
        }
        if (shots.Count == 0) throw new InvalidOperationException("Gemini trả về kế hoạch không có shot nào.");
        return new Plan(S(root, "caption"), S(root, "notes"), musicId, shots);
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
            StudioShot shot;
            if (input.Id is { } sid && existing.TryGetValue(sid, out var found))
            {
                shot = found;
                var changed = shot.DurationSeconds != GrokVideoClient.ClampSeconds(input.DurationSeconds) || shot.Action != input.Action.Trim()
                              || shot.Camera != input.Camera.Trim() || shot.Dialogue != NullIfEmpty(input.Dialogue) || shot.Speaker != NullIfEmpty(input.Speaker)
                              || shot.Sound != NullIfEmpty(input.Sound) || shot.AssetIdsJson != JsonSerializer.Serialize(input.AssetIds)
                              || shot.ContinueFromPrevious != (index > 1 && input.ContinueFromPrevious);
                if (changed && shot.Status == "Done") shot.Status = "Pending"; // edited → the clip no longer matches; regenerate
            }
            else
            {
                shot = new StudioShot { StudioVideoId = v.Id };
                _db.StudioShots.Add(shot); // via the DbSet: see PlanAsync
            }
            keep.Add(shot.Id);
            shot.Index = index++;
            shot.ContinueFromPrevious = shot.Index > 1 && input.ContinueFromPrevious;
            shot.DurationSeconds = GrokVideoClient.ClampSeconds(input.DurationSeconds);
            shot.Action = input.Action.Trim();
            shot.Camera = input.Camera.Trim();
            shot.Speaker = NullIfEmpty(input.Speaker);
            shot.Dialogue = NullIfEmpty(input.Dialogue);
            shot.Sound = NullIfEmpty(input.Sound);
            shot.AssetIdsJson = JsonSerializer.Serialize(input.AssetIds.Distinct().Take(MaxShotAssets));
        }
        foreach (var gone in v.Shots.Where(s => !keep.Contains(s.Id)).ToList())
        {
            if (gone.ClipBlobId != null) await DeleteBlobAsync(gone.ClipBlobId.Value, ct);
            _db.StudioShots.Remove(gone);
            v.Shots.Remove(gone);
        }
        v.EstimatedCostUsd = EstimateVideoCost(v);
        if (v.Status is "Draft" or "Ready" or "Failed") v.Status = v.Shots.Count > 0 ? "Planned" : "Draft";
        await _db.SaveChangesAsync(ct);
        return await GetVideoAsync(id, ct);
    }

    private decimal EstimateVideoCost(StudioVideo v) => Math.Round(v.Shots.Sum(s => ShotCost(v, s)), 4);

    /// <summary>Seconds × price, plus the input-image fee when the clip starts from a picture.</summary>
    private decimal ShotCost(StudioVideo v, StudioShot s) =>
        _video.EstimateCost(s.DurationSeconds, v.Resolution, s.ContinueFromPrevious || Ids(s.AssetIdsJson).Count > 0);

    // ═══════════════════════════════ Generation ═══════════════════════════════

    /// <summary>Queues clip generation for every shot without a clip (or only <paramref name="shotId"/>), then composing.</summary>
    public async Task<StudioVideoDto> StartGenerateAsync(Guid id, Guid? shotId, CancellationToken ct)
    {
        if (!_video.IsConfigured) throw new InvalidOperationException("Chưa có khoá xAI (biến XAI_API_KEY trên Railway) — chưa tạo được video.");
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
        if (v == null) return;
        var todo = v.Shots.OrderBy(s => s.Index)
            .Where(s => shotId != null ? s.Id == shotId : s.Status != "Done" || s.ClipBlobId == null).ToList();
        var assets = (await LoadAssetsAsync(v.Shots.SelectMany(s => Ids(s.AssetIdsJson)).Distinct().ToList(), ct)).ToDictionary(a => a.Id);
        var failed = new List<string>();
        var done = 0;
        var ordered = v.Shots.OrderBy(s => s.Index).ToList();

        foreach (var shot in todo)
        {
            v.Progress = $"Đang tạo clip {++done}/{todo.Count} (shot {shot.Index})…";
            shot.Status = "Generating";
            await _db.SaveChangesAsync(ct);

            var seconds = GrokVideoClient.ClampSeconds(shot.DurationSeconds);
            try
            {
                await EnsureBudgetAsync(ShotCost(v, shot), ct);
            }
            catch (InvalidOperationException ex)
            {
                shot.Status = "Failed";
                shot.ErrorMessage = ex.Message;
                failed.Add($"shot {shot.Index}: {ex.Message}");
                break; // budget reached: stop spending
            }

            StartFrame? frame;
            bool continued;
            try
            {
                (frame, continued) = await StartFrameAsync(v, ordered, shot, assets, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                shot.Status = "Failed";
                shot.ErrorMessage = ex.Message;
                failed.Add($"shot {shot.Index}: {ex.Message}");
                await _db.SaveChangesAsync(ct);
                continue;
            }
            var prompt = BuildShotPrompt(v, shot, assets, continued);

            for (var attempt = 1; attempt <= MaxAttemptsPerRun; attempt++)
            {
                shot.Attempts++;
                try
                {
                    var clip = await _video.GenerateAsync(prompt, frame, v.AspectRatio, v.Resolution, seconds, ct);
                    if (shot.ClipBlobId != null) await DeleteBlobAsync(shot.ClipBlobId.Value, ct);
                    var b = new StudioBlob { MimeType = clip.MimeType, Data = clip.Video, SizeBytes = clip.Video.Length };
                    _db.StudioBlobs.Add(b);
                    shot.ClipBlobId = b.Id;
                    shot.InteractionId = clip.RequestId;
                    shot.CostUsd += clip.CostUsd;
                    shot.Status = "Done";
                    shot.ErrorMessage = null;
                    v.ActualCostUsd += clip.CostUsd;
                    _db.StudioCostRecords.Add(new StudioCostRecord
                    {
                        StudioVideoId = v.Id, StudioShotId = shot.Id, Kind = "Video", Model = _video.Model, Seconds = clip.Seconds,
                        CostUsd = clip.CostUsd, Note = $"shot {shot.Index}, {v.Resolution}" + (frame == null ? "" : continued ? ", nối khung cuối" : ", từ ảnh")
                    });
                    await _db.SaveChangesAsync(ct);
                    break;
                }
                catch (VideoRejectedException ex)
                {
                    // refused / filtered (moderation, invalid argument): retrying the same prompt only spends money
                    shot.Status = "Failed";
                    shot.ErrorMessage = ex.Message;
                    if (ex.BilledUsd > 0)
                    {
                        shot.CostUsd += ex.BilledUsd;
                        v.ActualCostUsd += ex.BilledUsd;
                        _db.StudioCostRecords.Add(new StudioCostRecord
                        {
                            StudioVideoId = v.Id, StudioShotId = shot.Id, Kind = "Video", Model = _video.Model, Seconds = seconds,
                            CostUsd = ex.BilledUsd, Note = $"shot {shot.Index}, bị lọc nội dung"
                        });
                    }
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Video shot {Index} attempt {Attempt} failed", shot.Index, attempt);
                    shot.Status = "Failed";
                    shot.ErrorMessage = ex.Message;
                    if (attempt < MaxAttemptsPerRun) await Task.Delay(TimeSpan.FromSeconds(10), ct);
                }
            }
            if (shot.Status != "Done") failed.Add($"shot {shot.Index}: {shot.ErrorMessage}");
            await _db.SaveChangesAsync(ct);
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
    /// Picks the picture a shot starts from: the previous clip's last frame when the shot continues the take (and that
    /// clip exists), otherwise its first asset image cropped to the video's frame; null → text-to-video.
    /// </summary>
    private async Task<(StartFrame? Frame, bool Continued)> StartFrameAsync(StudioVideo v, List<StudioShot> ordered, StudioShot shot,
        IReadOnlyDictionary<Guid, StudioAsset> assets, CancellationToken ct)
    {
        if (shot.ContinueFromPrevious)
        {
            var prev = ordered.LastOrDefault(s => s.Index < shot.Index);
            if (prev?.ClipBlobId is { } clipId && await GetBlobAsync(clipId, ct) is { } clip)
                return (new StartFrame(await _media.LastFrameAsync(clip.Data, ct), "image/jpeg"), true);
        }
        foreach (var aid in Ids(shot.AssetIdsJson))
            if (assets.TryGetValue(aid, out var a) && await GetBlobAsync(a.BlobId, ct) is { } blob)
                return (new StartFrame(await _media.PrepareStartFrameAsync(blob.Data, v.AspectRatio, ct), "image/jpeg"), false);
        return (null, false);
    }

    /// <summary>The text sent to the video model for one shot (the start frame, if any, goes with it).</summary>
    public static string BuildShotPrompt(StudioVideo v, StudioShot shot, IReadOnlyDictionary<Guid, StudioAsset> assets, bool continued = false)
    {
        var used = Ids(shot.AssetIdsJson).Where(assets.ContainsKey).Select(id => assets[id]).ToList();
        var sb = new StringBuilder();
        sb.Append($"Live-action cinematic video, photorealistic, real people, {(v.AspectRatio == "16:9" ? "horizontal 16:9" : "vertical 9:16")} framing. ");
        if (continued) sb.Append("Continue seamlessly from the starting image: same people, same faces, same clothes, same place and light. ");
        else if (used.Count > 0) sb.Append("Start from the provided image and keep everyone's face, hair and clothes identical to it. ");
        var locations = used.Where(a => a.Kind == "Location").ToList();
        var people = used.Where(a => a.Kind == "Character").ToList();
        var props = used.Where(a => a.Kind == "Prop").ToList();
        if (locations.Count > 0) sb.Append("Setting: ").Append(string.Join("; ", locations.Select(l => $"{l.Name} — {l.Description}"))).Append(". ");
        if (people.Count > 0) sb.Append("Characters: ").Append(string.Join("; ", people.Select(p => $"{p.Name}: {p.Description}"))).Append(". ");
        if (props.Count > 0) sb.Append("Props: ").Append(string.Join("; ", props.Select(p => $"{p.Name} — {p.Description}"))).Append(". ");
        sb.Append("Action: ").Append(shot.Action.TrimEnd('.')).Append(". ");
        if (!string.IsNullOrWhiteSpace(shot.Camera)) sb.Append("Camera: ").Append(shot.Camera.TrimEnd('.')).Append(". ");
        if (!string.IsNullOrWhiteSpace(shot.Dialogue))
        {
            var who = string.IsNullOrWhiteSpace(shot.Speaker) ? "The character" : shot.Speaker;
            sb.Append($"Dialogue: {who} speaks Vietnamese naturally, with lip movements exactly matching the words, and says exactly: \"{shot.Dialogue}\". ")
              .Append($"Only {who} speaks in this shot. ");
        }
        else
        {
            sb.Append("No dialogue. ");
        }
        sb.Append("Sound: ").Append(string.IsNullOrWhiteSpace(shot.Sound) ? "natural ambient sound of the scene" : shot.Sound!.TrimEnd('.')).Append(". No background music. ");
        if (!string.IsNullOrWhiteSpace(v.StyleNotes)) sb.Append("Style: ").Append(v.StyleNotes!.TrimEnd('.')).Append(". ");
        sb.Append("Natural, believable acting. No subtitles, no captions, no on-screen text.");
        return sb.ToString();
    }

    // ═══════════════════════════════ Composing ═══════════════════════════════

    public async Task<StudioVideoDto> StartComposeAsync(Guid id, CancellationToken ct)
    {
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
        v.Progress = "Đang ghép video và nhạc nền…";
        await _db.SaveChangesAsync(ct);
        var dir = StudioMedia.TempDir();
        try
        {
            var clips = new List<string>();
            foreach (var s in v.Shots.OrderBy(s => s.Index))
            {
                var blob = s.ClipBlobId == null ? null : await GetBlobAsync(s.ClipBlobId.Value, ct);
                if (blob == null) throw new InvalidOperationException($"Shot {s.Index} chưa có clip.");
                var path = Path.Combine(dir, $"clip_{s.Index:D3}.mp4");
                await File.WriteAllBytesAsync(path, blob.Data, ct);
                clips.Add(path);
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

            var output = Path.Combine(dir, "final.mp4");
            // delivered at 1080p whatever the clip resolution (Facebook keeps a sharper copy of 1080p uploads)
            var duration = await _media.ComposeAsync(clips, musicPath, v.MusicVolume, v.AspectRatio, DeliveryResolution, output, ct);
            var bytes = await File.ReadAllBytesAsync(output, ct);
            if (v.FinalBlobId != null) await DeleteBlobAsync(v.FinalBlobId.Value, ct);
            var final = new StudioBlob { MimeType = "video/mp4", Data = bytes, SizeBytes = bytes.Length };
            _db.StudioBlobs.Add(final);
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

    /// <summary>Frees the database space of the clips once the final video exists (it can still be downloaded).</summary>
    public async Task<StudioVideoDto> PurgeClipsAsync(Guid id, CancellationToken ct)
    {
        var v = await _db.StudioVideos.Include(x => x.Shots).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy video.");
        EnsureNotBusy(v);
        if (v.FinalBlobId == null) throw new InvalidOperationException("Chưa có video hoàn chỉnh — giữ lại các clip.");
        foreach (var s in v.Shots.Where(s => s.ClipBlobId != null))
        {
            await DeleteBlobAsync(s.ClipBlobId!.Value, ct);
            s.ClipBlobId = null;
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
        VideoModel = _video.Model
    };

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
            PricePerSecondUsd = GrokVideoClient.Resolutions.ToDictionary(r => r, r => _video.PricePerSecond(r)),
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

    private async Task<List<StudioAsset>> LoadAssetsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        ids.Count == 0 ? new List<StudioAsset>() : await _db.StudioAssets.AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(ct);

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

    private static void EnsureNotBusy(StudioVideo v)
    {
        if (v.Status is "Planning" or "Generating" or "Composing" or "Publishing")
            throw new InvalidOperationException($"Video đang trong bước \"{v.Status}\" — chờ xong rồi thao tác tiếp.");
    }

    private async Task DeleteBlobAsync(Guid id, CancellationToken ct)
    {
        var blob = await _db.StudioBlobs.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (blob != null) _db.StudioBlobs.Remove(blob);
    }

    public static List<Guid> Ids(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<Guid>();
        try { return JsonSerializer.Deserialize<List<Guid>>(json, Json) ?? new List<Guid>(); }
        catch (JsonException) { return new List<Guid>(); }
    }

    private static string NormalizeKind(string? kind) => kind switch
    {
        "Location" or "location" => "Location",
        "Prop" or "prop" => "Prop",
        _ => "Character"
    };

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

    private static StudioAssetDto ToDto(StudioAsset a) => new()
    {
        Id = a.Id, Name = a.Name, Kind = a.Kind, Description = a.Description, SizeBytes = a.SizeBytes, CreatedAtUtc = a.CreatedAtUtc
    };

    private static StudioMusicDto ToDto(StudioMusicTrack m) => new()
    {
        Id = m.Id, Name = m.Name, Mood = m.Mood, DurationSeconds = m.DurationSeconds, SizeBytes = m.SizeBytes, UseCount = m.UseCount, CreatedAtUtc = m.CreatedAtUtc
    };
}

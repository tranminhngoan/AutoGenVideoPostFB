using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Infrastructure.FilmFactory;

/// <summary>
/// AI Showrunner: every creative step (story bible, cast &amp; locations, episode scripts) is written by
/// Gemini from the user's own idea. There is no template fallback — a failed call fails the step with
/// Google's reason instead of producing a film about someone else's story.
/// </summary>
public class FilmShowrunnerService : IFilmShowrunnerService
{
    /// <summary>Provider stamped on shots written by this engine (older template shots are rewritten).</summary>
    public const string ShotProvider = "GeminiImage";

    public const string NarratorId = "narrator";

    private static readonly string[] CameraMoves = { "SlowZoomIn", "SlowZoomOut", "PanLeft", "PanRight", "TiltUp", "TiltDown", "Static" };

    private readonly IApplicationDbContext _context;
    private readonly IFilmCostController _costController;
    private readonly FilmGeminiClient _gemini;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FilmShowrunnerService> _logger;

    public FilmShowrunnerService(
        IApplicationDbContext context,
        IFilmCostController costController,
        FilmGeminiClient gemini,
        IConfiguration configuration,
        ILogger<FilmShowrunnerService> logger)
    {
        _context = context;
        _costController = costController;
        _gemini = gemini;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Still frames generated per episode (each is reused by several shots with different camera moves).</summary>
    private int FramesPerEpisode => Math.Clamp(_configuration.GetValue("Film:FramesPerEpisode", 12), 4, 40);

    // ─────────────────────────────── Story bible ───────────────────────────────

    public async Task<FilmStoryBibleDto> DevelopStoryBibleAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await _context.FilmProjects
            .Include(p => p.FilmPage)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new ArgumentException("Không tìm thấy dự án phim.");

        _logger.LogInformation("AI Showrunner: Developing Story Bible for Project {Id} - '{Title}'", projectId, project.Title);

        project.Status = "ShowrunnerPlanning";
        project.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        var bible = await CallGeminiForStoryBibleAsync(project, ct);

        var existingBible = await _context.FilmStoryBibles.FirstOrDefaultAsync(b => b.FilmProjectId == projectId, ct);
        if (existingBible != null) _context.FilmStoryBibles.Remove(existingBible);

        bible.FilmProjectId = projectId;
        _context.FilmStoryBibles.Add(bible);

        var existingEpisodes = await _context.FilmEpisodes
            .Where(e => e.FilmProjectId == projectId)
            .OrderBy(e => e.EpisodeNumber)
            .ToListAsync(ct);

        var newEpisodes = ParseEpisodesFromJson(projectId, bible.EpisodesSummaryJson, project.TargetDurationPerEpisodeSeconds);

        // Cập nhật trực tiếp trên các tập hiện có để giữ nguyên ID, tránh lỗi DbUpdateConcurrencyException
        for (int i = 0; i < newEpisodes.Count; i++)
        {
            var parsed = newEpisodes[i];
            if (i < existingEpisodes.Count)
            {
                var existing = existingEpisodes[i];
                existing.EpisodeNumber = parsed.EpisodeNumber;
                existing.Title = parsed.Title;
                existing.Synopsis = parsed.Synopsis;
                existing.OpeningHook = parsed.OpeningHook;
                existing.EndingCliffhanger = parsed.EndingCliffhanger;
                existing.TargetDurationSeconds = parsed.TargetDurationSeconds;
                existing.Status = "Pending";
                existing.ErrorMessage = null;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                _context.FilmEpisodes.Add(parsed);
            }
        }

        // Nếu số tập mới ít hơn số tập cũ, gỡ bỏ các tập dư thừa
        if (existingEpisodes.Count > newEpisodes.Count)
        {
            var toRemove = existingEpisodes.Skip(newEpisodes.Count).ToList();
            var removeIds = toRemove.Select(e => e.Id).ToList();
            foreach (var rec in await _context.FilmCostRecords
                         .Where(c => c.FilmEpisodeId != null && removeIds.Contains(c.FilmEpisodeId.Value)).ToListAsync(ct))
            {
                rec.FilmEpisodeId = null;
                rec.FilmShotId = null;
            }
            _context.FilmEpisodes.RemoveRange(toRemove);
        }

        project.Status = "BibleReady";
        project.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return new FilmStoryBibleDto
        {
            Id = bible.Id,
            FilmProjectId = bible.FilmProjectId,
            Logline = bible.Logline,
            Genre = bible.Genre,
            WorldRules = bible.WorldRules,
            TimePeriod = bible.TimePeriod,
            GeographicSetting = bible.GeographicSetting,
            MainConflict = bible.MainConflict,
            KeyMysteriesAndTwists = bible.KeyMysteriesAndTwists,
            ClimaxResolution = bible.ClimaxResolution,
            OverallArcOutline = bible.OverallArcOutline,
            EpisodesSummaryJson = bible.EpisodesSummaryJson
        };
    }

    private async Task<FilmStoryBible> CallGeminiForStoryBibleAsync(FilmProject project, CancellationToken ct)
    {
        var episodes = Math.Clamp(project.TargetEpisodes, 1, 20);
        var minutes = Math.Max(1, project.TargetDurationPerEpisodeSeconds / 60);
        var system = "Bạn là AI Showrunner và Giám đốc Biên kịch chuyên các series phim truyền hình kịch tính, gay cấn triệu view tiếng Việt. Chỉ trả về JSON hợp lệ.";
        var user = $@"Phát triển ý tưởng phim sau thành một series gồm đúng {episodes} TẬP, mỗi tập khoảng {minutes} PHÚT.
YÊU CẦU ĐIỆN ẢNH CHUYÊN NGHIỆP:
1. MẠCH PHIM LIỀN MẠCH TUYỆT ĐỐI (SERIALIZED CONTINUITY):
   - {episodes} tập là MỘT BỘ PHIM LIÊN HOÀN. Tập sau BẮT BUỘC tiếp nối trực tiếp từ đúng khoảnh khắc kết thúc của tập trước, không nhảy cóc thời gian vô lý.
2. CLIFFHANGER KẾT THÚC TẬP 1 PHẢI CỰC KỲ GÂY CẤN & KHIẾN KHÁN GIẢ TỨC TỐI (HIGH-STAKES SUSPENSE):
   - Tập 1 là tập quyết định giữ chân người xem: Đoạn kết của Tập 1 PHẢI là một tình huống nghẹt thở, cú twist bàng hoàng, hoặc một hiểm hoạ sinh tử ập xuống khiến người xem 'tức', bất ngờ và BẮT BUỘC phải tìm ngay Tập 2 để xem tiếp!
3. TẬP 2 TIẾP NỐI NGAY LẬP TỨC:
   - Mở đầu Tập 2 (openingHook) PHẢI bắt đầu ngay tại hiện trường và giây phút của Cliffhanger Tập 1 để giải quyết sự việc nguy cấp đó trước khi mở ra biến cố mới.
4. MỌI TẬP TỪ TẬP 2 ĐẾN TẬP {episodes - 1}:
   - Mỗi tập đều có xung đột leo thang và kết thúc bằng EndingCliffhanger gay cấn. Riêng tập cuối ({episodes}) mới giải quyết trọn vẹn số phận nhân vật.
5. Phong cách hình ảnh: {project.VisualArtStyle}. Thể loại: {project.Genre}.
6. Tên nhân vật, tính cách, bối cảnh phải nhất quán 100% xuyên suốt toàn bộ các tập.

TÊN SERIES: {project.Title}
Ý TƯỞNG CỐT LÕI: {project.Logline}

Trả về JSON:
{{
  ""logline"": ""..."", ""genre"": ""{project.Genre}"", ""worldRules"": ""..."", ""timePeriod"": ""..."",
  ""geographicSetting"": ""..."", ""mainConflict"": ""..."", ""keyMysteriesAndTwists"": ""..."",
  ""climaxResolution"": ""..."", ""overallArcOutline"": ""..."",
  ""episodes"": [ {{ ""episodeNumber"": 1, ""title"": ""..."", ""synopsis"": ""5-8 câu diễn biến kịch tính cụ thể"", ""openingHook"": ""Mở đầu giật gân cuốn hút"", ""endingCliffhanger"": ""Khúc nghẹt thở cực điểm bắt buộc người xem xem tập kế"" }} ]
}}";

        var result = await _gemini.GenerateJsonAsync(system, user, 16384, 0.8, ct);
        await _costController.RecordCostAsync(project.Id, null, null, "Gemini", "ScriptLLM", result.Tokens, result.CostUsd, result.FreeKey, "Story Bible", ct);

        using var doc = ParseJson(result.Text, "Story Bible");
        var root = doc.RootElement;
        if (!root.TryGetProperty("episodes", out var eps) || eps.ValueKind != JsonValueKind.Array || eps.GetArrayLength() == 0)
            throw new InvalidOperationException("Gemini trả về Story Bible không có danh sách tập.");

        return new FilmStoryBible
        {
            FilmProjectId = project.Id,
            Logline = Str(root, "logline", project.Logline),
            Genre = Str(root, "genre", project.Genre),
            WorldRules = Str(root, "worldRules"),
            TimePeriod = Str(root, "timePeriod"),
            GeographicSetting = Str(root, "geographicSetting"),
            MainConflict = Str(root, "mainConflict"),
            KeyMysteriesAndTwists = Str(root, "keyMysteriesAndTwists"),
            ClimaxResolution = Str(root, "climaxResolution"),
            OverallArcOutline = Str(root, "overallArcOutline"),
            EpisodesSummaryJson = eps.GetRawText()
        };
    }

    private static List<FilmEpisode> ParseEpisodesFromJson(Guid projectId, string json, int targetSeconds)
    {
        var list = new List<FilmEpisode>();
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var num = item.TryGetProperty("episodeNumber", out var n) && n.TryGetInt32(out var parsed) ? parsed : list.Count + 1;
            list.Add(new FilmEpisode
            {
                FilmProjectId = projectId,
                EpisodeNumber = num,
                Title = Str(item, "title", $"Tập {num}"),
                Synopsis = Str(item, "synopsis"),
                OpeningHook = Str(item, "openingHook"),
                EndingCliffhanger = Str(item, "endingCliffhanger"),
                TargetDurationSeconds = targetSeconds > 0 ? targetSeconds : 300,
                Status = "Pending"
            });
        }
        return list;
    }

    // ─────────────────────────────── Cast & locations ───────────────────────────────

    /// <summary>True when the project still has the old hard-coded cast (or none) and must be cast from its own story.</summary>
    public static bool NeedsCasting(FilmProject project) =>
        project.Characters.Count == 0
        || project.Characters.Any(c => !GeminiFilmVoices.IsGeminiVoice(c.VoiceModel) || string.IsNullOrWhiteSpace(c.ConsistencyPrompt));

    public async Task<(List<FilmCharacterDto> Characters, List<FilmLocationDto> Locations)> GenerateCharactersAndLocationsAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await _context.FilmProjects
            .Include(p => p.StoryBible)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new ArgumentException("Không tìm thấy dự án phim.");
        var bible = project.StoryBible ?? throw new InvalidOperationException("Dự án chưa có Story Bible — chạy AI Showrunner trước.");

        _logger.LogInformation("AI Casting: creating characters and locations for Project {Id}", projectId);

        var system = "Bạn là đạo diễn casting và thiết kế bối cảnh cho phim truyền hình ngắn tiếng Việt. Chỉ trả về JSON hợp lệ.";
        var user = $@"Dựa trên Story Bible dưới đây, tạo hồ sơ NHÂN VẬT (3-6 nhân vật xuất hiện nhiều nhất, dùng đúng tên trong tóm tắt các tập) và BỐI CẢNH (4-8 địa điểm chính).

STORY BIBLE
Tên: {project.Title}
Logline: {bible.Logline}
Thể loại: {bible.Genre} — Phong cách hình ảnh: {project.VisualArtStyle}
Thời đại: {bible.TimePeriod}
Không gian: {bible.GeographicSetting}
Xung đột: {bible.MainConflict}
Tóm tắt các tập: {bible.EpisodesSummaryJson}

GIỌNG ĐỌC (chọn cho mỗi nhân vật 1 giọng KHÁC NHAU, hợp giới tính/tuổi/tính cách):
{GeminiFilmVoices.Describe()}

Trả về JSON:
{{
  ""characters"": [{{
    ""id"": ""char_<tên-không-dấu>"", ""name"": ""..."", ""role"": ""Protagonist|Antagonist|Supporting|Mentor"",
    ""age"": 27, ""gender"": ""Nam|Nữ"",
    ""appearance"": ""ngoại hình chi tiết (tiếng Việt)"", ""hairstyle"": ""..."", ""costume"": ""trang phục cố định"",
    ""personality"": ""..."", ""emotionalTrait"": ""..."",
    ""voice"": ""<tên giọng trong danh sách>"",
    ""visualPrompt"": ""ENGLISH description for an image model, fixed look used in every frame: ethnicity, age, face, hair, body, outfit. 30-50 words""
  }}],
  ""locations"": [{{
    ""id"": ""loc_<tên-không-dấu>"", ""name"": ""..."", ""architecture"": ""..."", ""lighting"": ""..."",
    ""timeOfDay"": ""Day|Night|Sunset|Dawn"", ""props"": ""...""
  }}]
}}";

        var result = await _gemini.GenerateJsonAsync(system, user, 12288, 0.7, ct);
        await _costController.RecordCostAsync(projectId, null, null, "Gemini", "ScriptLLM", result.Tokens, result.CostUsd, result.FreeKey, "Character & Location Bible", ct);

        using var doc = ParseJson(result.Text, "hồ sơ nhân vật");
        var root = doc.RootElement;

        var chars = new List<FilmCharacter>();
        var usedVoices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("characters", out var cs) && cs.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in cs.EnumerateArray())
            {
                var name = Str(c, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                var gender = Str(c, "gender", "Nam");
                var age = c.TryGetProperty("age", out var a) && a.TryGetInt32(out var ageVal) ? ageVal : 30;
                var voice = GeminiFilmVoices.Pick(Str(c, "voice"), gender, age, usedVoices);
                usedVoices.Add(voice);
                chars.Add(new FilmCharacter
                {
                    FilmProjectId = projectId,
                    CharacterId = UniqueId(Str(c, "id", $"char_{chars.Count + 1}"), chars.Select(x => x.CharacterId)),
                    Name = name,
                    Role = Str(c, "role", "Supporting"),
                    Age = age,
                    Gender = gender,
                    PhysicalAppearance = Str(c, "appearance"),
                    Hairstyle = Str(c, "hairstyle"),
                    DefaultCostume = Str(c, "costume"),
                    Personality = Str(c, "personality"),
                    EmotionalTrait = Str(c, "emotionalTrait"),
                    VoiceModel = GeminiFilmVoices.Prefix + voice,
                    ConsistencyPrompt = Str(c, "visualPrompt")
                });
            }
        }

        var locs = new List<FilmLocation>();
        if (root.TryGetProperty("locations", out var ls) && ls.ValueKind == JsonValueKind.Array)
        {
            foreach (var l in ls.EnumerateArray())
            {
                var name = Str(l, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                locs.Add(new FilmLocation
                {
                    FilmProjectId = projectId,
                    LocationId = UniqueId(Str(l, "id", $"loc_{locs.Count + 1}"), locs.Select(x => x.LocationId)),
                    Name = name,
                    ArchitectureStyle = Str(l, "architecture"),
                    LightingMood = Str(l, "lighting"),
                    TimeOfDay = Str(l, "timeOfDay", "Night"),
                    KeyProps = Str(l, "props")
                });
            }
        }

        if (chars.Count == 0 || chars.Any(c => string.IsNullOrWhiteSpace(c.ConsistencyPrompt)))
            throw new InvalidOperationException("Gemini trả về hồ sơ nhân vật thiếu thông tin ngoại hình — thử lại.");

        _context.FilmCharacters.RemoveRange(await _context.FilmCharacters.Where(c => c.FilmProjectId == projectId).ToListAsync(ct));
        _context.FilmLocations.RemoveRange(await _context.FilmLocations.Where(l => l.FilmProjectId == projectId).ToListAsync(ct));
        _context.FilmCharacters.AddRange(chars);
        _context.FilmLocations.AddRange(locs);
        await _context.SaveChangesAsync(ct);

        return (chars.Select(ToDto).ToList(), locs.Select(ToDto).ToList());
    }

    // ─────────────────────────────── Episode script ───────────────────────────────

    /// <summary>True when the episode has no script yet, or only the old template script.</summary>
    public static bool NeedsScript(FilmEpisode episode) =>
        episode.Scenes.Count == 0
        || episode.Scenes.SelectMany(s => s.Shots).Any(sh => sh.Provider != ShotProvider);

    public async Task<FilmEpisodeDetailDto> WriteEpisodeScriptAsync(Guid episodeId, CancellationToken ct = default)
    {
        var episode = await _context.FilmEpisodes
            .Include(e => e.FilmProject).ThenInclude(p => p!.StoryBible)
            .Include(e => e.FilmProject).ThenInclude(p => p!.Characters)
            .Include(e => e.FilmProject).ThenInclude(p => p!.Locations)
            .Include(e => e.FilmProject).ThenInclude(p => p!.Episodes)
            .FirstOrDefaultAsync(e => e.Id == episodeId, ct)
            ?? throw new ArgumentException("Không tìm thấy tập phim.");
        var project = episode.FilmProject!;

        _logger.LogInformation("AI Screenwriter: writing Episode {Num} of Project {Id}", episode.EpisodeNumber, project.Id);

        episode.Status = "Scripting";
        episode.ErrorMessage = null;
        episode.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        var target = episode.TargetDurationSeconds > 0 ? episode.TargetDurationSeconds : _configuration.GetValue("Film:TargetDurationSeconds", 150);
        // Natural Vietnamese delivery ≈ 2.6 words/second, plus short pauses between lines.
        var targetWords = (int)Math.Round(target * 2.3);
        var frames = FramesPerEpisode;
        var isFullMotion = _configuration.GetValue("Film:FullMotion", true);
        var motionPct = isFullMotion ? 100 : (int)Math.Round(Math.Clamp(_configuration.GetValue("Film:MotionShare", 0.35), 0, 1) * 100);
        var motionRule = isFullMotion
            ? "TẤT CẢ CÁC SHOT (100%) đều là clip video chuyển động (motion: true). Mỗi shot kéo dài từ 3 đến 5 giây, chuyển động mượt mà, diễn xuất và nét mặt tự nhiên, ánh mắt ổn định, tránh quay giật camera."
            : (_configuration.GetValue("Film:MotionFaces", false)
                ? "chọn shot có chuyển động cơ thể vừa phải (đi, quay đầu, nắm tay, ôm, cúi xuống), xe chạy, mưa, khói, gió; shot chỉ có lời dẫn hoặc cận mặt nói dài thì false. Tránh chuyển động mạnh (chạy nhanh, đánh nhau) vì dễ méo mặt."
                : "CHỈ chọn shot KHÔNG thấy rõ mặt người: cảnh vật, thời tiết (mưa, khói, gió), xe chạy, đồ vật cận cảnh (đồng hồ, tay cầm đồ vật), cảnh rộng người nhỏ ở xa, nhân vật quay lưng hoặc bóng dáng. Mọi shot thấy rõ mặt người (cận, trung cảnh) thì motion false — máy làm video hay làm méo mặt.");

        var cast = string.Join("\n", project.Characters.Select(c =>
            $"- id={c.CharacterId} | {c.Name} ({c.Gender}, {c.Age} tuổi, {c.Role}) | tính cách: {c.Personality} | ngoại hình (EN): {c.ConsistencyPrompt}"));
        var places = string.Join("\n", project.Locations.Select(l =>
            $"- id={l.LocationId} | {l.Name} | {l.ArchitectureStyle} | ánh sáng: {l.LightingMood} | {l.TimeOfDay} | đạo cụ: {l.KeyProps}"));
        var others = string.Join("\n", project.Episodes.OrderBy(e => e.EpisodeNumber)
            .Where(e => e.EpisodeNumber != episode.EpisodeNumber)
            .Select(e => $"- Tập {e.EpisodeNumber} \"{e.Title}\": {e.Synopsis} (kết: {e.EndingCliffhanger})"));

        var prevEp = project.Episodes.FirstOrDefault(e => e.EpisodeNumber == episode.EpisodeNumber - 1);
        var continuityBridge = prevEp != null
            ? $"\nCẦU NỐI CAO TRÀO BẮT BUỘC TỪ TẬP TRƯỚC (TẬP {prevEp.EpisodeNumber}):\n" +
              $"- Tập {prevEp.EpisodeNumber} kết thúc bằng CLIFFHANGER: \"{prevEp.EndingCliffhanger}\".\n" +
              $"- YÊU CẦU NỐI TIẾP: Scene 1, Shot 1 của Tập {episode.EpisodeNumber} BẮT BUỘC PHẢI BẮT ĐẦU NGAY TẠI HIỆN TRƯỜNG VÀ ĐÚNG KHOẢNH KHẮC CAO TRÀO ĐÓ! Các nhân vật phản ứng tức thì với tình huống nghẹt thở của cliffhanger trước, không nhảy cóc thời gian, tạo cảm giác mạch phim liên tục 100%.\n"
            : "";

        // With lip sync on, a line is mouth-synced only when its speaker is the one visible face (FilmShotPlanner),
        // so the script must frame dialogue that way (shot / reverse shot) and list only visible faces.
        var lipSyncRules = _configuration.GetValue("Film:LipSync:Enabled", false)
            ? "- KHẨU HÌNH: \"characters\" của frame CHỈ liệt kê nhân vật NHÌN THẤY MẶT (người quay lưng / chỉ thấy vai thì không ghi).\n" +
              "- Câu thoại nói trước máy: dùng frame CloseUp hoặc MediumShot mà người nói là người DUY NHẤT thấy mặt (quay kiểu shot / reverse-shot); người nghe phản ứng ở shot riêng với speaker \"none\" hoặc lời của người kia vọng vào.\n" +
              "- \"motionPrompt\" của shot có thoại chỉ tả cử động nhỏ, có chủ đích, khớp cảm xúc câu nói (gật nhẹ, nghiêng đầu, ánh mắt, cử chỉ tay đơn giản); không quay đầu gắt, không đổi tư thế đột ngột, không nhiều hành động cùng lúc.\n"
            : "";
        var system = "Bạn là biên kịch kiêm đạo diễn hình ảnh cho phim ngắn tiếng Việt dạng 'ảnh điện ảnh chuyển động + lồng tiếng'. Chỉ trả về JSON hợp lệ.";
        var filmingStyle = isFullMotion
            ? $"MỌI SHOT (100%) đều là CLIP CHUYỂN ĐỘNG THẬT 3-5 giây (Wan 2.2), KHÔNG DÙNG ẢNH TĨNH. Mỗi shot đi kèm chuyển động tự nhiên và 1 câu thoại lồng tiếng tương ứng."
            : $"mỗi \"frame\" là MỘT ảnh do AI vẽ; mỗi \"shot\" dùng lại 1 frame với 1 chuyển động máy (zoom/pan/tilt) và 1 câu thoại được lồng tiếng; khoảng {motionPct}% shot được AI biến ảnh thành CLIP CHUYỂN ĐỘNG thật 4-5 giây.";

        var user = $@"Viết kịch bản quay chi tiết cho TẬP {episode.EpisodeNumber} của series ""{project.Title}"".

STORY BIBLE: {project.StoryBible?.Logline}
Thế giới: {project.StoryBible?.WorldRules}
Thời đại / không gian: {project.StoryBible?.TimePeriod} — {project.StoryBible?.GeographicSetting}
Phong cách hình ảnh: {project.VisualArtStyle}, khung hình dọc {project.AspectRatio}.

NHÂN VẬT (chỉ dùng các id này):
{cast}

BỐI CẢNH (chỉ dùng các id này):
{places}

CÁC TẬP KHÁC (để giữ liên tục):
{others}

TẬP CẦN VIẾT — Tập {episode.EpisodeNumber}: {episode.Title}
Diễn biến: {episode.Synopsis}
Mở đầu: {episode.OpeningHook}
Kết thúc: {episode.EndingCliffhanger}
{continuityBridge}
CÁCH PHIM ĐƯỢC DỰNG: {filmingStyle} Vì vậy:
- Tổng cộng ĐÚNG {frames} frame cho cả tập (mỗi cảnh 1-3 frame). Mỗi frame được 2-4 shot dùng lại, các shot liền nhau dùng chung frame nên đổi camera.
- 26-40 shot. Tổng số chữ của tất cả ""line"" khoảng {targetWords} từ (để tập dài ~{target / 60} phút).
- ""speaker"": id nhân vật đang nói, hoặc ""narrator"" (lời dẫn/độc thoại nội tâm, tối đa 30% số câu), hoặc ""none"" (khoảnh khắc im lặng, tối đa 3 shot).
- ""line"": câu nói tiếng Việt tự nhiên như phim, tối đa 30 từ, KHÔNG ghi tên người nói, KHÔNG ghi chỉ dẫn diễn xuất trong ngoặc, không emoji.
- Frame ""prompt"" viết bằng TIẾNG ANH, tối đa 70 từ, tự đủ nghĩa (máy vẽ không biết gì ngoài câu này): bắt đầu bằng cỡ cảnh + nhân vật chính, rồi hành động, biểu cảm, bối cảnh, ánh sáng, thời điểm; mô tả ngắn ngoại hình nhân vật có mặt (tuổi, giới tính, tóc, trang phục theo mô tả EN ở trên), KHÔNG dùng tên riêng; KHÔNG thêm từ phong cách (hệ thống tự thêm). Ghi rõ thời điểm và ánh sáng ngay đầu prompt (vd: ""night, dark street lit by red neon""). Ít nhất 80% frame chỉ có 1 nhân vật nhìn thấy mặt (máy vẽ hay trộn mặt khi có 2 người); cảnh hai người dùng góc qua vai: một người quay lưng chỉ thấy vai và tóc, người kia nhìn rõ mặt.
- ""motion"": true cho khoảng {motionPct}% số shot — {motionRule}
- Khi ""motion"" true: thêm ""motionPrompt"" TIẾNG ANH tối đa 35 từ mô tả chuyển động của nhân vật và máy quay trong 4-5 giây, không thêm người/vật mới; và ""line"" của shot đó tối đa 12 từ (hoặc speaker ""none"").
{lipSyncRules}- Nội dung bám đúng diễn biến tập:
  + Mở đầu bằng phần Mở đầu { (prevEp != null ? "(nối tiếp ngay cliffhanger tập trước)" : "") }
  + KẾT THÚC TẬP (CLIFFHANGER NGHẸT THỞ): Shot cuối cùng của Cảnh cuối cùng BẮT BUỘC dừng lại đúng ở: '{episode.EndingCliffhanger}'. Tạo một hình ảnh dừng lại đầy ám ảnh, một cú twist, tiếng động bất ngờ hoặc mối nguy hiểm ập tới giật gân người xem!
  + Không bạo lực máu me chi tiết.
- ""camera"": một trong {string.Join(", ", CameraMoves)}.

Trả về JSON:
{{
  ""scenes"": [{{
    ""sceneNumber"": 1, ""locationId"": ""loc_..."", ""timeOfDay"": ""Night"", ""purpose"": ""..."", ""summary"": ""..."",
    ""frames"": [{{ ""frameId"": ""f1"", ""characters"": [""char_...""], ""shotType"": ""WideShot|MediumShot|CloseUp|OverTheShoulder"", ""prompt"": ""..."" }}],
    ""shots"": [{{ ""frameId"": ""f1"", ""camera"": ""SlowZoomIn"", ""speaker"": ""char_..."", ""line"": ""..."", ""emotion"": ""..."", ""acting"": ""..."", ""motion"": false, ""motionPrompt"": """" }}]
  }}]
}}";

        FilmGeminiClient.TextResult result;
        try
        {
            result = await _gemini.GenerateJsonAsync(system, user, 32768, 0.8, ct);
        }
        catch (FilmGeminiClient.SafetyBlockedException ex)
        {
            // Dark stories (danger, blood, a teenage character) sometimes trip Gemini's safety filter.
            // Ask once more for the same plot told more indirectly; a second refusal is reported as is.
            _logger.LogWarning("Episode {Num} script blocked by Gemini safety ({Reason}); retrying with safer wording", episode.EpisodeNumber, ex.Reason);
            user += "\n\nLƯU Ý AN TOÀN NỘI DUNG (bắt buộc): kể các tình tiết nguy hiểm một cách gián tiếp, KHÔNG mô tả máu, vết thương, cái chết hay bạo lực trực diện; " +
                    "nhân vật dưới 18 tuổi chỉ xuất hiện trong khung cảnh an toàn, ấm áp, không bị đe doạ hay làm hại trên màn hình; giữ nguyên mạch truyện và cảm xúc kịch tính.";
            result = await _gemini.GenerateJsonAsync(system, user, 32768, 0.8, ct);
        }
        await _costController.RecordCostAsync(project.Id, episode.Id, null, "Gemini", "ScriptLLM", result.Tokens, result.CostUsd, result.FreeKey,
            $"Kịch bản tập {episode.EpisodeNumber}", ct);

        var scenes = ParseEpisodeScript(result.Text, episode, project);

        var oldScenes = await _context.FilmScenes.Include(s => s.Shots).Where(s => s.FilmEpisodeId == episodeId).ToListAsync(ct);
        if (oldScenes.Count > 0)
        {
            var oldShots = oldScenes.SelectMany(s => s.Shots).ToList();
            var oldShotIds = oldShots.Select(s => s.Id).ToList();
            if (oldShotIds.Count > 0)
            {
                var linkedCosts = await _context.FilmCostRecords
                    .Where(c => c.FilmShotId != null && oldShotIds.Contains(c.FilmShotId.Value)).ToListAsync(ct);
                foreach (var rec in linkedCosts) rec.FilmShotId = null;
                await _context.SaveChangesAsync(ct);

                _context.FilmShots.RemoveRange(oldShots);
                await _context.SaveChangesAsync(ct);
            }

            _context.FilmScenes.RemoveRange(oldScenes);
            await _context.SaveChangesAsync(ct);
        }

        _context.FilmScenes.AddRange(scenes);

        episode.TargetDurationSeconds = target;
        episode.OutputVideoPath = null;
        episode.Status = "ScriptReady";
        episode.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Script ready for Episode {Num}: {Scenes} scenes, {Shots} shots, {Frames} frames",
            episode.EpisodeNumber, scenes.Count, scenes.Sum(s => s.Shots.Count),
            scenes.SelectMany(s => s.Shots).Select(s => s.VisualPrompt).Distinct().Count());

        return (await GetEpisodeDetailInternalAsync(episodeId, ct))!;
    }

    private List<FilmScene> ParseEpisodeScript(string json, FilmEpisode episode, FilmProject project)
    {
        using var doc = ParseJson(json, "kịch bản tập");
        if (!doc.RootElement.TryGetProperty("scenes", out var scenesEl) || scenesEl.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Gemini trả về kịch bản không có danh sách cảnh.");

        var characterIds = project.Characters.Select(c => c.CharacterId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var locations = project.Locations.ToDictionary(l => l.LocationId, StringComparer.OrdinalIgnoreCase);
        var scenes = new List<FilmScene>();

        foreach (var sc in scenesEl.EnumerateArray())
        {
            var locId = Str(sc, "locationId");
            locations.TryGetValue(locId, out var loc);
            var scene = new FilmScene
            {
                FilmEpisodeId = episode.Id,
                SceneNumber = scenes.Count + 1,
                LocationId = locId,
                LocationName = loc?.Name ?? locId,
                ScenePurpose = Str(sc, "purpose"),
                TimeOfDay = Str(sc, "timeOfDay", loc?.TimeOfDay ?? "Day"),
                NarrativeSummary = Str(sc, "summary")
            };

            var isFullMotion = _configuration.GetValue("Film:FullMotion", true);
            var frames = new Dictionary<string, (string Prompt, string ShotType, string Characters)>(StringComparer.OrdinalIgnoreCase);
            if (sc.TryGetProperty("frames", out var fs) && fs.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in fs.EnumerateArray())
                {
                    var prompt = Str(f, "prompt");
                    if (string.IsNullOrWhiteSpace(prompt)) continue;

                    // Anchor location consistency: inject architectural style, props and lighting
                    if (loc != null && !prompt.Contains(loc.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        var locAnchor = $"in {loc.Name}, architectural style: {loc.ArchitectureStyle}, lighting: {loc.LightingMood}, key interior elements: {loc.KeyProps}, maintaining consistent room layout and architectural details";
                        prompt = $"{prompt}, {locAnchor}";
                    }

                    var ids = f.TryGetProperty("characters", out var ce) && ce.ValueKind == JsonValueKind.Array
                        ? ce.EnumerateArray().Select(x => x.GetString() ?? "").Where(characterIds.Contains)
                        : Enumerable.Empty<string>();
                    frames[Str(f, "frameId", $"f{frames.Count + 1}")] = (prompt, Str(f, "shotType", "MediumShot"), string.Join(",", ids));
                }
            }
            if (frames.Count == 0) continue;
            var firstFrame = frames.First().Value;

            if (sc.TryGetProperty("shots", out var shotsEl) && shotsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in shotsEl.EnumerateArray())
                {
                    var frame = frames.TryGetValue(Str(s, "frameId"), out var fr) ? fr : firstFrame;
                    var speaker = Str(s, "speaker", "none");
                    var line = CleanLine(Str(s, "line"));
                    if (speaker.Equals("none", StringComparison.OrdinalIgnoreCase) || line.Length == 0)
                    {
                        speaker = string.Empty;
                        line = string.Empty;
                    }
                    else if (!characterIds.Contains(speaker))
                    {
                        speaker = NarratorId;
                    }

                    var camera = CameraMoves.FirstOrDefault(m => m.Equals(Str(s, "camera"), StringComparison.OrdinalIgnoreCase)) ?? "SlowZoomIn";
                    var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    var useMotion = isFullMotion || (s.TryGetProperty("motion", out var mv) && mv.ValueKind == JsonValueKind.True);
                    var motionPrompt = Str(s, "motionPrompt");
                    if (string.IsNullOrWhiteSpace(motionPrompt) && useMotion)
                    {
                        var acting = Str(s, "acting", "subtle natural movement");
                        motionPrompt = $"{acting}, smooth cinematic camera movement, stable natural face and eyes, photorealistic";
                    }

                    scene.Shots.Add(new FilmShot
                    {
                        FilmSceneId = scene.Id,
                        FilmEpisodeId = episode.Id,
                        ShotNumber = scene.Shots.Count + 1,
                        ShotType = frame.ShotType,
                        CameraMovement = camera,
                        VisualMethod = "CinematicMotionImage",
                        VisualPrompt = frame.Prompt,
                        CharacterIdsPresent = frame.Characters,
                        SpeakerCharacterId = speaker.Length > 0 ? speaker : null,
                        DialogueText = line.Length > 0 ? line : null,
                        ActingInstructions = Str(s, "acting"),
                        EmotionalTone = Str(s, "emotion"),
                        // Estimate only — the real length comes from the recorded voice.
                        DurationSeconds = words > 0 ? Math.Round(words / 2.6 + 0.5, 1) : 3.0,
                        Provider = ShotProvider,
                        UseMotion = useMotion,
                        MotionPrompt = motionPrompt,
                        RenderStatus = "Pending"
                    });
                }
            }

            if (scene.Shots.Count == 0) continue;
            scene.EstimatedDurationSeconds = scene.Shots.Sum(x => x.DurationSeconds);
            scene.CharactersPresentJson = JsonSerializer.Serialize(scene.Shots
                .SelectMany(x => (x.CharacterIdsPresent ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)).Distinct());
            scenes.Add(scene);
        }

        var shotCount = scenes.Sum(s => s.Shots.Count);
        if (shotCount < 8 || scenes.SelectMany(s => s.Shots).Count(s => s.DialogueText != null) < 5)
            throw new InvalidOperationException($"Kịch bản Gemini trả về quá ngắn ({shotCount} shot) — thử viết lại tập này.");
        return scenes;
    }

    /// <summary>Removes speaker labels, stage directions and quotes the model sometimes adds to a line.</summary>
    private static string CleanLine(string line)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(line, @"[\(\[].*?[\)\]]", " ");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"^\s*[\p{L} ]{1,25}:\s+", "");
        t = t.Replace("\"", "").Replace("“", "").Replace("”", "");
        return System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Trim();
    }

    // ─────────────────────────────── Continuity ───────────────────────────────

    public async Task<bool> CheckContinuityAsync(Guid episodeId, CancellationToken ct = default)
    {
        var episode = await _context.FilmEpisodes
            .Include(e => e.FilmProject).ThenInclude(p => p!.Characters)
            .Include(e => e.Scenes).ThenInclude(s => s.Shots)
            .FirstOrDefaultAsync(e => e.Id == episodeId, ct);
        if (episode == null) return false;

        // Factual checks only: every speaker and on-screen character must exist in the character bible.
        var known = episode.FilmProject!.Characters.Select(c => c.CharacterId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        known.Add(NarratorId);
        var shots = episode.Scenes.SelectMany(s => s.Shots).ToList();
        var unknown = shots
            .SelectMany(s => (s.CharacterIdsPresent ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Append(s.SpeakerCharacterId ?? NarratorId))
            .Where(id => !known.Contains(id))
            .Distinct()
            .ToList();

        _context.FilmContinuityRecords.Add(new FilmContinuityRecord
        {
            FilmProjectId = episode.FilmProjectId,
            EpisodeNumber = episode.EpisodeNumber,
            SceneNumber = 0,
            KeyDetailsJson = JsonSerializer.Serialize(new
            {
                Episode = episode.EpisodeNumber,
                Scenes = episode.Scenes.Count,
                Shots = shots.Count,
                Frames = shots.Select(s => s.VisualPrompt).Distinct().Count(),
                SpokenLines = shots.Count(s => s.DialogueText != null)
            }),
            PotentialConflicts = unknown.Count > 0 ? "Nhân vật không có trong hồ sơ: " + string.Join(", ", unknown) : null,
            IsResolved = unknown.Count == 0
        });
        await _context.SaveChangesAsync(ct);
        return unknown.Count == 0;
    }

    // ─────────────────────────────── Helpers ───────────────────────────────

    private async Task<FilmEpisodeDetailDto?> GetEpisodeDetailInternalAsync(Guid episodeId, CancellationToken ct)
    {
        var e = await _context.FilmEpisodes
            .Include(x => x.Scenes).ThenInclude(s => s.Shots)
            .FirstOrDefaultAsync(x => x.Id == episodeId, ct);
        if (e == null) return null;

        return new FilmEpisodeDetailDto
        {
            Id = e.Id,
            FilmProjectId = e.FilmProjectId,
            EpisodeNumber = e.EpisodeNumber,
            Title = e.Title,
            Synopsis = e.Synopsis,
            OpeningHook = e.OpeningHook,
            EndingCliffhanger = e.EndingCliffhanger,
            TargetDurationSeconds = e.TargetDurationSeconds,
            ActualDurationSeconds = e.ActualDurationSeconds,
            Status = e.Status,
            OutputVideoPath = e.OutputVideoPath,
            ThumbnailPath = e.ThumbnailPath,
            EstimatedCostUsd = e.EstimatedCostUsd,
            ActualCostUsd = e.ActualCostUsd,
            QualityScore = e.QualityScore,
            ErrorMessage = e.ErrorMessage,
            ScenesCount = e.Scenes.Count,
            ShotsCount = e.Scenes.SelectMany(s => s.Shots).Count(),
            Scenes = e.Scenes.OrderBy(s => s.SceneNumber).Select(s => new FilmSceneDto
            {
                Id = s.Id,
                FilmEpisodeId = s.FilmEpisodeId,
                SceneNumber = s.SceneNumber,
                LocationId = s.LocationId,
                LocationName = s.LocationName,
                ScenePurpose = s.ScenePurpose,
                TimeOfDay = s.TimeOfDay,
                NarrativeSummary = s.NarrativeSummary,
                EstimatedDurationSeconds = s.EstimatedDurationSeconds,
                Shots = s.Shots.OrderBy(sh => sh.ShotNumber).Select(sh => new FilmShotDto
                {
                    Id = sh.Id,
                    FilmSceneId = sh.FilmSceneId,
                    FilmEpisodeId = sh.FilmEpisodeId,
                    ShotNumber = sh.ShotNumber,
                    ShotType = sh.ShotType,
                    CameraMovement = sh.CameraMovement,
                    VisualMethod = sh.VisualMethod,
                    VisualPrompt = sh.VisualPrompt,
                    SpeakerCharacterId = sh.SpeakerCharacterId,
                    DialogueText = sh.DialogueText,
                    ActingInstructions = sh.ActingInstructions,
                    EmotionalTone = sh.EmotionalTone,
                    DurationSeconds = sh.DurationSeconds,
                    Provider = sh.Provider,
                    ActualCostUsd = sh.ActualCostUsd,
                    RenderStatus = sh.RenderStatus,
                    RenderedShotVideoPath = sh.RenderedShotVideoPath,
                    RawImagePath = sh.RawImagePath
                }).ToList()
            }).ToList()
        };
    }

    private static FilmCharacterDto ToDto(FilmCharacter c) => new()
    {
        Id = c.Id,
        FilmProjectId = c.FilmProjectId,
        CharacterId = c.CharacterId,
        Name = c.Name,
        Role = c.Role,
        Age = c.Age,
        Gender = c.Gender,
        PhysicalAppearance = c.PhysicalAppearance,
        Hairstyle = c.Hairstyle,
        DefaultCostume = c.DefaultCostume,
        Personality = c.Personality,
        EmotionalTrait = c.EmotionalTrait,
        VoiceModel = c.VoiceModel,
        ConsistencyPrompt = c.ConsistencyPrompt,
        FrontReferenceImageUrl = c.FrontReferenceImageUrl,
        SideReferenceImageUrl = c.SideReferenceImageUrl,
        FullBodyReferenceImageUrl = c.FullBodyReferenceImageUrl
    };

    private static FilmLocationDto ToDto(FilmLocation l) => new()
    {
        Id = l.Id,
        FilmProjectId = l.FilmProjectId,
        LocationId = l.LocationId,
        Name = l.Name,
        ArchitectureStyle = l.ArchitectureStyle,
        LightingMood = l.LightingMood,
        TimeOfDay = l.TimeOfDay,
        KeyProps = l.KeyProps,
        ReferenceImageUrl = l.ReferenceImageUrl
    };

    private static JsonDocument ParseJson(string text, string what)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Gemini trả về {what} không phải JSON hợp lệ: {ex.Message}");
        }
    }

    private static string Str(JsonElement el, string name, string fallback = "")
    {
        if (!el.TryGetProperty(name, out var v)) return fallback;
        var s = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Array => string.Join("\n", v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.GetRawText())),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => v.GetRawText()
        };
        return string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();
    }

    private static string UniqueId(string id, IEnumerable<string> existing)
    {
        var taken = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = id;
        for (var i = 2; taken.Contains(candidate); i++) candidate = $"{id}_{i}";
        return candidate;
    }
}

/// <summary>Gemini prebuilt TTS voices offered to film characters.</summary>
public static class GeminiFilmVoices
{
    public const string Prefix = "gemini:";

    private static readonly (string Name, string Gender, string Trait)[] Voices =
    {
        ("Orus", "Nam", "chắc, kiên định — nam chính trẻ"),
        ("Iapetus", "Nam", "rõ ràng, trẻ trung"),
        ("Puck", "Nam", "vui tươi, lanh lợi"),
        ("Fenrir", "Nam", "hào hứng, nóng nảy"),
        ("Charon", "Nam", "trầm, điềm tĩnh — người dẫn chuyện / trung niên"),
        ("Algenib", "Nam", "khàn, gai góc — phản diện / lớn tuổi"),
        ("Enceladus", "Nam", "thì thầm, bí ẩn"),
        ("Umbriel", "Nam", "dễ chịu, ấm"),
        ("Kore", "Nữ", "chắc, tự tin — nữ chính"),
        ("Leda", "Nữ", "trẻ, trong trẻo — thiếu nữ / em gái"),
        ("Aoede", "Nữ", "nhẹ nhàng, thoáng"),
        ("Zephyr", "Nữ", "tươi sáng"),
        ("Sulafat", "Nữ", "ấm áp — người mẹ"),
        ("Gacrux", "Nữ", "trưởng thành, từng trải"),
        ("Despina", "Nữ", "mượt, quyến rũ")
    };

    public static bool IsGeminiVoice(string? voice) =>
        !string.IsNullOrWhiteSpace(voice) && voice.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static string Describe() => string.Join("\n", Voices.Select(v => $"- {v.Name} ({v.Gender}): {v.Trait}"));

    /// <summary>The model's choice when it is a known, unused voice of the right gender; otherwise the first free match.</summary>
    public static string Pick(string requested, string gender, int age, ISet<string> used)
    {
        var gl = gender.Normalize(NormalizationForm.FormC).Trim().ToLowerInvariant();
        var female = gl.Contains('ữ') || gl == "nu" || gl.StartsWith("f") || gl.Contains("woman");
        var g = female ? "Nữ" : "Nam";
        var match = Voices.FirstOrDefault(v => v.Name.Equals(requested?.Replace(Prefix, ""), StringComparison.OrdinalIgnoreCase));
        if (match.Name != null && match.Gender == g && !used.Contains(match.Name)) return match.Name;

        var pool = Voices.Where(v => v.Gender == g && !used.Contains(v.Name)).Select(v => v.Name).ToList();
        if (age >= 45 && g == "Nam") pool = pool.OrderByDescending(n => n is "Algenib" or "Charon").ToList();
        if (age >= 45 && g == "Nữ") pool = pool.OrderByDescending(n => n is "Gacrux" or "Sulafat").ToList();
        if (age <= 18 && g == "Nữ") pool = pool.OrderByDescending(n => n is "Leda").ToList();
        return pool.FirstOrDefault() ?? (female ? "Kore" : "Orus");
    }

    /// <summary>A narrator voice no character uses.</summary>
    public static string Narrator(IEnumerable<string?> characterVoices)
    {
        var used = characterVoices.Where(v => v != null).Select(v => v!.Replace(Prefix, "")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "Charon", "Umbriel", "Iapetus", "Gacrux", "Aoede" })
            if (!used.Contains(name)) return Prefix + name;
        return Prefix + "Charon";
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Infrastructure.AiStudio;

/// <summary>
/// Narration: what the reference says in each scene (recognised on the PC) goes into that scene's prompt as story
/// context, and the whole narration is read again IN ONE TAKE by a Gemini TTS narrator chosen to sound like the
/// reference speaker — one continuous reading flows across the cuts like the reference's voice-over (scene-by-scene
/// takes stopped at every cut and broke sentences). The take is a small paid call written to the studio's cost
/// records under the same monthly budget; the same words, voice and style are never paid for twice.
/// </summary>
public partial class AiStudioService
{
    /// <summary>Spreads the recognised speech over the scenes (each line to one scene) — replaces any edited text.</summary>
    private static void FillNarration(AiStudioProject p, IReadOnlyList<AiStudioScene> scenes)
    {
        var lines = AiStudioNarration.Lines(p.TranscriptJson);
        var ordered = scenes.OrderBy(s => s.StartSeconds).ToList();
        var assigned = AiStudioNarration.Assign(lines, ordered.Select(s => (s.StartSeconds, s.EndSeconds)).ToList());
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Narration = assigned[i].Text;
            ordered[i].NarrationEn = assigned[i].En;
            ordered[i].NarrationEdited = false;
        }
    }

    /// <summary>The narrator voice and direction in use (suggested from the reference speaker when not chosen yet).</summary>
    private static (string Voice, string Style) NarratorOf(AiStudioProject p)
    {
        var suggested = AiStudioNarration.Suggest(AiStudioNarration.SpeakerOf(p.TranscriptJson), p.Topic);
        return (AiStudioNarration.IsVoice(p.NarratorVoice) ? p.NarratorVoice : suggested.Voice,
            string.IsNullOrWhiteSpace(p.NarratorStyle) || AiStudioNarration.IsOldSuggestion(p.NarratorStyle) ? suggested.Style : p.NarratorStyle.Trim());
    }

    /// <summary>
    /// Voice + direction like the reference speaker. The channel's own narrator (the default designed voice of the
    /// studio's "Giọng đọc" tab) wins over a prebuilt voice when it is of the speaker's gender.
    /// </summary>
    private async Task SuggestNarratorAsync(AiStudioProject p, CancellationToken ct)
    {
        var speaker = AiStudioNarration.SpeakerOf(p.TranscriptJson);
        var (voice, style) = AiStudioNarration.Suggest(speaker, p.Topic);
        var gender = AiStudioNarration.GenderOf(speaker) ?? StudioVoices.All.FirstOrDefault(v => v.Id == voice)?.Gender;
        var preferred = await _db.SystemSettings.AsNoTracking().Where(x => x.SettingKey == StudioService.DefaultNarratorKey)
            .Select(x => x.SettingValue).FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(preferred) && (await _studio.ReadDesignedVoicesAsync(ct)).FirstOrDefault(d => d.Id == preferred) is { } own
            && own.Gender == gender)
            voice = own.Id;
        p.NarratorVoice = voice;
        p.NarratorStyle = style;
    }

    /// <summary>The voices offered: the channel's designed voices first, then the prebuilt ones.</summary>
    public async Task<List<StudioVoiceDto>> NarratorVoicesAsync(CancellationToken ct) =>
        (await _studio.ReadDesignedVoicesAsync(ct)).Select(d => new StudioVoiceDto
            {
                Id = d.Id, Name = d.Name, Gender = d.Gender, Designed = true,
                Description = "Giọng thiết kế — " + (d.Description.Length > 70 ? d.Description[..70] + "…" : d.Description)
            })
            .Concat(StudioVoices.Dtos()).ToList();

    /// <summary>Every enabled scene's words in order, as one text (a sentence split over a cut is whole again).</summary>
    internal static string? FullNarration(IEnumerable<AiStudioScene> scenes)
    {
        var parts = scenes.Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.Narration)).OrderBy(s => s.Order)
            .Select(s => s.Narration!.Trim()).ToList();
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static string? NarrationKeyOf(AiStudioProject p, IEnumerable<AiStudioScene> scenes)
    {
        var text = FullNarration(scenes);
        if (text == null) return null;
        var (voice, style) = NarratorOf(p);
        return AiStudioNarration.Key(text, voice, style);
    }

    public async Task<AiStudioProjectDto> ResetNarrationAsync(Guid id, CancellationToken ct)
    {
        var p = await EditableProjectAsync(id, ct);
        var scenes = await _db.AiStudioScenes.Where(s => s.ProjectId == id).ToListAsync(ct);
        FillNarration(p, scenes);
        await _db.SaveChangesAsync(ct);
        await RebuildPromptsAsync(p, false, "engine", ct);
        return await GetProjectAsync(id, ct);
    }

    public async Task<AiStudioSceneDto> EditNarrationAsync(Guid projectId, Guid sceneId, string? text, CancellationToken ct)
    {
        var (p, s) = await SceneAsync(projectId, sceneId, ct);
        text = text?.Trim();
        if (text is { Length: > 2000 }) throw new ArgumentException("Lời bình quá dài (tối đa 2000 ký tự).");
        if (text == (s.Narration ?? "")) return await SceneDtoAsync(p, s, ct);
        s.Narration = string.IsNullOrEmpty(text) ? null : text;
        s.NarrationEn = null; // the prompt quotes the user's own words from now on
        s.NarrationEdited = true;
        if (!s.PromptEdited)
        {
            var earlier = await _db.AiStudioScenes.AnyAsync(x => x.ProjectId == projectId && x.Enabled && x.Order < s.Order, ct);
            await SetScenePromptAsync(p, s, AiStudioPromptEngine.Build(PromptInput(p, s, earlier)), "engine", edited: false, ct);
        }
        await _db.SaveChangesAsync(ct);
        return await SceneDtoAsync(p, s, ct);
    }

    public async Task<AiStudioProjectDto> SetNarratorAsync(Guid id, AiStudioNarratorDto input, CancellationToken ct)
    {
        var p = await EditableProjectAsync(id, ct);
        if (input.Suggest) await SuggestNarratorAsync(p, ct);
        if (input.Voice != null)
        {
            if (!(await NarratorVoicesAsync(ct)).Any(v => v.Id == input.Voice)) throw new ArgumentException("Giọng đọc không có trong danh sách.");
            p.NarratorVoice = input.Voice;
        }
        if (input.Style != null)
        {
            var style = input.Style.Trim();
            if (style.Length > 600) throw new ArgumentException("Cách đọc quá dài (tối đa 600 ký tự).");
            p.NarratorStyle = style;
        }
        await _db.SaveChangesAsync(ct);
        return await GetProjectAsync(id, ct);
    }

    /// <summary>Reads the whole narration with the narrator voice in one continuous take (paid, tiny).</summary>
    public async Task<AiStudioProjectDto> SpeakNarrationAsync(Guid id, CancellationToken ct)
    {
        var p = await EditableProjectAsync(id, ct);
        var scenes = await _db.AiStudioScenes.AsNoTracking().Where(s => s.ProjectId == id).ToListAsync(ct);
        var text = FullNarration(scenes) ?? throw new ArgumentException("Chưa có lời bình nào (các cảnh đang bật đều trống).");
        if (text.Length > AiStudioNarration.MaxNarrationChars)
            throw new ArgumentException($"Lời bình quá dài ({text.Length} ký tự, tối đa {AiStudioNarration.MaxNarrationChars}) — bớt chữ ở vài cảnh rồi đọc lại.");
        var (voice, style) = NarratorOf(p);
        var key = AiStudioNarration.Key(text, voice, style);
        if (p.NarrationKey == key && p.NarrationBlobId != null) return await GetProjectAsync(id, ct); // already read this way

        // a designed voice deleted from the studio since: the voice suggested for this speaker instead
        var designed = await _studio.ReadDesignedVoicesAsync(ct);
        var speakVoice = voice;
        if (StudioVoices.IsDesigned(voice) && designed.All(d => d.Id != voice))
            speakVoice = AiStudioNarration.Suggest(AiStudioNarration.SpeakerOf(p.TranscriptJson), p.Topic).Voice;
        var gender = designed.FirstOrDefault(d => d.Id == speakVoice)?.Gender ?? StudioVoices.All.FirstOrDefault(v => v.Id == speakVoice)?.Gender;

        // a take of these words at most: ~2 syllables a second + the tail
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        await _studio.EnsureMonthlyBudgetAsync(Math.Round((decimal)(words / 2.0 + 2) * _voices.PricePerSecond, 5), ct);
        var (audio, seconds, fromCache, delivery) = await _voices.SpeakAsync(p.Id, text, speakVoice, style, StudioVoices.FallbackFor(gender), ct);
        if (delivery != null && delivery != "diễn cảm") _logger.LogWarning("AI Studio narration {Project}: {Delivery}", p.Id, delivery);
        _db.StudioCostRecords.Add(new StudioCostRecord
        {
            Kind = "Voice", Model = speakVoice, Seconds = seconds, CostUsd = fromCache ? 0m : Math.Round((decimal)seconds * _voices.PricePerSecond, 5),
            Note = $"AI Video Studio · {Short(p.Title)} · lời bình cả video" + (fromCache ? " (dùng lại, miễn phí)" : "")
        });

        var temp = TempDir();
        try
        {
            var path = Path.Combine(temp, "narration.m4a");
            await File.WriteAllBytesAsync(path, audio, ct);
            var old = p.NarrationBlobId;
            p.NarrationBlobId = (await StoreFileAsync(path, "audio/mp4", ct)).Id;
            await DeleteBlobAsync(old, ct);
        }
        finally { DeleteDir(temp); }
        p.NarrationSeconds = seconds;
        p.NarrationKey = key;
        await _db.SaveChangesAsync(ct);
        return await GetProjectAsync(id, ct);
    }
}

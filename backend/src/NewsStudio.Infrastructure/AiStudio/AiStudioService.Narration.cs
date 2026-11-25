using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Infrastructure.AiStudio;

/// <summary>
/// Narration: what the reference says in each scene (recognised on the PC) goes into that scene's prompt as story
/// context and is read again by a Gemini TTS narrator chosen to sound like the reference speaker. A take fits its
/// clip: the clip is lengthened to the take (up to one clip), the take is played a little faster when the clip is
/// shorter. Each take is a small paid call written to the studio's cost records under the same monthly budget; the
/// same text, voice and style are never paid for twice.
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
            string.IsNullOrWhiteSpace(p.NarratorStyle) ? suggested.Style : p.NarratorStyle.Trim());
    }

    private static void SuggestNarrator(AiStudioProject p)
    {
        var (voice, style) = AiStudioNarration.Suggest(AiStudioNarration.SpeakerOf(p.TranscriptJson), p.Topic);
        p.NarratorVoice = voice;
        p.NarratorStyle = style;
    }

    private static string? VoiceKeyOf(AiStudioProject p, AiStudioScene s)
    {
        if (string.IsNullOrWhiteSpace(s.Narration)) return null;
        var (voice, style) = NarratorOf(p);
        return AiStudioNarration.Key(s.Narration, voice, style);
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
        if (input.Suggest) SuggestNarrator(p);
        if (input.Voice != null)
        {
            if (!AiStudioNarration.IsVoice(input.Voice)) throw new ArgumentException("Giọng đọc không có trong danh sách.");
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

    /// <summary>Reads the scene's narration with the narrator voice (paid, tiny) and fits the clip length to it.</summary>
    public async Task<AiStudioSceneDto> SpeakSceneAsync(Guid projectId, Guid sceneId, CancellationToken ct)
    {
        var (p, s) = await SceneAsync(projectId, sceneId, ct);
        if (string.IsNullOrWhiteSpace(s.Narration)) throw new ArgumentException("Cảnh này không có lời bình.");
        var (voice, style) = NarratorOf(p);
        var key = AiStudioNarration.Key(s.Narration, voice, style);
        if (s.VoiceKey == key && s.VoiceBlobId != null) return await SceneDtoAsync(p, s, ct); // already read this way

        // a take of these words at most: ~2 syllables a second + the tail
        var words = s.Narration.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        await _studio.EnsureMonthlyBudgetAsync(Math.Round((decimal)(words / 2.0 + 2) * _voices.PricePerSecond, 5), ct);
        var gender = StudioVoices.All.FirstOrDefault(v => v.Id == voice)?.Gender;
        var (audio, seconds, fromCache, delivery) = await _voices.SpeakAsync(p.Id, s.Narration, voice, style, StudioVoices.FallbackFor(gender), ct);
        if (delivery != null && delivery != "diễn cảm") _logger.LogWarning("AI Studio narration scene {Scene}: {Delivery}", s.Index, delivery);

        var cost = fromCache ? 0m : Math.Round((decimal)seconds * _voices.PricePerSecond, 5);
        _db.StudioCostRecords.Add(new StudioCostRecord
        {
            Kind = "Voice", Model = voice, Seconds = seconds, CostUsd = cost,
            Note = $"AI Video Studio · {Short(p.Title)} · lời bình cảnh {s.Order + 1}" + (fromCache ? " (dùng lại, miễn phí)" : "")
        });
        if (seconds > AiStudioNarration.MaxVoiceSeconds)
        {
            await _db.SaveChangesAsync(ct);
            throw new InvalidOperationException(
                $"Lời bình cảnh {s.Order + 1} đọc mất {seconds:0.0}s — dài hơn 1 clip ({SeedanceVideoClient.MaxSeconds}s). Rút gọn câu chữ rồi đọc lại.");
        }

        var temp = TempDir();
        try
        {
            var path = Path.Combine(temp, "voice.m4a");
            await File.WriteAllBytesAsync(path, audio, ct);
            var old = s.VoiceBlobId;
            s.VoiceBlobId = (await StoreFileAsync(path, "audio/mp4", ct)).Id;
            await DeleteBlobAsync(old, ct);
        }
        finally { DeleteDir(temp); }
        s.VoiceSeconds = seconds;
        s.VoiceKey = key;

        // the clip must outlast its narration (a longer take is sped up at assembly, up to 25 %)
        var need = SeedanceVideoClient.ClampSeconds((int)Math.Ceiling(seconds + 0.3));
        if (need > s.TargetSeconds)
        {
            s.TargetSeconds = need;
            if (!s.PromptEdited)
            {
                var earlier = await _db.AiStudioScenes.AnyAsync(x => x.ProjectId == projectId && x.Enabled && x.Order < s.Order, ct);
                await SetScenePromptAsync(p, s, AiStudioPromptEngine.Build(PromptInput(p, s, earlier)), "engine", edited: false, ct);
            }
        }
        await _db.SaveChangesAsync(ct);
        return await SceneDtoAsync(p, s, ct);
    }

    public async Task<Guid?> SceneVoiceAsync(Guid sceneId, CancellationToken ct) =>
        await _db.AiStudioScenes.AsNoTracking().Where(s => s.Id == sceneId).Select(s => s.VoiceBlobId).FirstOrDefaultAsync(ct);
}

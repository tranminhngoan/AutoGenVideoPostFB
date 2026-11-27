using Microsoft.Extensions.Configuration;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Infrastructure.Ai;
using NewsStudio.Infrastructure.Common;
using NewsStudio.Infrastructure.Rendering;

namespace NewsStudio.Infrastructure.Growth;

/// <summary>Reads a narration aloud (the app's existing TTS) into an audio file.</summary>
public interface IGrowthVoice
{
    /// <summary>Audio file of <paramref name="text"/> and its length; <paramref name="cleanupDir"/> is removed by the caller afterwards.</summary>
    Task<(string AudioPath, double Seconds, string CleanupDir)> SpeakAsync(string text, string style, CancellationToken ct);
}

/// <summary>
/// The natural Gemini voice the news videos use (Growth:StoryVoice, else Tts:DefaultVoice); like the news pipeline it
/// falls back to the free voice when the monthly AI budget says so.
/// </summary>
public sealed class GeminiGrowthVoice : IGrowthVoice
{
    private readonly ITtsFactory _tts;
    private readonly AiUsageMeter _meter;
    private readonly IConfiguration _configuration;

    public GeminiGrowthVoice(ITtsFactory tts, AiUsageMeter meter, IConfiguration configuration)
    {
        _tts = tts;
        _meter = meter;
        _configuration = configuration;
    }

    public async Task<(string AudioPath, double Seconds, string CleanupDir)> SpeakAsync(string text, string style, CancellationToken ct)
    {
        var voice = _configuration["Growth:StoryVoice"] ?? _configuration["Tts:DefaultVoice"] ?? "gemini:Kore";
        if (GeminiTtsService.IsGeminiVoice(voice) && await _meter.ShouldUseFreeVoiceAsync(ct))
            voice = _configuration["Tts:FreeVoice"] ?? "vi-VN-HoaiMyNeural";
        var service = GeminiTtsService.IsGeminiVoice(voice) ? _tts.GetTtsService("Gemini") : _tts.GetTtsService("EdgeTts");
        var id = Guid.NewGuid(); // a throw-away folder under the temp storage
        var result = await service.SynthesizeSpeechWithSubtitlesAsync(new TtsSynthesisRequest
        {
            ProjectId = id, Text = text, Voice = voice, Speed = "+0%", Style = style
        }, ct);
        return (result.AudioFilePath, result.AudioDurationSeconds, Path.Combine(TempStorage.GetRoot(_configuration), id.ToString()));
    }
}

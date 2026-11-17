using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Enums;
using NewsStudio.Infrastructure.Ai;
using NewsStudio.Infrastructure.Common;

namespace NewsStudio.Infrastructure.Rendering;

/// <summary>
/// Natural-sounding Vietnamese narration using Gemini's native text-to-speech models.
/// Voices are addressed as "gemini:&lt;VoiceName&gt;" (e.g. "gemini:Kore").
/// </summary>
public class GeminiTtsService : ITtsService
{
    public const string VoicePrefix = "gemini:";
    private const int SampleRate = 24000; // Gemini TTS returns 16-bit mono PCM @ 24 kHz

    private static string? _resolvedModel;
    private static readonly SemaphoreSlim ModelLock = new(1, 1);

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ISubtitleGeneratorService _subtitleGenerator;
    private readonly GeminiAiEditorService _gemini;
    private readonly ILogger<GeminiTtsService> _logger;

    public TtsProviderType ProviderType => TtsProviderType.GoogleTts;

    private readonly NewsStudio.Infrastructure.Ai.AiUsageMeter _meter;

    public GeminiTtsService(
        HttpClient httpClient,
        IConfiguration configuration,
        ISubtitleGeneratorService subtitleGenerator,
        GeminiAiEditorService gemini,
        NewsStudio.Infrastructure.Ai.AiUsageMeter meter,
        ILogger<GeminiTtsService> logger)
    {
        _meter = meter;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(5); // ~2 minutes of audio can take a while to synthesize
        _configuration = configuration;
        _subtitleGenerator = subtitleGenerator;
        _gemini = gemini;
        _logger = logger;
    }

    public static bool IsGeminiVoice(string? voice) =>
        !string.IsNullOrWhiteSpace(voice) && voice.StartsWith(VoicePrefix, StringComparison.OrdinalIgnoreCase);

    public async Task<TtsSynthesisResult> SynthesizeSpeechWithSubtitlesAsync(
        TtsSynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!GeminiKeys.HasAny(_configuration))
        {
            throw new InvalidOperationException(
                "Giọng đọc Gemini cần API key. Đặt Ai:Gemini:ApiKey (user-secrets) hoặc GEMINI_API_KEY trên máy chủ.");
        }

        var voiceName = IsGeminiVoice(request.Voice) ? request.Voice[VoicePrefix.Length..] : "Kore";
        var model = await ResolveModelAsync(cancellationToken);
        var text = request.Text.Trim();
        var tempo = ParseTempo(request.Speed);

        var projectDir = Path.Combine(TempStorage.GetRoot(_configuration), request.ProjectId.ToString());
        var audioDir = Path.Combine(projectDir, "audio");
        var subtitleDir = Path.Combine(projectDir, "subtitles");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(subtitleDir);

        var stamp = DateTime.UtcNow.Ticks;
        var rawWavPath = Path.Combine(audioDir, $"gemini_raw_{stamp}.wav");
        var audioFilePath = Path.Combine(audioDir, $"voiceover_{stamp}.wav");
        var srtFilePath = Path.Combine(subtitleDir, "subtitles.srt");
        var vttFilePath = Path.Combine(subtitleDir, "subtitles.vtt");

        _logger.LogInformation("Gemini TTS: project {ProjectId}, model {Model}, voice {Voice}, {Chars} chars",
            request.ProjectId, model, voiceName, text.Length);

        // 1. Synthesize — paid per call, so identical narration (same model + voice + text) is reused:
        //    re-rendering a video (new images, failed render, etc.) doesn't pay for the voice again.
        var prompt = BuildPrompt(text);
        var cacheDir = Path.Combine(TempStorage.GetRoot(_configuration), "tts-cache");
        Directory.CreateDirectory(cacheDir);
        var cacheKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes($"{model}|{voiceName}|{prompt}")))[..32].ToLowerInvariant();
        var cachePath = Path.Combine(cacheDir, cacheKey + ".pcm");

        byte[] pcm;
        if (File.Exists(cachePath))
        {
            pcm = await File.ReadAllBytesAsync(cachePath, cancellationToken);
            _logger.LogInformation("Gemini TTS: reused cached narration for project {ProjectId} (no API call)", request.ProjectId);
        }
        else
        {
            pcm = await RequestSpeechAsync(model, voiceName, prompt, cancellationToken);
            try { await File.WriteAllBytesAsync(cachePath, pcm, cancellationToken); }
            catch (IOException ex) { _logger.LogWarning("Could not cache TTS audio: {Message}", ex.Message); }
        }
        await WriteWavAsync(rawWavPath, pcm, cancellationToken);
        var rawSeconds = pcm.Length / (double)(SampleRate * 2);

        // 2. Speed adjustment (pitch-preserving), gentle loudness levelling, short tail
        var ffmpeg = EdgeTtsService.FindFfmpegExecutable()
            ?? throw new InvalidOperationException("Không tìm thấy FFmpeg trên máy chủ để xử lý giọng đọc.");
        var tempoStr = tempo.ToString("F2", CultureInfo.InvariantCulture);
        var tailStr = EdgeTtsService.TailSeconds.ToString("F2", CultureInfo.InvariantCulture);
        var filter = $"atempo={tempoStr},highpass=f=70,loudnorm=I=-16:TP=-1.5:LRA=11,apad=pad_dur={tailStr}";
        var ok = await EdgeTtsService.RunProcessAsync(ffmpeg,
            $"-hide_banner -y -i \"{rawWavPath}\" -af \"{filter}\" -ar {SampleRate} -ac 1 \"{audioFilePath}\"",
            cancellationToken);
        if (!ok || !File.Exists(audioFilePath))
        {
            throw new InvalidOperationException("Xử lý âm thanh giọng đọc Gemini bằng FFmpeg thất bại.");
        }
        try { File.Delete(rawWavPath); } catch { /* best effort */ }

        var totalDuration = Math.Round(rawSeconds / tempo + EdgeTtsService.TailSeconds, 2);

        // 3. Timing cues (internal use only — subtitles are never shown on screen)
        var cues = EdgeTtsService.GenerateFallbackCues(text, totalDuration);
        await _subtitleGenerator.GenerateSrtAsync(cues, srtFilePath, cancellationToken);
        await _subtitleGenerator.GenerateVttAsync(cues, vttFilePath, cancellationToken);

        var info = new FileInfo(audioFilePath);
        return new TtsSynthesisResult
        {
            AudioFilePath = audioFilePath,
            AudioDurationSeconds = totalDuration,
            SubtitleFilePath = srtFilePath,
            SubtitleVttFilePath = vttFilePath,
            Subtitles = cues,
            Format = "audio/wav",
            FileSizeBytes = info.Length,
            Sha256Hash = await EdgeTtsService.ComputeFileSha256Async(audioFilePath, cancellationToken)
        };
    }

    /// <summary>
    /// The text sent to the TTS model is ONLY the narration. A style instruction placed in front of it
    /// ("Đọc bằng tiếng Việt như một phát thanh viên…") was sometimes read aloud as part of the video,
    /// so none is added by default. An optional, short directive can be set in Tts:Gemini:Style — test
    /// it with the voice preview first, because the model may still speak it.
    /// </summary>
    private string BuildPrompt(string text)
    {
        var style = _configuration["Tts:Gemini:Style"];
        return string.IsNullOrWhiteSpace(style) ? text : $"{style.Trim()}: {text}";
    }

    /// <summary>Free key first (see <see cref="GeminiKeys"/>); falls through to the paid key when the free one is refused.</summary>
    private async Task<byte[]> RequestSpeechAsync(string model, string voiceName, string prompt, CancellationToken ct)
    {
        var keys = GeminiKeys.InOrder(_configuration, model);
        for (var i = 0; ; i++)
        {
            var key = keys[i];
            var hasNext = i < keys.Count - 1;
            var (pcm, status, reason, usage) = await SendSpeechRequestAsync(key.Value, model, voiceName, prompt, ct);
            if (pcm != null)
            {
                _logger.LogInformation("Gemini TTS used the {KeyType} key ({Model})", key.Label, model);
                await _meter.RecordAsync("tts", model, key.IsFree, usage, speech: true);
                return pcm;
            }

            if (key.IsFree) GeminiKeys.MarkFreeRefused(model, status, reason);
            if (key.IsFree && hasNext)
            {
                _logger.LogInformation("Gemini TTS free key refused (HTTP {Status}); using paid key. {Reason}", (int)status, reason);
                continue;
            }
            throw BuildSpeechError(model, status, reason, key.IsFree);
        }
    }

    private static InvalidOperationException BuildSpeechError(string model, System.Net.HttpStatusCode status, string reason, bool freeKey)
    {
        if ((int)status == 429)
        {
            var retry = System.Text.RegularExpressions.Regex.Match(reason, @"retry in (?:(\d+)h)?(?:(\d+)m)?");
            var wait = retry.Success
                ? $" Có thể dùng lại sau khoảng {(retry.Groups[1].Success ? retry.Groups[1].Value + " giờ " : "")}{(retry.Groups[2].Success ? retry.Groups[2].Value + " phút" : "")}."
                : string.Empty;
            var freeTier = freeKey
                           || reason.Contains("free_tier", StringComparison.OrdinalIgnoreCase)
                           || reason.Contains("Free Tier", StringComparison.OrdinalIgnoreCase);
            return new InvalidOperationException(freeTier
                ? $"Key Gemini đang ở gói MIỄN PHÍ và đã hết lượt đọc giọng (model {model}).{wait} " +
                  "Để không bị gián đoạn: đặt thêm key của project đã gắn billing vào Ai__Gemini__ApiKey — app sẽ dùng key miễn phí trước rồi tự chuyển sang key trả phí. " +
                  "Trong lúc chờ, hãy chọn giọng Hoài My hoặc Nam Minh."
                : $"Gemini đang giới hạn tốc độ đọc giọng (model {model}).{wait} Thử lại sau ít phút hoặc chọn giọng Hoài My / Nam Minh.");
        }
        return new InvalidOperationException($"Tạo giọng đọc Gemini thất bại (HTTP {(int)status}): {reason}");
    }

    /// <summary>One TTS call. Returns the PCM audio, or null plus Google's status/reason on failure.</summary>
    private async Task<(byte[]? Pcm, System.Net.HttpStatusCode Status, string Reason, JsonElement? Usage)> SendSpeechRequestAsync(
        string apiKey, string model, string voiceName, string prompt, CancellationToken ct)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
        var body = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new
            {
                responseModalities = new[] { "AUDIO" },
                speechConfig = new
                {
                    voiceConfig = new { prebuiltVoiceConfig = new { voiceName } }
                }
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(url, body, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            return (null, response.StatusCode, ExtractError(json), null);
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0
            && candidates[0].TryGetProperty("content", out var content)
            && content.TryGetProperty("parts", out var parts))
        {
            var audio = new MemoryStream();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("inlineData", out var inline) && inline.TryGetProperty("data", out var data))
                {
                    var bytes = Convert.FromBase64String(data.GetString() ?? string.Empty);
                    audio.Write(bytes);
                }
            }
            if (audio.Length > 0)
                return (audio.ToArray(), response.StatusCode, string.Empty,
                    doc.RootElement.TryGetProperty("usageMetadata", out var usage) ? usage.Clone() : null);
        }

        throw new InvalidOperationException("Gemini không trả về dữ liệu âm thanh. Thử lại hoặc chọn giọng khác.");
    }

    /// <summary>
    /// Uses Tts:Gemini:Model when set; otherwise picks the newest non-preview Flash TTS model this key
    /// can use (live list from Google), so a retired model name can't silently break narration.
    /// </summary>
    private async Task<string> ResolveModelAsync(CancellationToken ct)
    {
        var configured = _configuration["Tts:Gemini:Model"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        if (_resolvedModel != null) return _resolvedModel;

        await ModelLock.WaitAsync(ct);
        try
        {
            if (_resolvedModel != null) return _resolvedModel;
            var models = await _gemini.ListAvailableModelsAsync(ct) ?? new List<AiModelInfoDto>();
            var tts = models
                .Select(m => m.Id)
                .Where(id => id.Contains("tts", StringComparison.OrdinalIgnoreCase)
                             && id.Contains("flash", StringComparison.OrdinalIgnoreCase))
                .OrderBy(id => id.Contains("preview", StringComparison.OrdinalIgnoreCase)) // stable first
                .ThenBy(id => id.Contains("lite", StringComparison.OrdinalIgnoreCase))     // full quality first
                .ThenByDescending(VersionOf)
                .FirstOrDefault();
            _resolvedModel = tts ?? throw new InvalidOperationException(
                "API key Gemini này không có model đọc giọng (TTS) nào. Đặt Tts:Gemini:Model hoặc chọn giọng Edge.");
            _logger.LogInformation("Gemini TTS model auto-selected: {Model}", _resolvedModel);
            return _resolvedModel;
        }
        finally
        {
            ModelLock.Release();
        }
    }

    private static double VersionOf(string id)
    {
        var m = System.Text.RegularExpressions.Regex.Match(id, @"(\d+(\.\d+)?)");
        return m.Success && double.TryParse(m.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static double ParseTempo(string? speed)
    {
        if (string.IsNullOrWhiteSpace(speed)) return 1.0;
        var clean = speed.Replace("%", "").Replace("+", "").Trim();
        return double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct)
            ? Math.Clamp(1.0 + pct / 100.0, 0.8, 1.5)
            : 1.0;
    }

    private static string ExtractError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "Lỗi không rõ";
        }
        catch (JsonException) { }
        return json.Length > 300 ? json[..300] : json;
    }

    private static async Task WriteWavAsync(string path, byte[] pcm, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var bw = new BinaryWriter(fs);
        const short channels = 1, bits = 16;
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + pcm.Length);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write(channels);
        bw.Write(SampleRate);
        bw.Write(SampleRate * channels * bits / 8);
        bw.Write((short)(channels * bits / 8));
        bw.Write(bits);
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(pcm.Length);
        bw.Write(pcm);
        await fs.FlushAsync(ct);
    }
}

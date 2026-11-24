using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    private static List<string>? _resolvedModels;
    private static readonly SemaphoreSlim ModelLock = new(1, 1);

    /// <summary>
    /// TTS models whose daily quota is used up on every key (free and paid), until the quota resets.
    /// Google counts quotas per model, so the next TTS model still works with the same voices.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> ModelExhaustedUntil = new();

    /// <summary>The model can't be used now (daily quota on every key, or not offered) — try the next one.</summary>
    private sealed class TtsModelUnavailableException(string message) : InvalidOperationException(message);

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
        var models = await ResolveModelsAsync(cancellationToken);
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

        _logger.LogInformation("Gemini TTS: project {ProjectId}, models {Models}, voice {Voice}, {Chars} chars",
            request.ProjectId, string.Join(", ", models), voiceName, text.Length);

        // 1. Synthesize — paid per call, so identical narration (same model + voice + text) is reused:
        //    re-rendering a video (new images, failed render, etc.) doesn't pay for the voice again.
        var prompt = BuildPrompt(text);
        var cacheDir = Path.Combine(TempStorage.GetRoot(_configuration), "tts-cache");
        Directory.CreateDirectory(cacheDir);
        string CachePath(string m) => Path.Combine(cacheDir, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes($"{m}|{voiceName}|{prompt}")))[..32].ToLowerInvariant() + ".pcm");

        byte[]? pcm = null;
        var fromCache = false;
        var styled = false;
        string? styleError = null;
        // An acting direction goes to the expressive TTS model, apart from the words (never read aloud). If that model
        // or its API is not available, the plain narration below is used instead: a video never fails over style.
        var style = request.Style?.Trim();
        // a designed voice ("voice_…") lives on the expressive model only: always go there, with a light direction
        var designed = voiceName.StartsWith("voice_", StringComparison.Ordinal);
        if (designed && string.IsNullOrEmpty(style)) style = "natural, warm and conversational";
        if (!string.IsNullOrEmpty(style) && ExpressiveModel is { } expressive)
        {
            var styledCache = Path.Combine(cacheDir, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes($"{expressive}|{voiceName}|{style}|{text}")))[..32].ToLowerInvariant() + ".pcm");
            if (File.Exists(styledCache))
            {
                pcm = await File.ReadAllBytesAsync(styledCache, cancellationToken);
                fromCache = true;
                styled = true;
            }
            else
            {
                try
                {
                    pcm = await RequestExpressiveSpeechAsync(expressive, voiceName, text, style, cancellationToken);
                    styled = true;
                    try { await File.WriteAllBytesAsync(styledCache, pcm, cancellationToken); }
                    catch (IOException ex) { _logger.LogWarning("Could not cache TTS audio: {Message}", ex.Message); }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("Expressive Gemini TTS ({Model}) unavailable, reading without direction: {Message}", expressive, ex.Message);
                    pcm = null;
                    styleError = ex.Message.Length > 160 ? ex.Message[..160] : ex.Message;
                }
            }
        }

        // a designed voice that can't be used is an error, not a silent switch to another voice mid-video
        if (pcm == null && designed)
            throw new InvalidOperationException("Giọng thiết kế chưa dùng được (" + (styleError ?? "không rõ lỗi") +
                                                ") — tạo lại giọng ở tab Giọng đọc rồi đọc lại.");
        var cached = pcm != null ? null : models.Select(CachePath).FirstOrDefault(File.Exists);
        if (pcm != null)
        {
            // the styled take
        }
        else if (cached != null)
        {
            fromCache = true;
            pcm = await File.ReadAllBytesAsync(cached, cancellationToken);
            _logger.LogInformation("Gemini TTS: reused cached narration for project {ProjectId} (no API call)", request.ProjectId);
        }
        else
        {
            var (speech, model) = await RequestSpeechAnyModelAsync(models, voiceName, prompt, cancellationToken);
            pcm = speech;
            try { await File.WriteAllBytesAsync(CachePath(model), pcm, cancellationToken); }
            catch (IOException ex) { _logger.LogWarning("Could not cache TTS audio: {Message}", ex.Message); }
        }
        pcm = TrimTrailingNoiseBurst(pcm, out var trimmedMs);
        if (trimmedMs > 0) _logger.LogDebug("Gemini TTS: trimmed {Ms} ms end-of-clip noise burst", trimmedMs);
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
            Sha256Hash = await EdgeTtsService.ComputeFileSha256Async(audioFilePath, cancellationToken),
            FromCache = fromCache,
            Styled = styled,
            StyleError = styleError
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

    /// <summary>
    /// The expressive TTS model used when a request carries an acting direction (Tts:Gemini:ExpressiveModel,
    /// default gemini-3.8-flash-tts; "off" disables it).
    /// </summary>
    private string? ExpressiveModel
    {
        get
        {
            var m = _configuration["Tts:Gemini:ExpressiveModel"]?.Trim();
            if (string.Equals(m, "off", StringComparison.OrdinalIgnoreCase)) return null;
            return string.IsNullOrEmpty(m) ? "gemini-3.8-flash-tts" : m;
        }
    }

    /// <summary>
    /// Request body of the Interactions API for one styled take: the text is a verbatim transcript and the direction
    /// rides in a speech_metadata annotation, so the model performs it instead of reading it out.
    /// </summary>
    public static JsonObject BuildExpressiveRequest(string model, string voiceName, string text, string style) => new()
    {
        ["model"] = model,
        ["input"] = new JsonArray(new JsonObject
        {
            ["type"] = "user_input",
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = text,
                ["annotations"] = new JsonArray(new JsonObject { ["type"] = "speech_metadata", ["style"] = style })
            })
        }),
        ["response_format"] = new JsonObject { ["type"] = "audio" },
        ["generation_config"] = new JsonObject { ["speech_config"] = new JsonArray(new JsonObject { ["voice"] = voiceName }) }
    };

    /// <summary>
    /// The audio of an Interactions answer as 16-bit mono PCM at 24 kHz: the first base64 "data" of an audio item
    /// (WAV with its header, or raw PCM). Null when there is none or it is in another format.
    /// </summary>
    public static byte[]? ParseExpressiveAudio(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var found = FindAudio(doc.RootElement);
        if (found == null) return null;
        var bytes = Convert.FromBase64String(found.Value.Data);
        if (bytes.Length >= 44 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE")
        {
            int? rate = null, bits = null, channels = null;
            var pos = 12;
            while (pos + 8 <= bytes.Length)
            {
                var id = Encoding.ASCII.GetString(bytes, pos, 4);
                var size = BitConverter.ToInt32(bytes, pos + 4);
                if (id == "fmt " && pos + 24 <= bytes.Length)
                {
                    channels = BitConverter.ToInt16(bytes, pos + 10);
                    rate = BitConverter.ToInt32(bytes, pos + 12);
                    bits = BitConverter.ToInt16(bytes, pos + 22);
                }
                else if (id == "data")
                {
                    if (rate != SampleRate || bits != 16 || channels != 1) return null;
                    var len = size < 0 ? bytes.Length - pos - 8 : Math.Min(size, bytes.Length - pos - 8);
                    return bytes.AsSpan(pos + 8, len - len % 2).ToArray();
                }
                if (size < 0) return null;
                pos += 8 + size + (size % 2);
            }
            return null;
        }
        // raw PCM: accept only the 24 kHz rate this pipeline writes (stated in the mime type when given)
        var rateMatch = System.Text.RegularExpressions.Regex.Match(found.Value.Mime ?? "", @"rate=(\d+)");
        if (rateMatch.Success && rateMatch.Groups[1].Value != SampleRate.ToString(CultureInfo.InvariantCulture)) return null;
        return bytes.Length % 2 == 0 ? bytes : bytes[..^1];
    }

    private static (string Data, string? Mime)? FindAudio(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                string? type = null, mime = null, data = null;
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.String) continue;
                    if (p.NameEquals("type")) type = p.Value.GetString();
                    else if (p.NameEquals("mime_type") || p.NameEquals("mimeType")) mime = p.Value.GetString();
                    else if (p.NameEquals("data")) data = p.Value.GetString();
                }
                if (data is { Length: > 100 } && (type == "audio" || mime?.StartsWith("audio", StringComparison.OrdinalIgnoreCase) == true))
                    return (data, mime);
                foreach (var p in e.EnumerateObject())
                    if (FindAudio(p.Value) is { } inner) return inner;
                return null;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray())
                    if (FindAudio(x) is { } inner) return inner;
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Designs a new persistent voice from a description (Voices endpoint): age, timbre and regional accent are fixed
    /// in the voice itself. Returns its id ("voice_…") and the sample Google reads with it (WAV or raw PCM), if any.
    /// </summary>
    public async Task<(string VoiceId, byte[]? Sample)> DesignVoiceAsync(string displayName, string gender, string description, CancellationToken ct)
    {
        var model = ExpressiveModel ?? "gemini-3.8-flash-tts";
        var body = new JsonObject
        {
            ["store"] = true,
            ["voice"] = new JsonObject
            {
                ["model"] = model,
                ["type"] = "prompted",
                ["display_name"] = displayName.Length > 60 ? displayName[..60] : displayName,
                ["gender"] = gender == "female" ? "female" : "male",
                ["language_code"] = "vi-VN",
                ["prompted"] = new JsonObject { ["input"] = description }
            }
        }.ToJsonString();
        string? last = null;
        // a designed voice belongs to the Google project of the key that made it: always the paid key when there is one,
        // so every later take (also read with the paid key first) finds it
        foreach (var key in DesignedVoiceKeys(model))
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://generativelanguage.googleapis.com/v1beta/voices")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("x-goog-api-key", key.Value);
            using var res = await _httpClient.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(json);
                var id = FindVoiceId(doc.RootElement);
                if (id == null) { last = "Google không trả về mã giọng."; continue; }
                byte[]? sample = null;
                var data = FindSampleData(doc.RootElement);
                if (data != null) { try { sample = Convert.FromBase64String(data); } catch (FormatException) { } }
                _logger.LogInformation("Designed voice {VoiceId} ({Name})", id, displayName);
                return (id, sample);
            }
            last = $"HTTP {(int)res.StatusCode}: {ExtractError(json)}";
        }
        throw new InvalidOperationException("Không tạo được giọng: " + (last ?? "chưa có khoá Gemini"));
    }

    private static string? FindVoiceId(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                return e.GetString() is { } s && s.StartsWith("voice_", StringComparison.Ordinal) ? s : null;
            case JsonValueKind.Object:
                if (e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { } v && v.StartsWith("voice_", StringComparison.Ordinal))
                    return v;
                foreach (var p in e.EnumerateObject())
                    if (!p.NameEquals("sample_audio") && FindVoiceId(p.Value) is { } inner) return inner;
                return null;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray())
                    if (FindVoiceId(x) is { } inner) return inner;
                return null;
            default:
                return null;
        }
    }

    private static string? FindSampleData(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                if ((p.NameEquals("sample_audio") || p.NameEquals("sampleAudio")) && p.Value.ValueKind == JsonValueKind.Object
                    && p.Value.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.String)
                    return d.GetString();
                if (FindSampleData(p.Value) is { } inner) return inner;
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in e.EnumerateArray())
                if (FindSampleData(x) is { } inner) return inner;
        }
        return null;
    }

    /// <summary>Keys for designed voices: the paid key first (stable project, no free-tier rests), then the others.</summary>
    private List<GeminiKeys.Key> DesignedVoiceKeys(string model) =>
        GeminiKeys.InOrder(_configuration, model).OrderBy(k => k.IsFree).ToList();

    /// <summary>One styled take through the Interactions API, free keys first then the paid key.</summary>
    private async Task<byte[]> RequestExpressiveSpeechAsync(string model, string voiceName, string text, string style, CancellationToken ct)
    {
        var body = BuildExpressiveRequest(model, voiceName, text, style).ToJsonString();
        string? last = null;
        var keys = voiceName.StartsWith("voice_", StringComparison.Ordinal) ? DesignedVoiceKeys(model) : GeminiKeys.InOrder(_configuration, model);
        foreach (var key in keys)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://generativelanguage.googleapis.com/v1beta/interactions")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("x-goog-api-key", key.Value);
            using var res = await _httpClient.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode)
            {
                var pcm = ParseExpressiveAudio(json);
                if (pcm is { Length: > 4800 })
                {
                    _logger.LogInformation("Gemini TTS: styled take with {Model} ({KeyType} key)", model, key.Label);
                    JsonElement? usage = null;
                    using (var doc = JsonDocument.Parse(json))
                        if (doc.RootElement.TryGetProperty("usage", out var u)) usage = u.Clone();
                    await _meter.RecordAsync("tts", model, key.IsFree, usage, speech: true);
                    return pcm;
                }
                last = "no usable audio in the answer";
                continue;
            }
            last = $"HTTP {(int)res.StatusCode}: {ExtractError(json)}";
        }
        throw new InvalidOperationException(last ?? "no Gemini key");
    }

    /// <summary>
    /// Tries the TTS models in order. For each model the free key goes first, then the paid key. A model
    /// whose daily quota is used up on every key is skipped until the quota resets (Pacific midnight), so a
    /// video never waits hours while another TTS model (same voices) still has quota.
    /// </summary>
    private async Task<(byte[] Pcm, string Model)> RequestSpeechAnyModelAsync(
        IReadOnlyList<string> models, string voiceName, string prompt, CancellationToken ct)
    {
        foreach (var model in models)
        {
            if (ModelExhaustedUntil.TryGetValue(model, out var until) && until > DateTimeOffset.UtcNow) continue;
            try
            {
                return (await RequestSpeechAsync(model, voiceName, prompt, ct), model);
            }
            catch (TtsModelUnavailableException ex)
            {
                ModelExhaustedUntil[model] = GeminiKeys.NextPacificMidnight(DateTimeOffset.UtcNow);
                _logger.LogWarning("Gemini TTS model {Model} unavailable until the daily reset; trying the next model. {Reason}", model, ex.Message);
            }
        }
        throw AllModelsExhaustedError(models);
    }

    private static InvalidOperationException AllModelsExhaustedError(IReadOnlyList<string> models)
    {
        var reset = models.Select(m => ModelExhaustedUntil.TryGetValue(m, out var u) ? u : DateTimeOffset.UtcNow)
            .DefaultIfEmpty(DateTimeOffset.UtcNow).Min().ToOffset(TimeSpan.FromHours(7));
        return new InvalidOperationException(
            $"Hôm nay Google đã hết lượt đọc giọng Gemini ở cả key miễn phí và key trả phí (đã thử: {string.Join(", ", models)}). " +
            $"Lượt mới mở lại khoảng {reset:HH:mm} (giờ Việt Nam). Trong lúc chờ, hãy chọn giọng Hoài My hoặc Nam Minh. " +
            "Lưu ý: Xưởng phim dùng chung key và lượt đọc giọng với video tin tức.");
    }

    /// <summary>Free key first (see <see cref="GeminiKeys"/>); falls through to the paid key when the free one is refused.</summary>
    private async Task<byte[]> RequestSpeechAsync(string model, string voiceName, string prompt, CancellationToken ct)
    {
        var keys = GeminiKeys.InOrder(_configuration, model);
        var rateWaits = 0;
        for (var i = 0; ; i++)
        {
            var key = keys[i];
            var hasNext = i < keys.Count - 1;
            var (pcm, status, reason, usage) = await SendSpeechRequestAsync(key.Value, model, voiceName, prompt, ct);

            // Per-minute limit on the last key (TTS allows only a few requests per minute): wait it out and retry
            // instead of failing a whole video. A daily quota is not waited for.
            if (pcm == null && (int)status == 429 && !hasNext && rateWaits < 6 && !GeminiKeys.IsDailyQuota(reason))
            {
                rateWaits++;
                var wait = RetryDelay(reason) ?? TimeSpan.FromSeconds(20 * rateWaits);
                _logger.LogInformation("Gemini TTS rate-limited; waiting {Seconds:F0}s before retry {Attempt}/6", wait.TotalSeconds, rateWaits);
                await Task.Delay(wait, ct);
                i--; // same key again
                continue;
            }
            if (pcm != null)
            {
                _logger.LogInformation("Gemini TTS used the {KeyType} key ({Model})", key.Label, model);
                await _meter.RecordAsync("tts", model, key.IsFree, usage, speech: true);
                return pcm;
            }

            if (key.IsFree) GeminiKeys.MarkFreeRefused(key, model, status, reason);
            if (key.IsFree && hasNext)
            {
                _logger.LogInformation("Gemini TTS {KeyType} key refused (HTTP {Status}); trying the {Next} key. {Reason}",
                    key.Label, (int)status, keys[i + 1].Label, reason);
                continue;
            }
            var error = BuildSpeechError(model, status, reason, key.IsFree);
            // Out of today's quota on the last key, or the model isn't offered: the next TTS model may still work
            if (((int)status == 429 && GeminiKeys.IsDailyQuota(reason)) || (int)status == 404)
                throw new TtsModelUnavailableException(error.Message);
            throw error;
        }
    }

    /// <summary>
    /// Gemini TTS clips end with a ~160 ms burst of loud static after the speech has already faded to silence
    /// (heard as a "rè" crackle after every line). Cuts everything after the last ≥50 ms silence when what
    /// follows, within the final 400 ms, contains such a loud burst. Speech running to the very end is left alone.
    /// </summary>
    public static byte[] TrimTrailingNoiseBurst(byte[] pcm, out int trimmedMs)
    {
        trimmedMs = 0;
        const int window = SampleRate / 100; // 10 ms
        var samples = pcm.Length / 2;
        var windows = samples / window;
        if (windows < 10) return pcm;

        double Rms(int w)
        {
            double sum = 0;
            for (var i = w * window; i < (w + 1) * window; i++)
            {
                var v = BitConverter.ToInt16(pcm, i * 2);
                sum += (double)v * v;
            }
            return Math.Sqrt(sum / window);
        }

        var rms = Enumerable.Range(0, windows).Select(Rms).ToArray();
        var searchFrom = Math.Max(0, windows - 40); // last 400 ms
        var loudAfter = false;
        var silentRun = 0;
        for (var w = windows - 1; w >= searchFrom; w--)
        {
            if (rms[w] > 3000) { loudAfter = true; silentRun = 0; continue; }
            if (rms[w] < 150)
            {
                silentRun++;
                if (silentRun >= 5 && loudAfter)
                {
                    var cutWindow = w + silentRun; // first window of the burst
                    var tailSamples = samples - cutWindow * window;
                    // Static, not a last word: short, and noise-like (speech crosses zero ~1k times/s, the burst ~4k).
                    var crossings = 0;
                    for (var i = cutWindow * window + 1; i < samples; i++)
                        if ((BitConverter.ToInt16(pcm, i * 2) < 0) != (BitConverter.ToInt16(pcm, (i - 1) * 2) < 0)) crossings++;
                    var crossingsPerSecond = crossings * (double)SampleRate / Math.Max(1, tailSamples);
                    if (tailSamples > SampleRate / 4 || crossingsPerSecond < 2500) return pcm;

                    trimmedMs = tailSamples * 1000 / SampleRate;
                    return pcm[..(cutWindow * window * 2)];
                }
            }
            else
            {
                silentRun = 0;
            }
        }
        return pcm;
    }

    /// <summary>"Please retry in 37.5s" / "retryDelay": "37s" → 38 s (capped to 2 minutes).</summary>
    private static TimeSpan? RetryDelay(string reason)
    {
        var m = System.Text.RegularExpressions.Regex.Match(reason, @"(?:retry in|retryDelay""?\s*:\s*""?)\s*(\d+(?:\.\d+)?)s",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var s)
            ? TimeSpan.FromSeconds(Math.Clamp(Math.Ceiling(s) + 1, 2, 120))
            : null;
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
    /// TTS models to try, best first: Tts:Gemini:Model when set, then the TTS models this key can use
    /// (live list from Google) — newest stable Flash first, then preview / lite Flash, Pro last (dearer).
    /// Later ones are only used when the earlier ones are out of today's quota.
    /// </summary>
    private async Task<List<string>> ResolveModelsAsync(CancellationToken ct)
    {
        var configured = _configuration["Tts:Gemini:Model"]?.Trim();
        var auto = _resolvedModels;
        if (auto == null)
        {
            await ModelLock.WaitAsync(ct);
            try
            {
                auto = _resolvedModels;
                if (auto == null)
                {
                    var listed = await _gemini.ListAvailableModelsAsync(ct) ?? new List<AiModelInfoDto>();
                    auto = OrderTtsModels(listed.Select(m => m.Id));
                    if (auto.Count > 0)
                    {
                        _resolvedModels = auto;
                        _logger.LogInformation("Gemini TTS models auto-selected: {Models}", string.Join(", ", auto));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !string.IsNullOrWhiteSpace(configured))
            {
                _logger.LogWarning("Listing Gemini TTS models failed: {Message}", ex.Message);
                auto = new List<string>();
            }
            finally
            {
                ModelLock.Release();
            }
        }

        var all = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured)) all.Add(configured);
        all.AddRange(auto.Where(m => !all.Contains(m, StringComparer.OrdinalIgnoreCase)));
        if (all.Count == 0)
            throw new InvalidOperationException(
                "API key Gemini này không có model đọc giọng (TTS) nào. Đặt Tts:Gemini:Model hoặc chọn giọng Hoài My / Nam Minh.");
        return all;
    }

    /// <summary>Gemini TTS model ids, best first: Flash before Pro, stable before preview, full before lite, newest first.</summary>
    public static List<string> OrderTtsModels(IEnumerable<string> ids) => ids
        .Where(id => id.Contains("tts", StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(id => !id.Contains("flash", StringComparison.OrdinalIgnoreCase))  // Flash (cheap) first, Pro last
        .ThenBy(id => id.Contains("preview", StringComparison.OrdinalIgnoreCase))  // stable first
        .ThenBy(id => id.Contains("lite", StringComparison.OrdinalIgnoreCase))     // full quality first
        .ThenByDescending(VersionOf)
        .ToList();

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
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
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

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Infrastructure.Rendering;

public class EdgeTtsService : ITtsService
{
    /// <summary>Short pause kept after the last word so the video doesn't cut mid-breath.</summary>
    internal const double TailSeconds = 0.6;

    private readonly ISubtitleGeneratorService _subtitleGenerator;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EdgeTtsService> _logger;

    public TtsProviderType ProviderType => TtsProviderType.EdgeTts;

    public EdgeTtsService(
        ISubtitleGeneratorService subtitleGenerator,
        IConfiguration configuration,
        ILogger<EdgeTtsService> logger)
    {
        _subtitleGenerator = subtitleGenerator;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<TtsSynthesisResult> SynthesizeSpeechWithSubtitlesAsync(
        TtsSynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("EdgeTtsService synthesizing speech for project {ProjectId} (Voice: {Voice})",
            request.ProjectId, request.Voice);

        var tempBase = NewsStudio.Infrastructure.Common.TempStorage.GetRoot(_configuration);
        var projectDir = Path.Combine(tempBase, request.ProjectId.ToString());
        var audioDir = Path.Combine(projectDir, "audio");
        var subtitleDir = Path.Combine(projectDir, "subtitles");

        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(subtitleDir);

        var timestamp = DateTime.UtcNow.Ticks;
        var audioFileName = $"voiceover_{timestamp}.wav";
        var audioFilePath = Path.Combine(audioDir, audioFileName);
        var srtFilePath = Path.Combine(subtitleDir, "subtitles.srt");
        var vttFilePath = Path.Combine(subtitleDir, "subtitles.vtt");

        var text = request.Text.Trim();
        var voice = !string.IsNullOrWhiteSpace(request.Voice) ? request.Voice : "vi-VN-HoaiMyNeural";

        var totalWords = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        // Rough estimate only (used before the real audio length is measured) — never clamped.
        var estimatedDuration = Math.Max(3.0, Math.Round(totalWords / 2.3, 2));

        bool synthesizedSuccessfully = false;
        double totalDuration = estimatedDuration;
        var cues = new List<SubtitleCueDto>();

        var pythonExe = FindPythonExecutable();
        var ffmpegExe = FindFfmpegExecutable();

        if (pythonExe != null && ffmpegExe != null)
        {
            var tempInputTxt = Path.Combine(audioDir, $"input_{timestamp}.txt");
            var tempMp3Path = Path.Combine(audioDir, $"temp_voiceover_{timestamp}.mp3");
            var tempSrtPath = Path.Combine(subtitleDir, $"temp_sub_{timestamp}.srt");

            try
            {
                await File.WriteAllTextAsync(tempInputTxt, text, Encoding.UTF8, cancellationToken);

                // 1. Synthesize real neural voice using edge-tts
                var edgeTtsArgs = $"-m edge_tts --voice \"{voice}\" --file \"{tempInputTxt}\" --write-media \"{tempMp3Path}\" --write-subtitles \"{tempSrtPath}\"";
                var ttsSuccess = await RunProcessAsync(pythonExe, edgeTtsArgs, cancellationToken);

                if ((!ttsSuccess || !File.Exists(tempMp3Path) || new FileInfo(tempMp3Path).Length == 0) && voice != "vi-VN-HoaiMyNeural")
                {
                    _logger.LogWarning("Voice '{Voice}' synthesis failed or was rejected. Falling back to default 'vi-VN-HoaiMyNeural'.", voice);
                    edgeTtsArgs = $"-m edge_tts --voice \"vi-VN-HoaiMyNeural\" --file \"{tempInputTxt}\" --write-media \"{tempMp3Path}\" --write-subtitles \"{tempSrtPath}\"";
                    ttsSuccess = await RunProcessAsync(pythonExe, edgeTtsArgs, cancellationToken);
                }

                if (ttsSuccess && File.Exists(tempMp3Path) && new FileInfo(tempMp3Path).Length > 0)
                {
                    // 2. Measure actual speech duration using ffprobe
                    double rawSpeechDuration = estimatedDuration;
                    var probeExe = FindFfprobeExecutable();
                    if (probeExe != null)
                    {
                        var durStr = await RunProcessWithOutputAsync(probeExe, $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{tempMp3Path}\"", cancellationToken);
                        if (double.TryParse(durStr?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDur) && parsedDur > 0)
                        {
                            rawSpeechDuration = parsedDur;
                        }
                    }

                    double tempo = 1.12; // Default breaking news cadence (+12%)
                    if (!string.IsNullOrWhiteSpace(request.Speed))
                    {
                        var cleanSpeed = request.Speed.Replace("%", "").Replace("+", "").Trim();
                        if (double.TryParse(cleanSpeed, NumberStyles.Any, CultureInfo.InvariantCulture, out var percent))
                        {
                            tempo = Math.Clamp(1.0 + (percent / 100.0), 0.8, 1.5);
                        }
                    }

                    // Adjust speech duration for tempo
                    rawSpeechDuration = rawSpeechDuration / tempo;
                    // The video lasts exactly as long as the narration (+ a short tail) — no padding to a
                    // fixed 55–65s window, which used to leave long silent endings.
                    totalDuration = Math.Round(rawSpeechDuration + TailSeconds, 2);

                    // 3. Convert MP3 to 24kHz mono WAV with a short silent tail
                    var tempoStr = tempo.ToString("F2", CultureInfo.InvariantCulture);
                    var tailStr = TailSeconds.ToString("F2", CultureInfo.InvariantCulture);
                    var ffmpegArgs = $"-hide_banner -y -i \"{tempMp3Path}\" -filter_complex \"[0:a]atempo={tempoStr},equalizer=f=3000:t=q:w=1.5:g=2,highpass=f=80,apad=pad_dur={tailStr}[a]\" -map \"[a]\" -ar 24000 -ac 1 \"{audioFilePath}\"";
                    var ffmpegSuccess = await RunProcessAsync(ffmpegExe, ffmpegArgs, cancellationToken);

                    if (ffmpegSuccess && File.Exists(audioFilePath) && new FileInfo(audioFilePath).Length > 0)
                    {
                        // 4. Parse generated SRT subtitles
                        if (File.Exists(tempSrtPath))
                        {
                            var srtRaw = await File.ReadAllTextAsync(tempSrtPath, Encoding.UTF8, cancellationToken);
                            cues = ParseSrt(srtRaw);
                            foreach (var c in cues)
                            {
                                c.StartTimeSeconds = Math.Round(c.StartTimeSeconds / tempo, 2);
                                c.EndTimeSeconds = Math.Round(c.EndTimeSeconds / tempo, 2);
                            }
                        }

                        if (cues.Count == 0)
                        {
                            cues = GenerateFallbackCues(text, totalDuration);
                        }
                        else
                        {
                            cues[0].StartTimeSeconds = 0.0;
                            cues[^1].EndTimeSeconds = totalDuration;
                        }

                        await _subtitleGenerator.GenerateSrtAsync(cues, srtFilePath, cancellationToken);
                        await _subtitleGenerator.GenerateVttAsync(cues, vttFilePath, cancellationToken);

                        synthesizedSuccessfully = true;
                        _logger.LogInformation("Edge-TTS synthesized real voiceover successfully: {Path} ({Duration}s)",
                            audioFilePath, totalDuration);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Edge-TTS execution encountered an error, falling back to tone synthesis.");
            }
            finally
            {
                try { if (File.Exists(tempInputTxt)) File.Delete(tempInputTxt); } catch { }
                try { if (File.Exists(tempMp3Path)) File.Delete(tempMp3Path); } catch { }
                try { if (File.Exists(tempSrtPath)) File.Delete(tempSrtPath); } catch { }
            }
        }

        // Outside simulation mode a failed synthesis is a real error — never ship a placeholder tone as "voice-over"
        if (!synthesizedSuccessfully && !NewsStudio.Infrastructure.Common.SimulationMode.IsEnabled(_configuration))
        {
            var reason = pythonExe == null
                ? "không tìm thấy Python (cần cài Python và gói edge-tts)"
                : ffmpegExe == null
                    ? "không tìm thấy FFmpeg trên máy chủ"
                    : "Edge-TTS không tạo được âm thanh (kiểm tra kết nối mạng và gói edge-tts)";
            throw new InvalidOperationException($"Tạo giọng đọc thất bại: {reason}.");
        }

        // Simulation mode only: predictable tone synthesis so offline tests can run
        if (!synthesizedSuccessfully)
        {
            _logger.LogWarning("Simulation mode: using fallback audio tone synthesis for project {ProjectId}", request.ProjectId);
            totalDuration = estimatedDuration;
            cues = GenerateFallbackCues(text, totalDuration);

            await _subtitleGenerator.GenerateSrtAsync(cues, srtFilePath, cancellationToken);
            await _subtitleGenerator.GenerateVttAsync(cues, vttFilePath, cancellationToken);
            await WriteSynthesizedWavAudioAsync(audioFilePath, totalDuration, cancellationToken);
        }

        var fileInfo = new FileInfo(audioFilePath);
        var hash = await ComputeFileSha256Async(audioFilePath, cancellationToken);

        return new TtsSynthesisResult
        {
            AudioFilePath = audioFilePath,
            AudioDurationSeconds = totalDuration,
            SubtitleFilePath = srtFilePath,
            SubtitleVttFilePath = vttFilePath,
            Subtitles = cues,
            Format = "audio/wav",
            FileSizeBytes = fileInfo.Length,
            Sha256Hash = hash
        };
    }

    private static string? FindPythonExecutable()
    {
        var candidates = new[]
        {
            @"C:\Users\Admin\AppData\Local\Programs\Python\Python312\python.exe",
            "python3",          // Linux / Docker (Railway)
            "python.exe",
            "python",
            "py.exe",
            "py"
        };

        foreach (var c in candidates)
        {
            try
            {
                if (File.Exists(c)) return c;
                var psi = new ProcessStartInfo
                {
                    FileName = c,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(1500);
                    if (proc.ExitCode == 0) return c;
                }
            }
            catch { }
        }
        return null;
    }

    internal static string? FindFfmpegExecutable()
    {
        var candidates = new[]
        {
            @"D:\tools\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe",
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg"),
            "ffmpeg.exe",
            "ffmpeg"
        };

        foreach (var c in candidates)
        {
            try
            {
                if (File.Exists(c)) return c;
                var psi = new ProcessStartInfo
                {
                    FileName = c,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(1500);
                    if (proc.ExitCode == 0) return c;
                }
            }
            catch { }
        }
        return null;
    }

    internal static string? FindFfprobeExecutable()
    {
        var candidates = new[]
        {
            @"D:\tools\ffmpeg-master-latest-win64-gpl\bin\ffprobe.exe",
            Path.Combine(AppContext.BaseDirectory, "ffprobe.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffprobe"),
            "ffprobe.exe",
            "ffprobe"
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        return null;
    }

    internal static async Task<bool> RunProcessAsync(string exe, string args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return false;

        await proc.WaitForExitAsync(cancellationToken);
        return proc.ExitCode == 0;
    }

    internal static async Task<string?> RunProcessWithOutputAsync(string exe, string args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return null;

        var output = await proc.StandardOutput.ReadToEndAsync(cancellationToken);
        await proc.WaitForExitAsync(cancellationToken);
        return proc.ExitCode == 0 ? output : null;
    }

    private static List<SubtitleCueDto> ParseSrt(string srtContent)
    {
        var list = new List<SubtitleCueDto>();
        var pattern = @"(?:\r?\n|^)(\d+)\s*\r?\n(\d{2}):(\d{2}):(\d{2})[,.](\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2})[,.](\d{3})\s*\r?\n([\s\S]*?)(?=(?:\r?\n\s*\r?\n\d+\s*\r?\n|\z))";
        var matches = Regex.Matches(srtContent, pattern);

        int idx = 1;
        foreach (Match m in matches)
        {
            double start = int.Parse(m.Groups[2].Value) * 3600 +
                           int.Parse(m.Groups[3].Value) * 60 +
                           int.Parse(m.Groups[4].Value) +
                           int.Parse(m.Groups[5].Value) / 1000.0;

            double end = int.Parse(m.Groups[6].Value) * 3600 +
                         int.Parse(m.Groups[7].Value) * 60 +
                         int.Parse(m.Groups[8].Value) +
                         int.Parse(m.Groups[9].Value) / 1000.0;

            var rawLines = m.Groups[10].Value.Trim().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var text = string.Join(" ", rawLines).Trim();

            if (!string.IsNullOrEmpty(text))
            {
                list.Add(new SubtitleCueDto
                {
                    Index = idx++,
                    StartTimeSeconds = Math.Round(start, 2),
                    EndTimeSeconds = Math.Round(end, 2),
                    Text = text
                });
            }
        }

        return list;
    }

    internal static List<SubtitleCueDto> GenerateFallbackCues(string text, double totalDuration)
    {
        var sentences = SplitSentences(text);
        var totalWords = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        if (totalWords == 0) totalWords = 1;

        var cues = new List<SubtitleCueDto>();
        double currentStart = 0;

        for (int i = 0; i < sentences.Count; i++)
        {
            var s = sentences[i];
            var sWords = s.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (sWords == 0) sWords = 1;

            double duration;
            if (i == sentences.Count - 1)
            {
                duration = Math.Round(totalDuration - currentStart, 2);
                if (duration < 2.0) duration = 2.0;
            }
            else
            {
                duration = Math.Round(((double)sWords / totalWords) * totalDuration, 2);
                if (duration < 2.5) duration = 2.5;
            }

            var end = Math.Round(currentStart + duration, 2);

            cues.Add(new SubtitleCueDto
            {
                Index = i + 1,
                StartTimeSeconds = currentStart,
                EndTimeSeconds = end,
                Text = s
            });

            currentStart = end;
        }

        if (cues.Count > 0)
        {
            cues[0].StartTimeSeconds = 0.0;
            cues[^1].EndTimeSeconds = totalDuration;
        }

        return cues;
    }

    private static List<string> SplitSentences(string text)
    {
        var raw = Regex.Split(text, @"(?<=[.!?])\s+");
        var list = new List<string>();
        foreach (var r in raw)
        {
            var clean = r.Trim();
            if (clean.Length > 5)
            {
                list.Add(clean);
            }
        }

        if (list.Count == 0)
        {
            list.Add(text);
        }

        return list;
    }

    private static async Task WriteSynthesizedWavAudioAsync(string outputPath, double durationSeconds, CancellationToken cancellationToken)
    {
        int sampleRate = 24000;
        short channels = 1;
        short bitsPerSample = 16;
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        short blockAlign = (short)(channels * (bitsPerSample / 8));
        int totalSamples = (int)(sampleRate * durationSeconds);
        int dataSize = totalSamples * (bitsPerSample / 8);
        int fileSize = 36 + dataSize;

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);

        // RIFF header
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(fileSize);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16); // SubChunk1Size (16 for PCM)
        bw.Write((short)1); // AudioFormat (1 for PCM)
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write(bitsPerSample);

        // data chunk
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);

        byte[] buffer = new byte[4096];
        int bytesRemaining = dataSize;
        double phase = 0.0;
        double freq = 220.0;

        while (bytesRemaining > 0 && !cancellationToken.IsCancellationRequested)
        {
            int bytesToWrite = Math.Min(buffer.Length, bytesRemaining);
            int samplesToWrite = bytesToWrite / 2;

            for (int i = 0; i < samplesToWrite; i++)
            {
                double sample = Math.Sin(phase) * 0.25;
                short sampleInt16 = (short)(sample * short.MaxValue);
                buffer[i * 2] = (byte)(sampleInt16 & 0xFF);
                buffer[i * 2 + 1] = (byte)((sampleInt16 >> 8) & 0xFF);

                phase += 2.0 * Math.PI * freq / sampleRate;
                if (phase > 2.0 * Math.PI) phase -= 2.0 * Math.PI;
            }

            await fs.WriteAsync(buffer.AsMemory(0, bytesToWrite), cancellationToken);
            bytesRemaining -= bytesToWrite;
        }

        await fs.FlushAsync(cancellationToken);
    }

    internal static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var bytes = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

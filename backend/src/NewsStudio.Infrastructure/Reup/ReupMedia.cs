using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NewsStudio.Application.DTOs;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Infrastructure.Reup;

/// <summary>What ffprobe says about a video / picture / audio file.</summary>
public sealed record ReupProbe(double Duration, string FpsText, double Fps, int Width, int Height, bool HasVideo, bool HasAudio,
    string? VideoCodec, string? PixelFormat, long? Frames);

/// <summary>FFmpeg / ffprobe work of Reup Video: reading files, thumbnails, the audio step and the output checks.</summary>
public class ReupMedia
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public string Ffmpeg { get; }
    public string Ffprobe { get; }

    public ReupMedia(IConfiguration configuration)
    {
        var configured = configuration["Ffmpeg:BinaryPath"];
        Ffmpeg = !string.IsNullOrWhiteSpace(configured) && File.Exists(configured) ? configured : StudioMedia.LocateFfmpeg() ?? "ffmpeg";
        var dir = Path.GetDirectoryName(Ffmpeg);
        var probeName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        Ffprobe = !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, probeName)) ? Path.Combine(dir, probeName) : "ffprobe";
    }

    // ───────────── reading files ─────────────

    public async Task<ReupProbe> ProbeAsync(string path, CancellationToken ct)
    {
        var (code, stdout, stderr) = await RunAsync(Ffprobe, new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path }, ct);
        if (code != 0) throw new InvalidOperationException("Không đọc được file (không phải video / ảnh / âm thanh hợp lệ): " + Tail(stderr));
        return ParseProbe(stdout);
    }

    /// <summary>ffprobe JSON → <see cref="ReupProbe"/>; width / height already turned for phone videos filmed upright (rotation 90 / 270).</summary>
    public static ReupProbe ParseProbe(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        JsonElement? video = null;
        var hasAudio = false;
        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var s in streams.EnumerateArray())
            {
                var type = s.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
                var attached = s.TryGetProperty("disposition", out var d) && d.TryGetProperty("attached_pic", out var ap) && ap.GetInt32() == 1;
                if (type == "video" && video == null && !attached) video = s;
                if (type == "audio") hasAudio = true;
            }
        }
        double Num(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && double.TryParse(v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText(), NumberStyles.Float, Inv, out var x) ? x : 0;

        var duration = root.TryGetProperty("format", out var fmt) ? Num(fmt, "duration") : 0;
        if (video == null) return new ReupProbe(duration, "0/1", 0, 0, 0, false, hasAudio, null, null, null);
        var v = video.Value;
        var w = v.TryGetProperty("width", out var wv) ? wv.GetInt32() : 0;
        var h = v.TryGetProperty("height", out var hv) ? hv.GetInt32() : 0;
        if (Math.Abs(Rotation(v)) % 180 == 90) (w, h) = (h, w);
        var fpsText = PickFps(v.TryGetProperty("avg_frame_rate", out var a) ? a.GetString() : null, v.TryGetProperty("r_frame_rate", out var r) ? r.GetString() : null);
        var vDur = Num(v, "duration");
        long? frames = v.TryGetProperty("nb_frames", out var nf) && long.TryParse(nf.GetString(), out var n) ? n : null;
        return new ReupProbe(vDur > 0 ? vDur : duration, fpsText, FpsValue(fpsText), w, h, true, hasAudio,
            v.TryGetProperty("codec_name", out var c) ? c.GetString() : null, v.TryGetProperty("pix_fmt", out var pf) ? pf.GetString() : null, frames);
    }

    private static int Rotation(JsonElement v)
    {
        if (v.TryGetProperty("side_data_list", out var sd))
            foreach (var item in sd.EnumerateArray())
                if (item.TryGetProperty("rotation", out var rot) && rot.TryGetInt32(out var deg)) return deg;
        if (v.TryGetProperty("tags", out var tags) && tags.TryGetProperty("rotate", out var rt) && int.TryParse(rt.GetString(), out var r)) return r;
        return 0;
    }

    /// <summary>The frame rate to keep: the average one when sensible, else the stream's base rate; never above 60.</summary>
    public static string PickFps(string? avg, string? raw)
    {
        foreach (var candidate in new[] { avg, raw })
        {
            var value = FpsValue(candidate);
            if (value >= 1 && value <= 60) return candidate!;
        }
        var fallback = FpsValue(raw);
        return fallback > 60 ? "60/1" : "30/1";
    }

    public static double FpsValue(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var parts = text.Split('/');
        if (!double.TryParse(parts[0], NumberStyles.Float, Inv, out var num)) return 0;
        var den = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, Inv, out var d) ? d : 1;
        return den == 0 ? 0 : num / den;
    }

    /// <summary>
    /// Output size: the chosen short side (720 / 1080) with the source's aspect ratio, even numbers (H.264 needs them).
    /// Never larger than the source — upscaling adds pixels, not detail.
    /// </summary>
    public static (int Width, int Height) OutputSize(int srcWidth, int srcHeight, int shortSide)
    {
        if (srcWidth <= 0 || srcHeight <= 0) throw new ArgumentException("Kích thước video nguồn không hợp lệ.");
        var srcShort = Math.Min(srcWidth, srcHeight);
        var target = Math.Min(shortSide, srcShort);
        var scale = target / (double)srcShort;
        static int Even(double x) => Math.Max(2, (int)Math.Round(x / 2.0) * 2);
        return (Even(srcWidth * scale), Even(srcHeight * scale));
    }

    public async Task ThumbnailAsync(string input, string output, bool isImage, double atSeconds, CancellationToken ct)
    {
        var args = new List<string> { "-hide_banner", "-v", "error", "-y" };
        if (!isImage) args.AddRange(new[] { "-ss", atSeconds.ToString("0.###", Inv) });
        args.AddRange(new[] { "-i", input, "-frames:v", "1", "-vf", "scale='min(480,iw)':-2", "-q:v", "4", output });
        var (code, _, err) = await RunAsync(Ffmpeg, args, ct);
        if (code != 0 || !File.Exists(output))
        {
            if (!isImage && atSeconds > 0) { await ThumbnailAsync(input, output, false, 0, ct); return; } // very short clip
            throw new InvalidOperationException("Không tạo được ảnh xem trước: " + Tail(err));
        }
    }

    // ───────────── audio step ─────────────

    /// <summary>
    /// FFmpeg arguments that give the silent processed video its sound, keeping the video's exact length:
    /// Remove (no audio track — pick music in Facebook's library when posting), KeepOriginal, Replace (own file), Mix.
    /// The video stream is copied untouched.
    /// </summary>
    public static List<string> BuildMuxArgs(string mode, string video, string? source, string? audioFile, double durationSeconds,
        double startSeconds, double audioVolume, double originalVolume, string output)
    {
        var d = durationSeconds.ToString("0.###", Inv);
        var args = new List<string> { "-hide_banner", "-v", "error", "-y", "-i", video };
        switch (mode)
        {
            case "Remove":
                args.AddRange(new[] { "-map", "0:v:0", "-c:v", "copy", "-an" });
                break;
            case "KeepOriginal":
                if (source == null) throw new ArgumentException("Thiếu video nguồn để lấy âm thanh gốc.");
                if (startSeconds > 0) args.AddRange(new[] { "-ss", startSeconds.ToString("0.###", Inv) });
                args.AddRange(new[] { "-i", source, "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy",
                    "-af", $"volume={originalVolume.ToString("0.###", Inv)},apad", "-c:a", "aac", "-b:a", "192k", "-t", d });
                break;
            case "Replace":
                if (audioFile == null) throw new ArgumentException("Chưa chọn file âm thanh.");
                args.AddRange(new[] { "-i", audioFile, "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy",
                    "-af", $"volume={audioVolume.ToString("0.###", Inv)},apad", "-c:a", "aac", "-b:a", "192k", "-t", d });
                break;
            case "Mix":
                if (source == null || audioFile == null) throw new ArgumentException("Trộn âm thanh cần cả âm thanh gốc và file âm thanh riêng.");
                if (startSeconds > 0) args.AddRange(new[] { "-ss", startSeconds.ToString("0.###", Inv) });
                args.AddRange(new[] { "-i", source, "-i", audioFile, "-filter_complex",
                    $"[1:a:0]volume={originalVolume.ToString("0.###", Inv)}[o];[2:a:0]volume={audioVolume.ToString("0.###", Inv)}[m];" +
                    "[o][m]amix=inputs=2:duration=longest:dropout_transition=0:normalize=0,apad[a]",
                    "-map", "0:v:0", "-map", "[a]", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-t", d });
                break;
            default:
                throw new ArgumentException($"Chế độ âm thanh không hợp lệ: {mode}");
        }
        args.AddRange(new[] { "-movflags", "+faststart", output });
        return args;
    }

    public async Task MuxAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var (code, _, err) = await RunAsync(Ffmpeg, args, ct);
        if (code != 0) throw new InvalidOperationException("FFmpeg không ghép được âm thanh: " + Tail(err));
    }

    // ───────────── output checks ─────────────

    /// <summary>What the finished file must match.</summary>
    public sealed record Expectation(double DurationSeconds, double Fps, int Width, int Height, bool WithAudio);

    /// <summary>Checks the finished MP4 before it is called done: it decodes end to end, H.264, right size, FPS, length, audio, not black.</summary>
    public async Task<List<ReupQcCheckDto>> VerifyAsync(string path, Expectation expected, CancellationToken ct)
    {
        var checks = new List<ReupQcCheckDto>();
        ReupProbe p;
        try { p = await ProbeAsync(path, ct); }
        catch (InvalidOperationException ex)
        {
            checks.Add(new ReupQcCheckDto { Name = "Đọc được file", Ok = false, Detail = ex.Message });
            return checks;
        }
        // blackdetect reports at "info" level; the log is filtered down to its results and real problems
        var (_, _, err) = await RunAsync(Ffmpeg, new[] { "-hide_banner", "-v", "info", "-nostats", "-i", path, "-map", "0:v:0",
            "-vf", "blackdetect=d=1:pix_th=0.06", "-f", "null", "-" }, ct, captureInfo: true);
        checks.AddRange(Evaluate(p, expected, err));
        return checks;
    }

    /// <summary>The rules of <see cref="VerifyAsync"/> on a probe result and the decode pass's messages (pure; tested).</summary>
    public static List<ReupQcCheckDto> Evaluate(ReupProbe p, Expectation e, string decodeLog)
    {
        var list = new List<ReupQcCheckDto>();
        void Add(string name, bool ok, string detail) => list.Add(new ReupQcCheckDto { Name = name, Ok = ok, Detail = detail });

        Add("Có hình", p.HasVideo, p.HasVideo ? "Có luồng video" : "Không có luồng video");
        Add("Chuẩn H.264", p.VideoCodec == "h264" && p.PixelFormat == "yuv420p", $"{p.VideoCodec} / {p.PixelFormat}");
        Add("Kích thước", p.Width == e.Width && p.Height == e.Height, $"{p.Width}×{p.Height} (cần {e.Width}×{e.Height})");
        Add("FPS giữ nguyên", Math.Abs(p.Fps - e.Fps) < 0.02, $"{p.Fps:0.###} (nguồn {e.Fps:0.###})");
        var tolerance = Math.Max(0.15, 2.5 / Math.Max(1, e.Fps));
        Add("Thời lượng giữ nguyên", Math.Abs(p.Duration - e.DurationSeconds) <= tolerance, $"{p.Duration:0.00}s (nguồn {e.DurationSeconds:0.00}s)");
        Add("Âm thanh", p.HasAudio == e.WithAudio, e.WithAudio ? (p.HasAudio ? "Có âm thanh" : "Thiếu âm thanh") : (p.HasAudio ? "Lẽ ra không có âm thanh" : "Không âm thanh (chọn nhạc khi đăng)"));

        var lines = decodeLog.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var errors = lines.Where(l => !l.Contains("blackdetect", StringComparison.OrdinalIgnoreCase)).ToList();
        Add("Giải mã trọn vẹn", errors.Count == 0, errors.Count == 0 ? "Không lỗi" : string.Join(" | ", errors.Take(3)));

        double black = 0;
        foreach (var l in lines.Where(l => l.Contains("black_duration:", StringComparison.Ordinal)))
        {
            var i = l.IndexOf("black_duration:", StringComparison.Ordinal) + "black_duration:".Length;
            var token = new string(l[i..].TrimStart().TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray());
            if (double.TryParse(token, NumberStyles.Float, Inv, out var x)) black += x;
        }
        var share = p.Duration > 0 ? black / p.Duration : 0;
        Add("Không bị đen hình", share < 0.5, black <= 0 ? "Không có đoạn đen" : $"Đoạn đen tổng {black:0.0}s ({share:P0})");
        return list;
    }

    // ───────────── processes ─────────────

    public static string Tail(string s)
    {
        s = s.Trim();
        return s.Length > 600 ? "…" + s[^600..] : s;
    }

    public static async Task<(int Code, string Stdout, string Stderr)> RunAsync(string exe, IEnumerable<string> args, CancellationToken ct, bool captureInfo = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Không chạy được {exe}.");
        try
        {
            var stderr = proc.StandardError.ReadToEndAsync(ct);
            var stdout = proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            var err = await stderr;
            if (captureInfo) err = FilterInfo(err);
            return (proc.ExitCode, await stdout, err);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }
    }

    /// <summary>From an info-level FFmpeg log keep the blackdetect results and real problems, drop the banner / stream listing.</summary>
    private static string FilterInfo(string log) => string.Join("\n", log.Split('\n').Select(l => l.TrimEnd('\r')).Where(l =>
        l.Contains("blackdetect", StringComparison.OrdinalIgnoreCase)
        || l.Contains("error", StringComparison.OrdinalIgnoreCase)
        || l.Contains("corrupt", StringComparison.OrdinalIgnoreCase)
        || l.Contains("invalid", StringComparison.OrdinalIgnoreCase)));
}

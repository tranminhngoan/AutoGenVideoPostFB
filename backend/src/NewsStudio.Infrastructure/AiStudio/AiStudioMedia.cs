using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NewsStudio.Application.DTOs;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Infrastructure.AiStudio;

/// <summary>What ffprobe says about a reference video.</summary>
public sealed record AiStudioProbe(double Duration, string FpsText, double Fps, int Width, int Height, bool HasVideo,
    string? VideoCodec, long? BitrateKbps, List<AiStudioAudioTrackDto> Audio, string? FormatName);

/// <summary>FFmpeg / ffprobe work of AI Video Studio: checking uploads, thumbnails, last frames, joining clips.</summary>
public class AiStudioMedia
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public string Ffmpeg { get; }
    public string Ffprobe { get; }

    public AiStudioMedia(IConfiguration configuration)
    {
        var configured = configuration["Ffmpeg:BinaryPath"];
        Ffmpeg = !string.IsNullOrWhiteSpace(configured) && File.Exists(configured) ? configured : StudioMedia.LocateFfmpeg() ?? "ffmpeg";
        var dir = Path.GetDirectoryName(Ffmpeg);
        var probeName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        Ffprobe = !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, probeName)) ? Path.Combine(dir, probeName) : "ffprobe";
    }

    // ───────────── upload checks ─────────────

    /// <summary>
    /// The real container from the first bytes (never the file name / browser type): "mp4" (ISO BMFF: MP4 / M4V),
    /// "mov" (QuickTime), "webm" (Matroska / WebM) — null when it is none of them (a renamed file, a program…).
    /// </summary>
    public static string? SniffContainer(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 4 && head[0] == 0x1A && head[1] == 0x45 && head[2] == 0xDF && head[3] == 0xA3) return "webm";
        if (head.Length >= 12)
        {
            var box = Encoding.ASCII.GetString(head.Slice(4, 4));
            if (box == "ftyp")
            {
                var brand = Encoding.ASCII.GetString(head.Slice(8, 4));
                return brand == "qt  " ? "mov" : "mp4";
            }
            if (box is "moov" or "mdat" or "wide" or "free" or "skip") return "mov"; // old QuickTime files start with these atoms
        }
        return null;
    }

    public static async Task<string?> SniffFileAsync(string path, CancellationToken ct)
    {
        var buf = new byte[16];
        await using var fs = File.OpenRead(path);
        var n = await fs.ReadAsync(buf, ct);
        return SniffContainer(buf.AsSpan(0, n));
    }

    public async Task<AiStudioProbe> ProbeAsync(string path, CancellationToken ct)
    {
        var (code, stdout, stderr) = await RunAsync(Ffprobe, new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path }, ct);
        if (code != 0) throw new InvalidOperationException("Không đọc được video (file hỏng hoặc không phải video): " + Tail(stderr));
        return ParseProbe(stdout);
    }

    /// <summary>ffprobe JSON → <see cref="AiStudioProbe"/>; width / height already turned for phone videos filmed upright.</summary>
    public static AiStudioProbe ParseProbe(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        JsonElement? video = null;
        var audio = new List<AiStudioAudioTrackDto>();
        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var s in streams.EnumerateArray())
            {
                var type = s.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
                var attached = s.TryGetProperty("disposition", out var d) && d.TryGetProperty("attached_pic", out var ap) && ap.GetInt32() == 1;
                if (type == "video" && video == null && !attached) video = s;
                if (type == "audio")
                    audio.Add(new AiStudioAudioTrackDto
                    {
                        Codec = s.TryGetProperty("codec_name", out var ac) ? ac.GetString() : null,
                        SampleRate = (int)Num(s, "sample_rate"),
                        Channels = s.TryGetProperty("channels", out var ch) && ch.TryGetInt32(out var c) ? c : 0
                    });
            }
        }
        var fmt = root.TryGetProperty("format", out var f) ? f : default;
        var duration = fmt.ValueKind == JsonValueKind.Object ? Num(fmt, "duration") : 0;
        var formatName = fmt.ValueKind == JsonValueKind.Object && fmt.TryGetProperty("format_name", out var fn) ? fn.GetString() : null;
        var bitrate = fmt.ValueKind == JsonValueKind.Object ? Num(fmt, "bit_rate") : 0;
        if (video == null) return new AiStudioProbe(duration, "0/1", 0, 0, 0, false, null, bitrate > 0 ? (long)(bitrate / 1000) : null, audio, formatName);
        var v = video.Value;
        var w = v.TryGetProperty("width", out var wv) ? wv.GetInt32() : 0;
        var h = v.TryGetProperty("height", out var hv) ? hv.GetInt32() : 0;
        if (Math.Abs(Rotation(v)) % 180 == 90) (w, h) = (h, w);
        var fpsText = PickFps(v.TryGetProperty("avg_frame_rate", out var a) ? a.GetString() : null, v.TryGetProperty("r_frame_rate", out var r) ? r.GetString() : null);
        var vDur = Num(v, "duration");
        if (bitrate <= 0) bitrate = Num(v, "bit_rate");
        return new AiStudioProbe(vDur > 0 ? vDur : duration, fpsText, FpsValue(fpsText), w, h, true,
            v.TryGetProperty("codec_name", out var c2) ? c2.GetString() : null, bitrate > 0 ? (long)Math.Round(bitrate / 1000) : null, audio, formatName);
    }

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && double.TryParse(v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText(), NumberStyles.Float, Inv, out var x) ? x : 0;

    private static int Rotation(JsonElement v)
    {
        if (v.TryGetProperty("side_data_list", out var sd))
            foreach (var item in sd.EnumerateArray())
                if (item.TryGetProperty("rotation", out var rot) && rot.TryGetInt32(out var deg)) return deg;
        if (v.TryGetProperty("tags", out var tags) && tags.TryGetProperty("rotate", out var rt) && int.TryParse(rt.GetString(), out var r)) return r;
        return 0;
    }

    public static string PickFps(string? avg, string? raw)
    {
        foreach (var candidate in new[] { avg, raw })
        {
            var value = FpsValue(candidate);
            if (value >= 1 && value <= 120) return candidate!;
        }
        return "30/1";
    }

    public static double FpsValue(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var parts = text.Split('/');
        if (!double.TryParse(parts[0], NumberStyles.Float, Inv, out var num)) return 0;
        var den = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, Inv, out var d) ? d : 1;
        return den == 0 ? 0 : num / den;
    }

    /// <summary>"9:16", "16:9", "1:1", "4:5"… — the nearest common shape.</summary>
    public static string AspectOf(int w, int h)
    {
        if (w <= 0 || h <= 0) return "9:16";
        var r = w / (double)h;
        var known = new (string Name, double Ratio)[] { ("9:16", 9 / 16.0), ("3:4", 0.75), ("4:5", 0.8), ("1:1", 1), ("4:3", 4 / 3.0), ("16:9", 16 / 9.0), ("21:9", 21 / 9.0) };
        return known.MinBy(k => Math.Abs(Math.Log(r / k.Ratio))).Name;
    }

    /// <summary>Seedance makes 9:16 or 16:9 clips: the one closest to the reference.</summary>
    public static string GenerationAspect(int w, int h) => w > h ? "16:9" : "9:16";

    /// <summary>Decodes the first seconds and the end: a truncated or corrupt file fails here instead of on the PC.</summary>
    public async Task CheckDecodesAsync(string path, double duration, CancellationToken ct)
    {
        var (code, _, err) = await RunAsync(Ffmpeg, new[] { "-hide_banner", "-v", "error", "-xerror", "-t", "2", "-i", path, "-map", "0:v:0", "-f", "null", "-" }, ct);
        if (code != 0) throw new ArgumentException("Video bị hỏng, không giải mã được: " + Tail(err));
        if (duration > 4)
        {
            var at = Math.Max(0, duration - 2).ToString("0.###", Inv);
            (code, _, err) = await RunAsync(Ffmpeg, new[] { "-hide_banner", "-v", "error", "-ss", at, "-i", path, "-map", "0:v:0", "-f", "null", "-" }, ct);
            if (code != 0 || err.Contains("Invalid data", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Cuối video bị hỏng / thiếu (tải lên chưa trọn?): " + Tail(err));
        }
    }

    public async Task ThumbnailAsync(string input, string output, double atSeconds, int maxSide, CancellationToken ct)
    {
        var args = new List<string> { "-hide_banner", "-v", "error", "-y", "-ss", atSeconds.ToString("0.###", Inv), "-i", input,
            "-frames:v", "1", "-vf", $"scale='if(gt(iw,ih),min({maxSide},iw),-2)':'if(gt(iw,ih),-2,min({maxSide},ih))'", "-q:v", "4", output };
        var (code, _, err) = await RunAsync(Ffmpeg, args, ct);
        if (code != 0 || !File.Exists(output))
        {
            if (atSeconds > 0) { await ThumbnailAsync(input, output, 0, maxSide, ct); return; }
            throw new InvalidOperationException("Không tạo được ảnh xem trước: " + Tail(err));
        }
    }

    /// <summary>The last frame of a clip as a JPEG (the first frame of the next scene in continuity mode).</summary>
    public async Task LastFrameAsync(string clip, string output, CancellationToken ct)
    {
        var (code, _, err) = await RunAsync(Ffmpeg, new[] { "-hide_banner", "-v", "error", "-y", "-sseof", "-0.25", "-i", clip,
            "-update", "1", "-q:v", "2", output }, ct);
        if (code != 0 || !File.Exists(output)) throw new InvalidOperationException("Không lấy được khung cuối của clip trước: " + Tail(err));
    }

    /// <summary>
    /// Joins clips in order. Same model / resolution / shape = stream copy (no re-encode, nearly free CPU); otherwise
    /// one H.264 re-encode to the first clip's size.
    /// </summary>
    public async Task ConcatAsync(IReadOnlyList<string> clips, string output, bool sameFormat, int width, int height, CancellationToken ct)
    {
        if (clips.Count == 0) throw new InvalidOperationException("Chưa có clip nào để ghép.");
        var dir = Path.GetDirectoryName(output)!;
        List<string> args;
        if (sameFormat)
        {
            var list = Path.Combine(dir, "list.txt");
            await File.WriteAllLinesAsync(list, clips.Select(c => $"file '{c.Replace("\\", "/").Replace("'", "'\\''")}'"), ct);
            args = new List<string> { "-hide_banner", "-v", "error", "-y", "-f", "concat", "-safe", "0", "-i", list, "-map", "0:v:0", "-c", "copy", "-an", "-movflags", "+faststart", output };
        }
        else
        {
            args = new List<string> { "-hide_banner", "-v", "error", "-y" };
            foreach (var c in clips) args.AddRange(new[] { "-i", c });
            var filter = new StringBuilder();
            for (var i = 0; i < clips.Count; i++)
                filter.Append(Inv, $"[{i}:v:0]scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=24,format=yuv420p[v{i}];");
            for (var i = 0; i < clips.Count; i++) filter.Append(Inv, $"[v{i}]");
            filter.Append(Inv, $"concat=n={clips.Count}:v=1:a=0[out]");
            args.AddRange(new[] { "-filter_complex", filter.ToString(), "-map", "[out]", "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-movflags", "+faststart", output });
        }
        var (code, _, err) = await RunAsync(Ffmpeg, args, ct);
        if (code != 0 || !File.Exists(output)) throw new InvalidOperationException("FFmpeg không ghép được các clip: " + Tail(err));
    }

    // ───────────── original sound ─────────────

    /// <summary>The reference video's audio as a small AAC file (kept for the joined video after the video expires).</summary>
    public async Task ExtractAudioAsync(string input, string output, CancellationToken ct)
    {
        var (code, _, err) = await RunAsync(Ffmpeg, new[] { "-hide_banner", "-v", "error", "-y", "-i", input, "-vn", "-map", "0:a:0",
            "-ac", "2", "-ar", "44100", "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart", output }, ct);
        if (code != 0 || !File.Exists(output)) throw new InvalidOperationException("Không tách được âm thanh của video: " + Tail(err));
    }

    /// <summary>One clip of the joined video and the part of the reference sound it replaces.</summary>
    public sealed record SoundPiece(double SourceStart, double SourceEnd, double ClipSeconds);

    /// <summary>
    /// filter_complex that gives every clip the reference sound of its own scene: cut [start, end], played up to 25 %
    /// faster / slower when the clip is a little shorter / longer (voices stay natural), then padded with silence or
    /// trimmed to the clip's exact length, and the pieces joined in clip order. Input 1 = the audio file.
    /// </summary>
    public static string BuildSoundFilter(IReadOnlyList<SoundPiece> pieces)
    {
        var f = new StringBuilder();
        for (var i = 0; i < pieces.Count; i++)
        {
            var p = pieces[i];
            var len = Math.Max(0.05, p.SourceEnd - p.SourceStart);
            var clip = Math.Max(0.1, p.ClipSeconds);
            var ratio = Math.Clamp(len / clip, 0.8, 1.25);
            var tempo = Math.Abs(ratio - 1) > 0.03 ? $",atempo={ratio.ToString("0.####", Inv)}" : "";
            f.Append(Inv, $"[1:a:0]atrim=start={p.SourceStart.ToString("0.###", Inv)}:end={p.SourceEnd.ToString("0.###", Inv)},asetpts=PTS-STARTPTS{tempo},")
             .Append(Inv, $"apad=whole_dur={clip.ToString("0.###", Inv)},atrim=0:{clip.ToString("0.###", Inv)},asetpts=PTS-STARTPTS[a{i}];");
        }
        for (var i = 0; i < pieces.Count; i++) f.Append(Inv, $"[a{i}]");
        f.Append(Inv, $"concat=n={pieces.Count}:v=0:a=1[aout]");
        return f.ToString();
    }

    /// <summary>Adds the fitted reference sound to the joined (silent) video; the picture is copied untouched.</summary>
    public async Task AddSoundAsync(string video, string audio, IReadOnlyList<SoundPiece> pieces, string output, CancellationToken ct)
    {
        var (code, _, err) = await RunAsync(Ffmpeg, new[] { "-hide_banner", "-v", "error", "-y", "-i", video, "-i", audio,
            "-filter_complex", BuildSoundFilter(pieces), "-map", "0:v:0", "-map", "[aout]", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
            "-shortest", "-movflags", "+faststart", output }, ct);
        if (code != 0 || !File.Exists(output)) throw new InvalidOperationException("Không ghép được lời bình gốc vào video: " + Tail(err));
    }

    // ───────────── processes ─────────────

    public static string Tail(string s)
    {
        s = s.Trim();
        return s.Length > 600 ? "…" + s[^600..] : s;
    }

    public static async Task<(int Code, string Stdout, string Stderr)> RunAsync(string exe, IEnumerable<string> args, CancellationToken ct)
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
            return (proc.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }
    }
}

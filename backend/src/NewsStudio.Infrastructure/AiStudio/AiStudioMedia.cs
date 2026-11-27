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

    /// <summary>Veo makes 9:16 or 16:9 clips: the one closest to the reference.</summary>
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

    /// <summary>
    /// One clip of the joined video and the narrator's take for it (null = no narration in that scene). With
    /// <paramref name="From"/>, only <paramref name="VoiceSeconds"/> of the take starting there is played (a scene's
    /// part of the one continuous reading).
    /// </summary>
    public sealed record VoicePiece(string? VoicePath, double VoiceSeconds, double ClipSeconds, double? From = null);

    /// <summary>
    /// Where every take plays in the joined video. A voice-over runs on across a cut like the reference does: each
    /// take starts with its own clip (or just after the previous take when that one ran over), a take longer than its
    /// clip carries on over the next scene's picture. Only when the takes would outlast the whole video are they all
    /// read a little faster (up to 25 %, pitch kept), and past that the last picture is held so no word is cut.
    /// </summary>
    public sealed record VoicePlan(double Tempo, IReadOnlyList<double> Starts, double VideoSeconds, double HoldSeconds);

    public const double VoiceGap = 0.3, VoiceTail = 0.4, MaxVoiceTempo = 1.25;

    public static VoicePlan PlanVoices(IReadOnlyList<VoicePiece> pieces)
    {
        var video = pieces.Sum(p => Math.Max(0.1, p.ClipSeconds));
        (List<double> Starts, double End) Lay(double tempo)
        {
            var starts = new List<double>();
            double at = 0, end = 0;
            var any = false;
            foreach (var p in pieces)
            {
                if (p.VoicePath != null)
                {
                    var start = any ? Math.Max(at, end + VoiceGap) : at;
                    starts.Add(start);
                    end = start + Math.Max(0, p.VoiceSeconds) / tempo;
                    any = true;
                }
                else starts.Add(at);
                at += Math.Max(0.1, p.ClipSeconds);
            }
            return (starts, end);
        }

        var room = video - VoiceTail;
        var plain = Lay(1.0);
        if (plain.End <= room) return new VoicePlan(1.0, plain.Starts, video, 0);
        var fastest = Lay(MaxVoiceTempo);
        if (fastest.End > room) return new VoicePlan(MaxVoiceTempo, fastest.Starts, video, Math.Round(fastest.End - room, 2));
        double lo = 1.0, hi = MaxVoiceTempo; // the gentlest speed-up that fits
        for (var i = 0; i < 30; i++)
        {
            var mid = (lo + hi) / 2;
            if (Lay(mid).End <= room) hi = mid; else lo = mid;
        }
        var tempo = Math.Ceiling(hi * 1000) / 1000;
        return new VoicePlan(tempo, Lay(tempo).Starts, video, 0);
    }

    /// <summary>
    /// filter_complex for the plan: silence the length of the (held) video, every take mixed in at its start.
    /// Input 0 = the video; the takes are inputs 1… in clip order.
    /// </summary>
    public static string BuildVoiceFilter(IReadOnlyList<VoicePiece> pieces, VoicePlan plan)
    {
        var f = new StringBuilder();
        var total = (plan.VideoSeconds + plan.HoldSeconds).ToString("0.###", Inv);
        if (plan.HoldSeconds > 0) f.Append(Inv, $"[0:v:0]tpad=stop_mode=clone:stop_duration={plan.HoldSeconds.ToString("0.###", Inv)}[vout];");
        f.Append(Inv, $"anullsrc=r=48000:cl=stereo,atrim=0:{total},asetpts=PTS-STARTPTS[base];");
        var tempo = plan.Tempo > 1.001 ? $",atempo={plan.Tempo.ToString("0.###", Inv)}" : "";
        var input = 1;
        var mixed = new StringBuilder("[base]");
        for (var i = 0; i < pieces.Count; i++)
        {
            if (pieces[i].VoicePath == null) continue;
            var ms = (long)Math.Round(plan.Starts[i] * 1000);
            var part = pieces[i].From is { } from
                ? $"atrim=start={from.ToString("0.###", Inv)}:end={(from + pieces[i].VoiceSeconds).ToString("0.###", Inv)},asetpts=PTS-STARTPTS,"
                : "";
            f.Append(Inv, $"[{input}:a:0]{part}aformat=sample_rates=48000:channel_layouts=stereo,asetpts=PTS-STARTPTS{tempo},adelay={ms}:all=1[v{input}];");
            mixed.Append(Inv, $"[v{input}]");
            input++;
        }
        f.Append(mixed).Append(Inv, $"amix=inputs={input}:duration=first:dropout_transition=0:normalize=0[aout]");
        return f.ToString();
    }

    /// <summary>The pauses in a take (start, end) — where the narrator breathes between sentences.</summary>
    public async Task<List<(double Start, double End)>> SilencesAsync(string audio, CancellationToken ct)
    {
        var (code, _, err) = await RunAsync(Ffmpeg, new[] { "-hide_banner", "-nostats", "-v", "info", "-i", audio,
            "-af", "silencedetect=noise=-35dB:d=0.22", "-f", "null", "-" }, ct);
        if (code != 0) throw new InvalidOperationException("Không đọc được bản lời bình: " + Tail(err));
        var found = new List<(double, double)>();
        double? open = null;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(err, @"silence_(start|end): (-?[0-9.]+)"))
        {
            var t = double.Parse(m.Groups[2].Value, Inv);
            if (m.Groups[1].Value == "start") open = Math.Max(0, t);
            else if (open is { } o) { found.Add((o, t)); open = null; }
        }
        return found;
    }

    /// <summary>
    /// Where to cut one continuous reading of several scenes' words so that each scene's part plays with its own clip:
    /// for every border between two parts, the pause nearest to where that border should fall (by the share of words
    /// still to be read), or null when no pause is near (the parts then stay together — never cut inside a word).
    /// </summary>
    public static List<double?> ChooseCuts(double takeSeconds, IReadOnlyList<int> words, IReadOnlyList<(double Start, double End)> pauses)
    {
        var cuts = new List<double?>();
        double from = 0;
        for (var j = 0; j < words.Count - 1; j++)
        {
            var left = words.Skip(j).Sum(w => Math.Max(1, w));
            var expected = from + (takeSeconds - from) * Math.Max(1, words[j]) / left;
            var tolerance = Math.Max(1.5, (takeSeconds - from) * 0.15);
            var best = pauses.Select(x => (Mid: (x.Start + x.End) / 2, x.Start, x.End))
                .Where(x => x.Mid > from + 0.3 && x.Mid < takeSeconds - 0.3 && Math.Abs(x.Mid - expected) <= tolerance)
                .OrderBy(x => Math.Abs(x.Mid - expected) - Math.Min(0.6, x.End - x.Start)) // nearest, a longer breath preferred
                .Select(x => (double?)x.Mid).FirstOrDefault();
            cuts.Add(best);
            if (best is { } c) from = c;
        }
        return cuts;
    }

    /// <summary>
    /// Adds the narrator's takes to the joined (silent) video. The picture is copied untouched — re-encoded only when
    /// its last frame must be held for the end of the narration.
    /// </summary>
    public async Task AddVoicesAsync(string video, IReadOnlyList<VoicePiece> pieces, string output, CancellationToken ct)
    {
        var plan = PlanVoices(pieces);
        var args = new List<string> { "-hide_banner", "-v", "error", "-y", "-i", video };
        foreach (var p in pieces.Where(p => p.VoicePath != null)) { args.Add("-i"); args.Add(p.VoicePath!); } // a part of one take: the same file again
        args.AddRange(new[] { "-filter_complex", BuildVoiceFilter(pieces, plan) });
        if (plan.HoldSeconds > 0)
            args.AddRange(new[] { "-map", "[vout]", "-map", "[aout]", "-c:v", "libx264", "-preset", "medium", "-crf", "16", "-pix_fmt", "yuv420p" });
        else
            args.AddRange(new[] { "-map", "0:v:0", "-map", "[aout]", "-c:v", "copy" });
        args.AddRange(new[] { "-c:a", "aac", "-b:a", "192k", "-shortest", "-movflags", "+faststart", output });
        var (code, _, err) = await RunAsync(Ffmpeg, args, ct);
        if (code != 0 || !File.Exists(output)) throw new InvalidOperationException("Không ghép được lời bình vào video: " + Tail(err));
    }

    /// <summary>
    /// Burns the Page watermark (the studio's moving overlay, see <see cref="StudioMedia.WatermarkFilter"/>) into the
    /// joined video and stamps the Page as artist / copyright. The sound is copied untouched.
    /// </summary>
    public async Task AddWatermarkAsync(string video, string watermarkPng, string aspectRatio, string brand, string output, CancellationToken ct)
    {
        var filter = "[0:v]null[base];" + StudioMedia.WatermarkFilter(aspectRatio, 1, "base", 0);
        var args = new List<string> { "-hide_banner", "-v", "error", "-y", "-i", video, "-i", watermarkPng, "-filter_complex", filter,
            "-map", "[v]", "-map", "0:a?", "-c:v", "libx264", "-preset", "medium", "-crf", "16", "-pix_fmt", "yuv420p", "-c:a", "copy",
            "-metadata", $"artist={brand}", "-metadata", $"copyright=© {brand}", "-metadata", $"comment=Video gốc của Page {brand} trên Facebook",
            "-movflags", "+faststart", output };
        var (code, _, err) = await RunAsync(Ffmpeg, args, ct);
        if (code != 0 || !File.Exists(output)) throw new InvalidOperationException("Không gắn được logo Page vào video: " + Tail(err));
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

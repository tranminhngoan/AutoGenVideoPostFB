using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Infrastructure.Growth;

/// <summary>
/// FFmpeg work of the autopilot (the same FFmpeg the studio uses): finding the liveliest part of a video for a Story,
/// cutting it, and stamping the Page watermark on a picture.
/// </summary>
public partial class GrowthMedia
{
    public const double StorySeconds = 45;
    public const double MaxStorySeconds = 59;

    private static string Ffmpeg => StudioMedia.LocateFfmpeg() ?? throw new InvalidOperationException("Không tìm thấy FFmpeg trên máy chủ.");

    [GeneratedRegex(@"t:\s*([0-9.]+)\s+.*?M:\s*(-?[0-9.]+|-inf|nan)", RegexOptions.IgnoreCase)]
    private static partial Regex EburLine();

    /// <summary>Momentary loudness (LUFS) every 0.1 s, from FFmpeg's ebur128 filter. Empty when the video has no sound.</summary>
    public async Task<List<(double T, double M)>> LoudnessAsync(string video, CancellationToken ct)
    {
        var (code, _, err) = await RunAsync(Ffmpeg, new[] { "-nostats", "-hide_banner", "-i", video, "-vn", "-af", "ebur128", "-f", "null", "-" }, ct);
        if (code != 0) return new();
        return ParseLoudness(err);
    }

    public static List<(double T, double M)> ParseLoudness(string stderr)
    {
        var list = new List<(double, double)>();
        foreach (Match m in EburLine().Matches(stderr))
        {
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) continue;
            var v = double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : -70;
            list.Add((t, Math.Max(-70, v)));
        }
        return list;
    }

    /// <summary>
    /// Start of the <paramref name="length"/>-second window with the highest average loudness (speech, music peaks,
    /// action), not overlapping the windows already used for this video. Null when nothing is left. A video no longer than
    /// a Story is used whole. Without sound, the windows are taken in order from the start.
    /// </summary>
    public static (double Start, double Seconds)? PickWindow(IReadOnlyList<(double T, double M)> loudness, double duration, double length,
        IReadOnlyList<(double Start, double Seconds)> used)
    {
        if (duration <= 0) return null;
        if (duration <= MaxStorySeconds) return used.Count == 0 ? (0, Math.Round(duration, 1)) : null;
        length = Math.Min(length, MaxStorySeconds);
        bool Free(double s) => used.All(u => s + length <= u.Start || s >= u.Start + u.Seconds);
        var starts = new List<double>();
        for (var s = 1.0; s + length <= duration - 0.5; s += 1) starts.Add(s);
        if (starts.Count == 0) starts.Add(0);
        var free = starts.Where(Free).ToList();
        if (free.Count == 0) return null;
        if (loudness.Count < 10) return (free[0], length);
        double Score(double s)
        {
            var w = loudness.Where(p => p.T >= s && p.T < s + length).Select(p => p.M).ToList();
            return w.Count == 0 ? -70 : w.Average();
        }
        var best = free.OrderByDescending(Score).ThenBy(s => s).First();
        return (best, length);
    }

    /// <summary>Cuts <paramref name="seconds"/> from <paramref name="start"/> into a Story-ready MP4 (H.264 / AAC, fast start).</summary>
    public async Task CutAsync(string input, double start, double seconds, string output, CancellationToken ct)
    {
        var (code, _, err) = await RunAsync(Ffmpeg, new[]
        {
            "-y", "-hide_banner", "-ss", start.ToString("0.###", CultureInfo.InvariantCulture), "-i", input,
            "-t", Math.Min(seconds, MaxStorySeconds).ToString("0.###", CultureInfo.InvariantCulture),
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "21", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k",
            "-movflags", "+faststart", output
        }, ct);
        if (code != 0 || !File.Exists(output)) throw new InvalidOperationException("Cắt đoạn video cho Story thất bại: " + Tail(err));
    }

    /// <summary>The Page watermark (bottom-left, ~38 % of the width) on a picture, saved as JPEG.</summary>
    public async Task WatermarkImageAsync(string image, string watermarkPng, string outputJpg, CancellationToken ct)
    {
        var (code, _, err) = await RunAsync(Ffmpeg, new[]
        {
            "-y", "-hide_banner", "-i", image, "-i", watermarkPng, "-filter_complex",
            "[1:v][0:v]scale2ref=w=main_w*0.38:h=ow/mdar[wm][base];[base][wm]overlay=x=main_w*0.04:y=main_h-overlay_h-main_h*0.035",
            "-frames:v", "1", "-q:v", "2", outputJpg
        }, ct);
        if (code != 0 || !File.Exists(outputJpg)) throw new InvalidOperationException("Gắn watermark vào ảnh thất bại: " + Tail(err));
    }

    /// <summary>
    /// A 9:16 Story video from one picture and a narration: the picture slowly zooms in (Ken Burns) for the length of the
    /// voice, the Page watermark sits bottom-left. No subtitles. Returns the video length in seconds.
    /// </summary>
    public async Task MakeStoryVideoAsync(string image, string audio, string? watermarkPng, string output, CancellationToken ct)
    {
        var args = new List<string> { "-y", "-hide_banner", "-loop", "1", "-framerate", "30", "-i", image, "-i", audio };
        var filter = "[0:v]scale=1188:2112:force_original_aspect_ratio=increase,crop=1188:2112," +
                     "zoompan=z='min(zoom+0.0004,1.1)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=1:s=1080x1920:fps=30,setsar=1[bg]";
        if (watermarkPng != null && File.Exists(watermarkPng))
        {
            args.AddRange(new[] { "-i", watermarkPng });
            filter += ";[2:v]scale=420:-1[wm];[bg][wm]overlay=x=44:y=main_h-overlay_h-150,format=yuv420p[v]";
        }
        else filter += ";[bg]format=yuv420p[v]";
        args.AddRange(new[]
        {
            "-filter_complex", filter, "-map", "[v]", "-map", "1:a", "-shortest", "-t", MaxStorySeconds.ToString("0.###", CultureInfo.InvariantCulture),
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "21", "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", output
        });
        var (code, _, err) = await RunAsync(Ffmpeg, args, ct);
        if (code != 0 || !File.Exists(output)) throw new InvalidOperationException("Dựng video Tin thất bại: " + Tail(err));
    }

    /// <summary>Re-encodes any picture to JPEG (Facebook photo upload).</summary>
    public async Task ToJpegAsync(string image, string outputJpg, CancellationToken ct)
    {
        var (code, _, err) = await RunAsync(Ffmpeg, new[] { "-y", "-hide_banner", "-i", image, "-frames:v", "1", "-q:v", "2", outputJpg }, ct);
        if (code != 0 || !File.Exists(outputJpg)) throw new InvalidOperationException("Chuyển ảnh sang JPEG thất bại: " + Tail(err));
    }

    private static async Task<(int Code, string Stdout, string Stderr)> RunAsync(string exe, IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { /* already gone */ }
            throw;
        }
        return (p.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static string Tail(string s) => s.Length > 400 ? s[^400..] : s;
}

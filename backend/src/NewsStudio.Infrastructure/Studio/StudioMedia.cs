using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using NewsStudio.Infrastructure.Rendering;

namespace NewsStudio.Infrastructure.Studio;

/// <summary>FFmpeg work of the studio: shrinking reference images, measuring audio, joining clips and mixing music.</summary>
public class StudioMedia
{
    private readonly string _ffmpeg;
    private readonly string _ffprobe;

    public StudioMedia(IConfiguration configuration)
    {
        var configured = configuration["Ffmpeg:BinaryPath"];
        _ffmpeg = !string.IsNullOrWhiteSpace(configured) && File.Exists(configured)
            ? configured
            : EdgeTtsService.FindFfmpegExecutable() ?? "ffmpeg";
        var dir = Path.GetDirectoryName(_ffmpeg);
        var probeName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        _ffprobe = !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, probeName)) ? Path.Combine(dir, probeName) : "ffprobe";
    }

    /// <summary>Path of the FFmpeg executable found on this machine (null when missing).</summary>
    public static string? LocateFfmpeg() => EdgeTtsService.FindFfmpegExecutable();

    /// <summary>Frame size of a delivery resolution ("720p", "1080p", …) for the aspect ratio.</summary>
    public static (int W, int H) FrameSize(string aspectRatio, string resolution)
    {
        var shortSide = resolution.ToLowerInvariant() switch
        {
            "360p" => 360,
            "480p" => 480,
            "1080p" => 1080,
            "4k" => 2160,
            _ => 720
        };
        var longSide = (int)Math.Round(shortSide * 16 / 9.0 / 2) * 2; // even, as H.264 needs (480p → 854)
        return aspectRatio == "16:9" ? (longSide, shortSide) : (shortSide, longSide);
    }

    /// <summary>Re-encodes any uploaded picture to a JPEG of at most 1536 px on its long side.</summary>
    public async Task<byte[]> NormalizeImageAsync(byte[] data, string extension, CancellationToken ct)
    {
        var dir = TempDir();
        try
        {
            var input = Path.Combine(dir, "in" + (string.IsNullOrWhiteSpace(extension) ? ".img" : extension));
            var output = Path.Combine(dir, "out.jpg");
            await File.WriteAllBytesAsync(input, data, ct);
            var (code, _, err) = await RunAsync(_ffmpeg,
                $"-hide_banner -y -i \"{input}\" -vf \"scale='if(gt(iw,ih),min(1536,iw),-2)':'if(gt(iw,ih),-2,min(1536,ih))'\" -frames:v 1 -q:v 3 \"{output}\"", ct);
            if (code != 0 || !File.Exists(output)) throw new InvalidOperationException($"Ảnh không hợp lệ hoặc không đọc được: {Tail(err)}");
            return await File.ReadAllBytesAsync(output, ct);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    /// <summary>
    /// The start frame sent to the video model: the picture scaled to cover the clip's frame and centre-cropped, so the
    /// clip (whose shape follows the image) has exactly the video's aspect ratio and no black bars.
    /// </summary>
    public async Task<byte[]> PrepareStartFrameAsync(byte[] image, string aspectRatio, CancellationToken ct)
    {
        var (w, h) = FrameSize(aspectRatio, "720p");
        var dir = TempDir();
        try
        {
            var input = Path.Combine(dir, "in.img");
            var output = Path.Combine(dir, "frame.jpg");
            await File.WriteAllBytesAsync(input, image, ct);
            var (code, _, err) = await RunAsync(_ffmpeg,
                $"-hide_banner -y -i \"{input}\" -vf \"scale={w}:{h}:force_original_aspect_ratio=increase:flags=lanczos,crop={w}:{h},setsar=1\" -frames:v 1 -q:v 2 \"{output}\"", ct);
            if (code != 0 || !File.Exists(output)) throw new InvalidOperationException($"Không chuẩn bị được ảnh mở đầu: {Tail(err)}");
            return await File.ReadAllBytesAsync(output, ct);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    /// <summary>The last frame of a clip as a JPEG (start frame of the next shot when it continues the take).</summary>
    public async Task<byte[]> LastFrameAsync(byte[] video, CancellationToken ct)
    {
        var dir = TempDir();
        try
        {
            var input = Path.Combine(dir, "clip.mp4");
            var output = Path.Combine(dir, "last.jpg");
            await File.WriteAllBytesAsync(input, video, ct);
            // seek close to the end and keep overwriting the picture: the file left is the final frame
            var (code, _, err) = await RunAsync(_ffmpeg, $"-hide_banner -y -sseof -0.5 -i \"{input}\" -update 1 -q:v 2 \"{output}\"", ct);
            if (code != 0 || !File.Exists(output)) throw new InvalidOperationException($"Không lấy được khung cuối của clip trước: {Tail(err)}");
            return await File.ReadAllBytesAsync(output, ct);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    /// <summary>
    /// The Page watermark (round brand-kit logo + Page name on a translucent pill) as a transparent PNG at
    /// <paramref name="outputPng"/>, drawn by Scripts/generate_studio_watermark.py with Pillow.
    /// </summary>
    public async Task CreateWatermarkAsync(string pageName, byte[]? logo, string outputPng, CancellationToken ct)
    {
        var python = FFmpegRendererService.FindPythonExecutable()
                     ?? throw new InvalidOperationException("Không tìm thấy Python để vẽ watermark tên Page.");
        var script = FFmpegRendererService.FindScriptPath("generate_studio_watermark.py")
                     ?? throw new InvalidOperationException("Thiếu script generate_studio_watermark.py.");
        var args = $"\"{script}\" --output \"{outputPng}\" --name \"{Quote(pageName)}\"";
        if (logo is { Length: > 0 })
        {
            var logoPath = Path.Combine(Path.GetDirectoryName(outputPng)!, "brand_logo.img");
            await File.WriteAllBytesAsync(logoPath, logo, ct);
            args += $" --logo-image \"{logoPath}\"";
        }
        var (code, _, err) = await RunAsync(python, args, ct);
        if (code != 0 || !File.Exists(outputPng)) throw new InvalidOperationException($"Không vẽ được watermark tên Page: {Tail(err)}");
    }

    /// <summary>
    /// FFmpeg overlay that moves the watermark every 7 s between top-left, top-right and lower-left — all inside the
    /// Reels safe zone (clear of Facebook's header, action column and caption) — so it can't be cropped out in one go.
    /// </summary>
    public static string WatermarkFilter(string aspectRatio)
    {
        var (mx, top, low) = aspectRatio == "16:9" ? ("W*0.04", "H*0.06", "H*0.80") : ("W*0.11", "H*0.085", "H*0.70");
        const string slot = "mod(floor(t/7),3)";
        return $"[1:v]format=rgba,colorchannelmixer=aa=0.85[wm];" +
               $"[0:v][wm]overlay=x='if(eq({slot},1),W-w-{mx},{mx})':y='if(eq({slot},2),{low},{top})':eval=frame,format=yuv420p[v]";
    }

    private static string Quote(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string MetadataArgs(string? brand) => string.IsNullOrWhiteSpace(brand)
        ? ""
        : $" -metadata artist=\"{Quote(brand)}\" -metadata copyright=\"© {Quote(brand)}\" -metadata comment=\"Video gốc của Page {Quote(brand)} trên Facebook\"";

    public async Task<double> ProbeSecondsAsync(string path, CancellationToken ct)
    {
        var (_, output, _) = await RunAsync(_ffprobe, $"-v error -show_entries format=duration -of csv=p=0 \"{path}\"", ct);
        return double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    public async Task<double> ProbeSecondsAsync(byte[] data, string extension, CancellationToken ct)
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "probe" + extension);
            await File.WriteAllBytesAsync(path, data, ct);
            return await ProbeSecondsAsync(path, ct);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private async Task<bool> HasAudioAsync(string path, CancellationToken ct)
    {
        var (_, output, _) = await RunAsync(_ffprobe, $"-v error -select_streams a -show_entries stream=index -of csv=p=0 \"{path}\"", ct);
        return output.Trim().Length > 0;
    }

    /// <summary>
    /// Joins the clips in order (every clip re-encoded to the same size, 30 fps, 48 kHz stereo — a clip without sound
    /// gets silence, so joins never drift) and, when a music file is given, loops it under the whole video at
    /// <paramref name="musicVolume"/>, ducked while people speak, with a fade in/out. With <paramref name="watermarkPng"/>
    /// the Page watermark is burned in (moving, see <see cref="WatermarkFilter"/>); <paramref name="brand"/> also goes
    /// into the file's metadata. Returns the duration in seconds.
    /// </summary>
    public async Task<double> ComposeAsync(IReadOnlyList<string> clips, string? music, double musicVolume,
        string aspectRatio, string resolution, string output, CancellationToken ct, string? watermarkPng = null, string? brand = null)
    {
        var meta = MetadataArgs(brand);
        if (clips.Count == 0) throw new InvalidOperationException("Chưa có clip nào để ghép.");
        var (w, h) = FrameSize(aspectRatio, resolution);
        var dir = TempDir();
        try
        {
            var parts = new List<string>();
            for (var i = 0; i < clips.Count; i++)
            {
                var part = Path.Combine(dir, $"part_{i:D3}.mp4");
                var hasAudio = await HasAudioAsync(clips[i], ct);
                var audioIn = hasAudio ? "" : " -f lavfi -i anullsrc=r=48000:cl=stereo";
                var audioMap = hasAudio ? "0:a:0" : "1:a:0";
                var (code, _, err) = await RunAsync(_ffmpeg,
                    $"-hide_banner -y -i \"{clips[i]}\"{audioIn} -map 0:v:0 -map {audioMap} " +
                    $"-vf \"scale={w}:{h}:force_original_aspect_ratio=decrease:flags=lanczos,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p\" " +
                    "-af \"aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo\" -shortest " +
                    $"-c:v libx264 -preset veryfast -crf 18 -c:a aac -b:a 192k -ar 48000 -ac 2 \"{part}\"", ct);
                if (code != 0 || !File.Exists(part)) throw new InvalidOperationException($"FFmpeg không chuẩn hoá được clip {i + 1}: {Tail(err)}");
                parts.Add(part);
            }

            var list = Path.Combine(dir, "list.txt");
            var sb = new StringBuilder();
            foreach (var p in parts) sb.Append("file '").Append(p.Replace("\\", "/").Replace("'", "'\\''")).Append("'\n");
            await File.WriteAllTextAsync(list, sb.ToString(), new UTF8Encoding(false), ct);

            var joined = music == null && watermarkPng == null ? output : Path.Combine(dir, "joined.mp4");
            var (jc, _, jerr) = await RunAsync(_ffmpeg, $"-hide_banner -y -f concat -safe 0 -i \"{list}\" -c copy{meta} -movflags +faststart \"{joined}\"", ct);
            if (jc != 0 || !File.Exists(joined)) throw new InvalidOperationException($"FFmpeg không ghép được các clip: {Tail(jerr)}");

            if (watermarkPng != null)
            {
                var marked = music == null ? output : Path.Combine(dir, "marked.mp4");
                var (wc, _, werr) = await RunAsync(_ffmpeg,
                    $"-hide_banner -y -i \"{joined}\" -i \"{watermarkPng}\" -filter_complex \"{WatermarkFilter(aspectRatio)}\" " +
                    $"-map \"[v]\" -map 0:a -c:v libx264 -preset veryfast -crf 18 -c:a copy{meta} -movflags +faststart \"{marked}\"", ct);
                if (wc != 0 || !File.Exists(marked)) throw new InvalidOperationException($"FFmpeg không gắn được watermark tên Page: {Tail(werr)}");
                joined = marked;
            }

            var duration = await ProbeSecondsAsync(joined, ct);
            if (music != null)
            {
                var d = duration.ToString("F3", CultureInfo.InvariantCulture);
                var fadeOut = Math.Max(0, duration - 2).ToString("F3", CultureInfo.InvariantCulture);
                var vol = Math.Clamp(musicVolume, 0, 1).ToString("F3", CultureInfo.InvariantCulture);
                var filter =
                    "[0:a]asplit=2[voice][key];" +
                    $"[1:a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,volume={vol},atrim=0:{d},afade=t=in:d=1.5,afade=t=out:st={fadeOut}:d=2[m];" +
                    "[m][key]sidechaincompress=threshold=0.03:ratio=6:attack=20:release=500[ducked];" +
                    "[voice][ducked]amix=inputs=2:duration=first:dropout_transition=0:normalize=0[a]";
                var (mc, _, merr) = await RunAsync(_ffmpeg,
                    $"-hide_banner -y -i \"{joined}\" -stream_loop -1 -i \"{music}\" -filter_complex \"{filter}\" " +
                    $"-map 0:v -map \"[a]\" -c:v copy -c:a aac -b:a 192k -ar 48000 -t {d}{meta} -movflags +faststart \"{output}\"", ct);
                if (mc != 0 || !File.Exists(output)) throw new InvalidOperationException($"FFmpeg không trộn được nhạc nền: {Tail(merr)}");
                duration = await ProbeSecondsAsync(output, ct);
            }
            return duration;
        }
        finally
        {
            TryDelete(dir);
        }
    }

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "studio_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* best effort */ }
    }

    private static string Tail(string s)
    {
        var t = (s ?? string.Empty).Trim();
        return t.Length > 500 ? "…" + t[^500..] : t;
    }

    private static async Task<(int Code, string Stdout, string Stderr)> RunAsync(string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Không chạy được {exe}.");
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return (proc.ExitCode, await stdout, await stderr);
    }
}

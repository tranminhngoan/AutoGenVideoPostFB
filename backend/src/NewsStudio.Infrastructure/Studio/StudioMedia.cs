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
    /// <param name="offsetSeconds">Where this clip starts in the final video, so the cycle runs on across clips.</param>
    public static string WatermarkFilter(string aspectRatio, int watermarkInput, string baseLabel, double offsetSeconds)
    {
        var (mx, top, low) = aspectRatio == "16:9" ? ("W*0.04", "H*0.06", "H*0.80") : ("W*0.11", "H*0.085", "H*0.70");
        var slot = $"mod(floor((t+{offsetSeconds.ToString("F3", CultureInfo.InvariantCulture)})/7),3)";
        return $"[{watermarkInput}:v]format=rgba,colorchannelmixer=aa=0.85[wm];" +
               $"[{baseLabel}][wm]overlay=x='if(eq({slot},1),W-w-{mx},{mx})':y='if(eq({slot},2),{low},{top})':eval=frame,format=yuv420p[v]";
    }

    /// <summary>Frame rate of a clip's video stream (kept as is when joining, so motion isn't re-timed); 30 when unknown.</summary>
    public async Task<string> ProbeFrameRateAsync(string path, CancellationToken ct)
    {
        var (_, output, _) = await RunAsync(_ffprobe, $"-v error -select_streams v:0 -show_entries stream=avg_frame_rate -of csv=p=0 \"{path}\"", ct);
        var text = output.Trim();
        var parts = text.Split('/');
        if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 && n / d is >= 15 and <= 60)
            return text;
        return "30";
    }

    private static string Quote(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string MetadataArgs(string? brand) => string.IsNullOrWhiteSpace(brand)
        ? ""
        : $" -metadata artist=\"{Quote(brand)}\" -metadata copyright=\"© {Quote(brand)}\" -metadata comment=\"Video gốc của Page {Quote(brand)} trên Facebook\"";

    public const int MaxMusicSeconds = 300;

    /// <summary>A TTS line (WAV) as compact mono AAC (≈12 KB a second) — small enough to keep in the database.</summary>
    public async Task<byte[]> EncodeVoiceAsync(string wavPath, CancellationToken ct)
    {
        var dir = TempDir();
        try
        {
            var output = Path.Combine(dir, "voice.m4a");
            var (code, _, err) = await RunAsync(_ffmpeg, $"-hide_banner -y -i \"{wavPath}\" -vn -c:a aac -b:a 96k -ac 1 -ar 48000 -movflags +faststart \"{output}\"", ct);
            if (code != 0 || !File.Exists(output)) throw new InvalidOperationException($"Không mã hoá được giọng đọc: {Tail(err)}");
            return await File.ReadAllBytesAsync(output, ct);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    /// <summary>Re-encodes an uploaded track to AAC 128 kb/s stereo (≈1 MB a minute), at most 5 minutes.</summary>
    public async Task<byte[]> CompactMusicAsync(byte[] data, string extension, CancellationToken ct)
    {
        var dir = TempDir();
        try
        {
            var input = Path.Combine(dir, "in" + (string.IsNullOrWhiteSpace(extension) ? ".audio" : extension));
            var output = Path.Combine(dir, "out.m4a");
            await File.WriteAllBytesAsync(input, data, ct);
            var (code, _, err) = await RunAsync(_ffmpeg,
                $"-hide_banner -y -i \"{input}\" -vn -t {MaxMusicSeconds} -c:a aac -b:a 128k -ar 44100 -ac 2 -movflags +faststart \"{output}\"", ct);
            if (code != 0 || !File.Exists(output)) throw new ArgumentException($"Không đọc được file nhạc: {Tail(err)}");
            return await File.ReadAllBytesAsync(output, ct);
        }
        finally
        {
            TryDelete(dir);
        }
    }

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
    /// Joins the clips in order (every clip re-encoded once to the same size, the first clip's frame rate and 48 kHz
    /// stereo, the watermark burned in during that same encode) and, when a music file is given, loops it under the
    /// whole video at <paramref name="musicVolume"/> with a fade in/out, ducked while people SPEAK (sound effects don't
    /// push the music down). <paramref name="brand"/> also goes into the file's metadata. Returns the duration in seconds.
    /// A clip without effects keeps its own audio (silence when it has none), cut / padded to the clip.
    /// </summary>
    /// <param name="voices">
    /// Per clip, a voice (TTS) starting a quarter second into that clip. It may be longer than the clip — a scene's
    /// narration read in one take — and runs on over the next clips until the next voice starts.
    /// </param>
    /// <param name="effects">Per clip, its sound effects track, mixed at <paramref name="effectsVolume"/>.</param>
    public async Task<double> ComposeAsync(IReadOnlyList<string> clips, string? music, double musicVolume,
        string aspectRatio, string resolution, string output, CancellationToken ct, string? watermarkPng = null, string? brand = null,
        IReadOnlyList<string?>? voices = null, IReadOnlyList<string?>? effects = null, double effectsVolume = 0.7)
    {
        var meta = MetadataArgs(brand);
        if (clips.Count == 0) throw new InvalidOperationException("Chưa có clip nào để ghép.");
        var (w, h) = FrameSize(aspectRatio, resolution);
        const string fmt = "aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo";
        var fxVol = Math.Clamp(effectsVolume, 0, 2).ToString("F3", CultureInfo.InvariantCulture);
        var dir = TempDir();
        try
        {
            // 1. every clip normalised (size, rate, watermark) with its own effects / sound; voices come later, on one
            //    timeline, so a narration take can run across cuts instead of stopping at the end of each clip
            var parts = new List<string>();
            var starts = new List<double>();
            var fps = await ProbeFrameRateAsync(clips[0], ct);
            var offset = 0.0;
            for (var i = 0; i < clips.Count; i++)
            {
                var part = Path.Combine(dir, $"part_{i:D3}.mp4");
                var fx = effects != null && i < effects.Count ? effects[i] : null;
                var own = fx == null && await HasAudioAsync(clips[i], ct);
                var seconds = (await ProbeSecondsAsync(clips[i], ct)).ToString("F3", CultureInfo.InvariantCulture);

                var inputs = $"-i \"{clips[i]}\"";
                var next = 1;
                var fi = -1;
                if (fx != null) { inputs += $" -i \"{fx}\""; fi = next++; }
                var graph = $"[0:v]scale={w}:{h}:force_original_aspect_ratio=decrease:flags=lanczos,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={fps}[base];";
                if (watermarkPng != null)
                {
                    inputs += $" -i \"{watermarkPng}\"";
                    graph += WatermarkFilter(aspectRatio, next, "base", offset) + ";";
                }
                else
                {
                    graph += "[base]format=yuv420p[v];";
                }
                graph += fx != null ? $"[{fi}:a]{fmt},volume={fxVol},apad[a]"
                    : own ? $"[0:a]{fmt}[a]"
                    : "anullsrc=r=48000:cl=stereo[a]";

                var args = $"-hide_banner -y {inputs} -filter_complex \"{graph}\" -map \"[v]\" -map \"[a]\" -t {seconds} " +
                           $"-c:v libx264 -preset fast -crf 19 -maxrate 10M -bufsize 20M -c:a aac -b:a 192k -ar 48000 -ac 2 \"{part}\"";
                var (code, _, err) = await RunAsync(_ffmpeg, args, ct);
                if (code != 0 || !File.Exists(part)) throw new InvalidOperationException($"FFmpeg không chuẩn hoá được clip {i + 1}: {Tail(err)}");
                parts.Add(part);
                starts.Add(offset);
                offset += await ProbeSecondsAsync(part, ct);
            }

            var spoken = new List<(string Path, int Clip)>();
            for (var i = 0; voices != null && i < voices.Count && i < clips.Count; i++)
                if (voices[i] is { } vp) spoken.Add((vp, i));

            var joined = music == null && spoken.Count == 0 ? output : Path.Combine(dir, "joined.mp4");
            await ConcatAsync(parts, joined, meta + " -movflags +faststart", dir, "list.txt", ct);
            var duration = await ProbeSecondsAsync(joined, ct);
            if (music == null && spoken.Count == 0) return duration;

            // 2. voices on the timeline: each starts 0.25 s into its clip and may run on over the next clips (a scene's
            //    narration take) until the next voice starts or the video ends; music ducks under speech only
            var d = duration.ToString("F3", CultureInfo.InvariantCulture);
            var inputsAll = $"-i \"{joined}\"";
            var filter = new StringBuilder();
            for (var j = 0; j < spoken.Count; j++)
            {
                var start = starts[spoken[j].Clip] + 0.25;
                var end = j + 1 < spoken.Count ? starts[spoken[j + 1].Clip] + 0.25 : duration;
                var length = Math.Max(0.1, end - start).ToString("F3", CultureInfo.InvariantCulture);
                var ms = ((int)Math.Round(start * 1000)).ToString(CultureInfo.InvariantCulture);
                inputsAll += $" -i \"{spoken[j].Path}\"";
                filter.Append($"[{j + 1}:a]{fmt},atrim=0:{length},asetpts=PTS-STARTPTS,adelay={ms}|{ms}[v{j}];");
            }
            if (spoken.Count > 0)
            {
                for (var j = 0; j < spoken.Count; j++) filter.Append($"[v{j}]");
                filter.Append(spoken.Count == 1 ? "anull" : $"amix=inputs={spoken.Count}:duration=longest:dropout_transition=0:normalize=0");
                filter.Append($",apad,atrim=0:{d}[speech];");
            }
            if (music != null)
            {
                var mi = spoken.Count + 1;
                inputsAll += $" -stream_loop -1 -i \"{music}\"";
                var fadeOut = Math.Max(0, duration - 2).ToString("F3", CultureInfo.InvariantCulture);
                var vol = Math.Clamp(musicVolume, 0, 1).ToString("F3", CultureInfo.InvariantCulture);
                // every track brought to the voices' loudness first, so the volume means the same for any song
                filter.Append($"[{mi}:a]loudnorm=I=-16:TP=-2:LRA=11,{fmt},volume={vol},atrim=0:{d},afade=t=in:d=1.5,afade=t=out:st={fadeOut}:d=2[m];");
                if (spoken.Count > 0)
                {
                    filter.Append("[speech]asplit=2[sp][key];");
                    // a gentle dip (about 6 dB) under speech: the music stays present instead of vanishing
                    filter.Append("[m][key]sidechaincompress=threshold=0.1:ratio=2.5:attack=30:release=600[ducked];");
                    filter.Append("[0:a][sp][ducked]amix=inputs=3:duration=first:dropout_transition=0:normalize=0[a]");
                }
                else
                {
                    filter.Append("[0:a][m]amix=inputs=2:duration=first:dropout_transition=0:normalize=0[a]");
                }
            }
            else
            {
                filter.Append("[0:a][speech]amix=inputs=2:duration=first:dropout_transition=0:normalize=0[a]");
            }
            var (mc, _, merr) = await RunAsync(_ffmpeg,
                $"-hide_banner -y {inputsAll} -filter_complex \"{filter}\" " +
                $"-map 0:v -map \"[a]\" -c:v copy -c:a aac -b:a 192k -ar 48000 -t {d}{meta} -movflags +faststart \"{output}\"", ct);
            if (mc != 0 || !File.Exists(output)) throw new InvalidOperationException($"FFmpeg không trộn được giọng đọc / nhạc nền: {Tail(merr)}");
            return await ProbeSecondsAsync(output, ct);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private async Task ConcatAsync(IReadOnlyList<string> files, string output, string extraArgs, string dir, string listName, CancellationToken ct)
    {
        var list = Path.Combine(dir, listName);
        var sb = new StringBuilder();
        foreach (var p in files) sb.Append("file '").Append(p.Replace("\\", "/").Replace("'", "'\\''")).Append("'\n");
        await File.WriteAllTextAsync(list, sb.ToString(), new UTF8Encoding(false), ct);
        var (code, _, err) = await RunAsync(_ffmpeg, $"-hide_banner -y -f concat -safe 0 -i \"{list}\" -c copy{extraArgs} \"{output}\"", ct);
        if (code != 0 || !File.Exists(output)) throw new InvalidOperationException($"FFmpeg không ghép được các đoạn: {Tail(err)}");
    }

    /// <summary>The audio of a media file (an MP3 effects track, or a video's soundtrack) as compact AAC.</summary>
    public async Task<byte[]> ExtractAudioAsync(byte[] media, CancellationToken ct, string extension = ".mp4")
    {
        var dir = TempDir();
        var video = media;
        try
        {
            var input = Path.Combine(dir, "in" + extension);
            var output = Path.Combine(dir, "sound.m4a");
            await File.WriteAllBytesAsync(input, video, ct);
            if (!await HasAudioAsync(input, ct)) throw new InvalidOperationException("File trả về không có âm thanh.");
            var (code, _, err) = await RunAsync(_ffmpeg, $"-hide_banner -y -i \"{input}\" -vn -map 0:a:0 -c:a aac -b:a 128k -ar 48000 -ac 2 -movflags +faststart \"{output}\"", ct);
            if (code != 0 || !File.Exists(output)) throw new InvalidOperationException($"Không tách được âm thanh: {Tail(err)}");
            return await File.ReadAllBytesAsync(output, ct);
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

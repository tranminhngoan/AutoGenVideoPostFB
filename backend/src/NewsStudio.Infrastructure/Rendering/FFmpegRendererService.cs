using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Infrastructure.Rendering;

public class FFmpegRendererService : IVideoRendererService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<FFmpegRendererService> _logger;

    public FFmpegRendererService(
        IConfiguration configuration,
        ILogger<FFmpegRendererService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<VideoRenderResult> RenderVerticalReelAsync(
        VideoRenderRequest request,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var tempBase = NewsStudio.Infrastructure.Common.TempStorage.GetRoot(_configuration);
        var outputDir = Path.Combine(tempBase, request.ProjectId.ToString(), "output");
        Directory.CreateDirectory(outputDir);

        var outputVideoPath = Path.Combine(outputDir, $"reel_9x16_{DateTime.UtcNow.Ticks}.mp4");
        var filterScriptPath = Path.Combine(outputDir, "filter_complex.txt");
        var overlayPath = Path.Combine(outputDir, "news_overlay.png");

        _logger.LogInformation("Starting FFmpeg vertical 9:16 render for project {ProjectId} (Target: {Res}, 30 FPS)",
            request.ProjectId, request.Resolution);

        progress?.Report(10);

        // Scene images normally arrive already downloaded and verified (SceneImageService).
        // Remote URLs left over are fetched here with a per-URL file name, so a re-render never
        // picks up a stale picture from an earlier attempt.
        string? localPrimaryImage = null;
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(20);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 AI-News-Studio/1.0");

        foreach (var scene in request.Scenes)
        {
            if (string.IsNullOrWhiteSpace(scene.ImageUrl)) continue;

            if (File.Exists(scene.ImageUrl))
            {
                localPrimaryImage ??= scene.ImageUrl;
                continue;
            }

            var remote = System.Net.WebUtility.HtmlDecode(scene.ImageUrl);
            if (!remote.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !remote.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;

            var urlKey = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(remote)))[..16].ToLowerInvariant();
            var targetFile = Directory.GetFiles(outputDir, $"img_{urlKey}.*").FirstOrDefault();
            if (targetFile == null)
            {
                try
                {
                    var bytes = await httpClient.GetByteArrayAsync(remote, cancellationToken);
                    var ext = SceneImageService.DetectImageType(bytes);
                    if (ext == null)
                    {
                        _logger.LogWarning("Scene image {Url} is not a JPEG/PNG/WebP picture; skipped", remote);
                        continue;
                    }
                    targetFile = Path.Combine(outputDir, $"img_{urlKey}.{ext}");
                    await File.WriteAllBytesAsync(targetFile, bytes, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Could not pre-download scene image {Url}", remote);
                    continue;
                }
            }
            scene.ImageUrl = targetFile;
            localPrimaryImage ??= targetFile;
        }

        // Rule: never render a video without pictures (it would be a black screen)
        if (localPrimaryImage == null && !NewsStudio.Infrastructure.Common.SimulationMode.IsEnabled(_configuration))
        {
            throw new InvalidOperationException(
                "Dựng video thất bại: không có ảnh nào dùng được cho các cảnh. Video không được dựng khi thiếu ảnh.");
        }

        // Scenes whose picture is unusable reuse a working one instead of a blank background
        foreach (var scene in request.Scenes.Where(s => string.IsNullOrWhiteSpace(s.ImageUrl) || !File.Exists(s.ImageUrl)))
        {
            if (localPrimaryImage != null) scene.ImageUrl = localPrimaryImage;
        }

        progress?.Report(20);

        // 1. Generate Branded News Card Overlay (Top window + lower card + date + headline)
        var pythonExe = FindPythonExecutable();
        var overlayScript = FindOverlayScriptPath();

        if (!string.IsNullOrWhiteSpace(pythonExe) && !string.IsNullOrWhiteSpace(overlayScript))
        {
            try
            {
                var cleanTitle = System.Text.RegularExpressions.Regex
                    .Replace(request.Title ?? "BẢN TIN TIÊU ĐIỂM", @"^\s*TIÊU ĐIỂM:\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    .Replace("\"", "\\\"");
                // Lead sentence under the headline so the text fills the green card (overlay auto-sizes it)
                var cleanSummary = (request.Summary ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("\"", "\\\"");
                if (cleanSummary.Length > 400) cleanSummary = cleanSummary[..400];
                var cleanPub = (!string.IsNullOrWhiteSpace(request.Publisher) ? request.Publisher : "TIN TỨC 24H").Replace("\"", "\\\"");
                var dateStr = request.PublishedDate.ToString("dd/MM/yyyy");
                // Brand pill = name of the Facebook Page the video is made for; colours follow its topic
                var brand = (string.IsNullOrWhiteSpace(request.BrandName) ? "Trạm Tin 24/7" : request.BrandName.Trim()).Replace("\"", "\\\"");
                var theme = System.Text.RegularExpressions.Regex.IsMatch(request.Theme ?? "", "^[a-z-]{1,30}$") ? request.Theme : "tong-hop";
                var topicLabel = (request.TopicLabel ?? string.Empty).Replace("\"", "\\\"");

                var overlayArgs = $"\"{overlayScript}\" --output \"{overlayPath}\" --title \"{cleanTitle}\" --summary \"{cleanSummary}\" --publisher \"{cleanPub}\" --date \"{dateStr}\" --logo \"{brand}\" --theme \"{theme}\" --topic-label \"{topicLabel}\"";
                _logger.LogInformation("Generating news overlay at {Path} via Python script", overlayPath);
                await RunProcessAsync(pythonExe, overlayArgs, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to run Python overlay script. Will attempt render without overlay.");
            }
        }

        // 2. Build FFmpeg command and filter graph
        var ffmpegArgs = BuildFfmpegArguments(request, outputVideoPath, overlayPath, localPrimaryImage, out var totalDuration);
        await File.WriteAllTextAsync(filterScriptPath, ffmpegArgs, Encoding.UTF8, cancellationToken);

        progress?.Report(35);

        // 3. Resolve FFmpeg executable
        var ffmpegPath = FindFfmpegExecutable();

        var simulate = NewsStudio.Infrastructure.Common.SimulationMode.IsEnabled(_configuration);

        if (!simulate && (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath)))
        {
            throw new InvalidOperationException("Dựng video thất bại: không tìm thấy FFmpeg trên máy chủ. Cài FFmpeg và thêm vào PATH.");
        }

        if (!string.IsNullOrWhiteSpace(ffmpegPath) && File.Exists(ffmpegPath))
        {
            _logger.LogInformation("Executing FFmpeg at {Path} with arguments: {Args}", ffmpegPath, ffmpegArgs);
            try
            {
                await ExecuteFfmpegProcessAsync(ffmpegPath, ffmpegArgs, progress, cancellationToken);
            }
            catch (Exception ex) when (!simulate && ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "FFmpeg render failed for project {ProjectId}", request.ProjectId);
                // FFmpeg stderr can be very long; keep the tail, which holds the actual error
                var detail = ex.Message.Length > 600 ? "…" + ex.Message[^600..] : ex.Message;
                throw new InvalidOperationException($"Dựng video thất bại: {detail}", ex);
            }
            catch (Exception ex) when (simulate && ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "FFmpeg failed. Simulation mode: generating placeholder MP4.");
                await GenerateFallbackMp4Async(outputVideoPath, totalDuration, cancellationToken);
            }
        }
        else
        {
            _logger.LogInformation("FFmpeg not found. Simulation mode: generating placeholder MP4.");
            await GenerateFallbackMp4Async(outputVideoPath, totalDuration, cancellationToken);
        }

        progress?.Report(95);

        var fileInfo = new FileInfo(outputVideoPath);
        var hash = await ComputeFileSha256Async(outputVideoPath, cancellationToken);

        progress?.Report(100);

        _logger.LogInformation("Rendered 9:16 vertical video reel successfully at {Path} ({Size} bytes, {Duration}s)",
            outputVideoPath, fileInfo.Length, totalDuration);

        return new VideoRenderResult
        {
            OutputVideoPath = outputVideoPath,
            DurationSeconds = totalDuration,
            FileSizeBytes = fileInfo.Length,
            Resolution = request.Resolution,
            Fps = request.Fps,
            MimeType = "video/mp4",
            Sha256Hash = hash
        };
    }

    public async Task<string> RenderSlideshowAsync(IReadOnlyList<string> imagePaths, IReadOnlyList<double> seconds, string outputPath, CancellationToken cancellationToken = default)
    {
        if (imagePaths.Count == 0 || imagePaths.Count != seconds.Count)
            throw new ArgumentException("Cần mỗi ảnh một thời lượng.");
        var ffmpegPath = FindFfmpegExecutable();
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
            throw new InvalidOperationException("Không tìm thấy FFmpeg trên máy chủ để dựng video Story.");

        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        var total = seconds.Sum();
        var args = new StringBuilder("-y ");
        for (var i = 0; i < imagePaths.Count; i++)
            args.Append($"-loop 1 -framerate 30 -t {F(seconds[i])} -i \"{imagePaths[i]}\" ");
        // Silent stereo track: Stories expect AAC audio
        args.Append($"-f lavfi -t {F(total)} -i anullsrc=r=48000:cl=stereo ");

        var filter = new StringBuilder();
        for (var i = 0; i < imagePaths.Count; i++)
        {
            var fade = Math.Min(0.35, seconds[i] / 4);
            filter.Append($"[{i}:v]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,setsar=1,fps=30,format=yuv420p,")
                  .Append($"fade=t=in:st=0:d={F(fade)},fade=t=out:st={F(seconds[i] - fade)}:d={F(fade)}[v{i}];");
        }
        for (var i = 0; i < imagePaths.Count; i++) filter.Append($"[v{i}]");
        filter.Append($"concat=n={imagePaths.Count}:v=1:a=0[v]");

        args.Append($"-filter_complex \"{filter}\" -map \"[v]\" -map {imagePaths.Count}:a ")
            .Append("-c:v libx264 -preset veryfast -crf 22 -pix_fmt yuv420p -r 30 -g 60 -keyint_min 60 -sc_threshold 0 ")
            .Append("-c:a aac -b:a 128k -ar 48000 -ac 2 -shortest -movflags +faststart ")
            .Append($"\"{outputPath}\"");

        _logger.LogInformation("Rendering {Count}-picture Story slideshow ({Seconds}s) to {Path}", imagePaths.Count, total, outputPath);
        await ExecuteFfmpegProcessAsync(ffmpegPath, args.ToString(), null, cancellationToken);
        return outputPath;
    }

    private string FindFfmpegExecutable()
    {
        var cfgPath = _configuration["Ffmpeg:BinaryPath"];
        if (!string.IsNullOrWhiteSpace(cfgPath) && File.Exists(cfgPath)) return cfgPath;

        var localBin = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(localBin)) return localBin;

        var localLinuxBin = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        if (File.Exists(localLinuxBin)) return localLinuxBin;

        var toolBin = @"D:\tools\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe";
        if (File.Exists(toolBin)) return toolBin;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(Path.PathSeparator);
        var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        foreach (var p in paths)
        {
            try
            {
                var full = Path.Combine(p.Trim(), exeName);
                if (File.Exists(full)) return full;
            }
            catch { }
        }

        return string.Empty;
    }

    internal static string? FindPythonExecutable()
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

    private static string? FindOverlayScriptPath() => FindScriptPath("generate_news_overlay.py");

    /// <summary>Python helper shipped in the Scripts folder (next to the app, or in the source tree when developing).</summary>
    internal static string? FindScriptPath(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Scripts", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "Scripts", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "backend", "src", "NewsStudio.Infrastructure", "Scripts", fileName),
            Path.Combine(@"d:\Project\AutoGenVideoPostFB\backend\src\NewsStudio.Infrastructure\Scripts", fileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<bool> RunProcessAsync(string exe, string args, CancellationToken cancellationToken)
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

    private static string BuildFfmpegArguments(
        VideoRenderRequest request,
        string outputPath,
        string overlayPath,
        string? localImagePath,
        out double totalDuration)
    {
        // The video lasts exactly as long as the narration. No fixed 55–65s window: a short article
        // gives a short video instead of a silent tail, a long one isn't cut off mid-sentence.
        totalDuration = request.AudioDurationSeconds > 0
            ? request.AudioDurationSeconds
            : request.Scenes.Count > 0
                ? request.Scenes.Max(s => s.EndTimeSeconds)
                : 60.0;

        bool hasOverlay = !string.IsNullOrWhiteSpace(overlayPath) && File.Exists(overlayPath);
        bool hasAudio = !string.IsNullOrWhiteSpace(request.AudioFilePath) && File.Exists(request.AudioFilePath);

        var scenesWithImages = request.Scenes
            .Where(s => !string.IsNullOrWhiteSpace(s.ImageUrl) && File.Exists(s.ImageUrl))
            .ToList();

        var uniqueImages = scenesWithImages
            .Select(s => s.ImageUrl)
            .Distinct()
            .ToList();

        var args = new StringBuilder();

        if (uniqueImages.Count > 1 && request.Scenes.Count > 1)
        {
            // Multi-scene visual concatenation
            int sceneCount = request.Scenes.Count;
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = request.Scenes[i];
                var img = File.Exists(scene.ImageUrl) ? scene.ImageUrl : localImagePath;
                var dur = Math.Max(scene.EndTimeSeconds - scene.StartTimeSeconds, 1.0);

                if (!string.IsNullOrWhiteSpace(img) && File.Exists(img))
                {
                    args.Append($"-loop 1 -t {dur.ToString("F2", CultureInfo.InvariantCulture)} -i \"{img}\" ");
                }
                else
                {
                    args.Append($"-f lavfi -t {dur.ToString("F2", CultureInfo.InvariantCulture)} -i color=c=0x0B0F19:s=1080x1920:r=30 ");
                }
            }

            // Input sceneCount: Overlay PNG
            if (hasOverlay)
            {
                args.Append($"-i \"{overlayPath}\" ");
            }
            else
            {
                args.Append("-f lavfi -i color=c=0x00000000:s=1080x1920:r=30 ");
            }

            // Input sceneCount + 1: Audio WAV
            if (hasAudio)
            {
                args.Append($"-i \"{request.AudioFilePath}\" ");
            }
            else
            {
                args.Append("-f lavfi -i anullsrc=r=44100:cl=stereo ");
            }

            var filter = new StringBuilder();
            for (int i = 0; i < sceneCount; i++)
            {
                filter.Append($"[{i}:v]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,setsar=1[v{i}];");
            }
            for (int i = 0; i < sceneCount; i++)
            {
                filter.Append($"[v{i}]");
            }
            filter.Append($"concat=n={sceneCount}:v=1:a=0[media];");
            filter.Append("[media]split[bg][fg];");
            filter.Append("[bg]boxblur=20:5[bgout];");
            filter.Append("[fg]scale=1080:1050:force_original_aspect_ratio=decrease[fgout];");
            filter.Append("[bgout][fgout]overlay=(W-w)/2:(1050-h)/2[base];");
            filter.Append($"[base][{sceneCount}:v]overlay=0:0[v]");

            args.Append($"-filter_complex \"{filter}\" ");
            args.Append("-map \"[v]\" ");
            args.Append($"-map {sceneCount + 1}:a ");
        }
        else
        {
            // Single image or uniform visuals
            bool hasImage = !string.IsNullOrWhiteSpace(localImagePath) && File.Exists(localImagePath);
            if (hasImage)
            {
                args.Append($"-loop 1 -i \"{localImagePath}\" ");
            }
            else
            {
                args.Append("-f lavfi -i color=c=0x0B0F19:s=1080x1920:r=30 ");
            }

            if (hasOverlay)
            {
                args.Append($"-i \"{overlayPath}\" ");
            }
            else
            {
                args.Append("-f lavfi -i color=c=0x00000000:s=1080x1920:r=30 ");
            }

            if (hasAudio)
            {
                args.Append($"-i \"{request.AudioFilePath}\" ");
            }
            else
            {
                args.Append("-f lavfi -i anullsrc=r=44100:cl=stereo ");
            }

            if (hasImage)
            {
                args.Append("-filter_complex \"[0:v]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=20:5[bg];[0:v]scale=1080:1050:force_original_aspect_ratio=decrease[fg];[bg][fg]overlay=(W-w)/2:(1050-h)/2[base];[base][1:v]overlay=0:0[v]\" -map \"[v]\" ");
            }
            else
            {
                args.Append("-filter_complex \"[0:v][1:v]overlay=0:0[v]\" -map \"[v]\" ");
            }

            args.Append("-map 2:a ");
        }

        args.Append($"-t {totalDuration.ToString("F1", CultureInfo.InvariantCulture)} ");
        args.Append("-c:v libx264 -preset veryfast -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart ");
        args.Append($"-y \"{outputPath}\"");

        return args.ToString().Trim();
    }

    private async Task ExecuteFfmpegProcessAsync(
        string ffmpegPath,
        string arguments,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var err = await errorTask;
            _logger.LogWarning("FFmpeg exited with code {Code}: {Err}", process.ExitCode, err);
            throw new InvalidOperationException($"FFmpeg failed with exit code {process.ExitCode}: {err}");
        }
    }

    private static async Task GenerateFallbackMp4Async(string outputPath, double durationSeconds, CancellationToken cancellationToken)
    {
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);

        bw.Write(new byte[] { 0x00, 0x00, 0x00, 0x20 });
        bw.Write(Encoding.ASCII.GetBytes("ftyp"));
        bw.Write(Encoding.ASCII.GetBytes("isom"));
        bw.Write(new byte[] { 0x00, 0x00, 0x02, 0x00 });
        bw.Write(Encoding.ASCII.GetBytes("isomiso2avc1mp41"));

        var mdatSize = (int)(durationSeconds * 128000);
        bw.Write(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(mdatSize + 8)));
        bw.Write(Encoding.ASCII.GetBytes("mdat"));

        byte[] frameBuffer = new byte[8192];
        int remaining = mdatSize;
        while (remaining > 0 && !cancellationToken.IsCancellationRequested)
        {
            int toWrite = Math.Min(frameBuffer.Length, remaining);
            await fs.WriteAsync(frameBuffer.AsMemory(0, toWrite), cancellationToken);
            remaining -= toWrite;
        }

        bw.Write(new byte[] { 0x00, 0x00, 0x00, 0x6C });
        bw.Write(Encoding.ASCII.GetBytes("moov"));
        bw.Write(new byte[] { 0x00, 0x00, 0x00, 0x64 });
        bw.Write(Encoding.ASCII.GetBytes("mvhd"));
        bw.Write(new byte[92]);

        await fs.FlushAsync(cancellationToken);
    }

    private static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var bytes = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

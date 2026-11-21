using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Ai;
using NewsStudio.Infrastructure.Common;
using NewsStudio.Infrastructure.Rendering;

namespace NewsStudio.Infrastructure.FilmFactory;

/// <summary>
/// Turns a written episode into an MP4:
///  1. one AI still frame per distinct frame prompt (characters keep their face via reference portraits),
///  2. one Gemini voice line per shot (each character has its own voice),
///  3. each shot = frame + camera move (Ken Burns) + its line, all encoded with identical parameters,
///  4. shots joined, then checked (has picture, has audible sound, right length).
/// Any failure stops the render with the real reason — no placeholder pictures, no silent tracks.
/// </summary>
public class FilmRendererService : IFilmRendererService
{
    private const int Fps = 30;
    private const double GapAfterLine = 0.25;

    private readonly IApplicationDbContext _context;
    private readonly GeminiTtsService _tts;
    private readonly FilmGeminiClient _gemini;
    private readonly IFilmCostController _costController;
    private readonly AiUsageMeter _meter;
    private readonly IFilmGpuBackend _gpu;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FilmRendererService> _logger;
    private readonly string _ffmpeg;
    private readonly string _ffprobe;

    public FilmRendererService(
        IApplicationDbContext context,
        GeminiTtsService tts,
        FilmGeminiClient gemini,
        IFilmCostController costController,
        AiUsageMeter meter,
        IFilmGpuBackend gpu,
        IConfiguration configuration,
        ILogger<FilmRendererService> logger)
    {
        _context = context;
        _tts = tts;
        _gemini = gemini;
        _costController = costController;
        _meter = meter;
        _gpu = gpu;
        _configuration = configuration;
        _logger = logger;
        (_ffmpeg, _ffprobe) = FindFfmpeg(configuration);
    }

    private string Root => Path.Combine(TempStorage.GetRoot(_configuration), "film_factory");

    // ─────────────────────────────── Public API ───────────────────────────────

    public async Task<string> RenderShotAsync(Guid shotId, CancellationToken ct = default)
    {
        var shot = await _context.FilmShots.FirstOrDefaultAsync(s => s.Id == shotId, ct)
                   ?? throw new ArgumentException("Không tìm thấy Shot.");
        var episode = await LoadEpisodeAsync(shot.FilmEpisodeId, ct);
        var shots = OrderedShots(episode);
        var target = shots.First(s => s.Id == shotId);

        await EnsureFramesAsync(episode, new[] { target }, ct);
        await EnsureVoicesAsync(episode, new[] { target }, ct);
        return await RenderShotVideoAsync(episode, target, IsSceneStart(shots, target), target == shots[^1], ct);
    }

    /// <summary>Records every line first: the GPU worker needs each shot's real length to size its motion clip.</summary>
    public async Task PrepareVoicesAsync(Guid episodeId, CancellationToken ct = default)
    {
        var episode = await LoadEpisodeAsync(episodeId, ct);
        if (await _meter.ShouldPauseAsync(ct))
            throw new InvalidOperationException("Đã vượt ngân sách AI của tháng (chế độ tạm dừng) — tăng ngân sách trong Cài đặt để dựng tiếp.");
        await SetStatusAsync(episode, "VoiceGenerating", ct);
        await EnsureVoicesAsync(episode, OrderedShots(episode), ct);
    }

    public async Task<string> AssembleEpisodeAsync(Guid episodeId, CancellationToken ct = default)
    {
        var episode = await LoadEpisodeAsync(episodeId, ct);
        var shots = OrderedShots(episode);
        if (shots.Count == 0)
            throw new InvalidOperationException("Tập phim chưa có kịch bản (0 shot) — cần viết kịch bản trước khi dựng.");

        _logger.LogInformation("Assembling Episode {Num} ({Title}): {Shots} shots", episode.EpisodeNumber, episode.Title, shots.Count);

        try
        {
            if (await _meter.ShouldPauseAsync(ct))
                throw new InvalidOperationException("Đã vượt ngân sách AI của tháng (chế độ tạm dừng) — tăng ngân sách trong Cài đặt để dựng tiếp.");

            await SetStatusAsync(episode, "VisualAssetsGenerating", ct);
            await EnsureFramesAsync(episode, shots, ct);

            await SetStatusAsync(episode, "VoiceGenerating", ct);
            await EnsureVoicesAsync(episode, shots, ct);

            await SetStatusAsync(episode, "FilmEditing", ct);
            var files = new List<string>();
            for (var i = 0; i < shots.Count; i++)
            {
                var shot = shots[i];
                var upToDate = shot.RenderStatus == "Completed"
                               && !string.IsNullOrWhiteSpace(shot.RenderedShotVideoPath)
                               && File.Exists(shot.RenderedShotVideoPath);
                files.Add(upToDate
                    ? shot.RenderedShotVideoPath!
                    : await RenderShotVideoAsync(episode, shot, IsSceneStart(shots, shot), i == shots.Count - 1, ct));
            }

            var episodeDir = Directory.CreateDirectory(Path.Combine(Root, "episodes")).FullName;
            var output = Path.Combine(episodeDir, $"episode_{episode.EpisodeNumber}_{episode.Id}.mp4");
            await ConcatAsync(files, output, episode, ct);

            await SetStatusAsync(episode, "QualityCheck", ct);
            var (score, duration) = await InspectAsync(output, shots.Any(s => s.DialogueText != null), shots.Sum(s => s.DurationSeconds), ct);

            episode.OutputVideoPath = output;
            episode.ActualDurationSeconds = Math.Round(duration, 1);
            episode.QualityScore = score;
            episode.ThumbnailPath = shots.First().RawImagePath;
            episode.ErrorMessage = null;
            episode.Status = "Completed";
            episode.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("Episode {Num} ready: {Duration:F1}s, QA {Score}/100 -> {Path}", episode.EpisodeNumber, duration, score, output);
            return output;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Episode {Num} render failed", episode.EpisodeNumber);
            episode.Status = "Failed";
            episode.ErrorMessage = ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message;
            episode.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<double> CheckQualityAsync(Guid episodeId, string videoPath, CancellationToken ct = default)
    {
        var episode = await LoadEpisodeAsync(episodeId, ct);
        var shots = OrderedShots(episode);
        var (score, _) = await InspectAsync(videoPath, shots.Any(s => s.DialogueText != null), shots.Sum(s => s.DurationSeconds), ct);
        return score;
    }

    // ─────────────────────────────── Frames (AI stills) ───────────────────────────────

    private async Task EnsureFramesAsync(FilmEpisode episode, IReadOnlyList<FilmShot> shots, CancellationToken ct)
    {
        var project = episode.FilmProject!;
        var aspect = project.AspectRatio == "16:9" ? "16:9" : "9:16";
        var framesDir = Directory.CreateDirectory(Path.Combine(Root, "frames")).FullName;

        // Frames already delivered (e.g. by the free GPU worker) are used as they are.
        shots = shots.Where(s => string.IsNullOrWhiteSpace(s.RawImagePath) || !File.Exists(s.RawImagePath)).ToList();
        if (shots.Count == 0) return;
        if (_gpu.IsConfigured && _configuration.GetValue("Film:Gpu:Enabled", true))
            throw new InvalidOperationException(
                $"{shots.Count} shot chưa có khung hình từ máy GPU {_gpu.Name}. Bấm \"Dựng tập\" để gửi lại lên GPU " +
                "(app không tự chuyển sang vẽ ảnh Gemini trả phí).");

        // Characters seen in these frames need a reference portrait first (keeps faces consistent).
        var charsById = project.Characters.ToDictionary(c => c.CharacterId, StringComparer.OrdinalIgnoreCase);
        var needed = shots.SelectMany(CharactersIn).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(charsById.ContainsKey).Select(id => charsById[id]).ToList();
        foreach (var character in needed)
        {
            await EnsurePortraitAsync(project, episode, character, ct);
        }

        // One image per distinct frame prompt; shots sharing a prompt reuse it.
        var jobs = new List<(string Path, string Prompt, List<FilmCharacter> Refs, List<FilmShot> Shots)>();
        foreach (var group in shots.GroupBy(s => s.VisualPrompt))
        {
            var refs = CharactersIn(group.First()).Where(charsById.ContainsKey).Select(id => charsById[id]).Take(3).ToList();
            var key = Hash($"{_gemini.ImageModel}|{aspect}|{group.Key}|{string.Join(",", refs.Select(r => r.Id + r.ConsistencyPrompt))}");
            var path = Path.Combine(framesDir, key + ".jpg");
            if (File.Exists(path))
            {
                foreach (var s in group) s.RawImagePath = path;
                continue;
            }
            jobs.Add((path, group.Key, refs, group.ToList()));
        }

        var parallel = Math.Clamp(_configuration.GetValue("Film:ImageParallelism", 3), 1, 6);
        foreach (var batch in jobs.Chunk(parallel))
        {
            await EnsureBudgetAsync(project, _gemini.EstimatedImageCostUsd * batch.Length, ct);
            var results = await Task.WhenAll(batch.Select(async job =>
            {
                var references = new List<FilmGeminiClient.ReferenceImage>();
                var intro = new StringBuilder();
                for (var i = 0; i < job.Refs.Count; i++)
                {
                    var portrait = job.Refs[i].FrontReferenceImageUrl!;
                    references.Add(new FilmGeminiClient.ReferenceImage(await File.ReadAllBytesAsync(portrait, ct), "image/jpeg"));
                    intro.Append($"Reference photo {i + 1} shows this person: {job.Refs[i].ConsistencyPrompt}. ");
                }
                var scene = $"{job.Prompt}. {project.VisualArtStyle}, photorealistic cinematic film still, no text, no subtitles, no watermark";
                var prompt = references.Count > 0
                    ? intro + "Keep each person's face, hairstyle and outfit exactly as in the reference photos, but create a completely new picture (new pose, framing and background) of this scene: " + scene
                    : scene;
                var image = await _gemini.GenerateImageAsync(prompt, aspect, references, ct);
                await SaveAsJpegAsync(image.Bytes, job.Path, ct);
                return (job, image);
            }));

            foreach (var (job, image) in results)
            {
                await _costController.RecordCostAsync(project.Id, episode.Id, null, "Gemini", "ImageGen", image.Tokens, image.CostUsd, image.FreeKey,
                    $"Khung hình tập {episode.EpisodeNumber}", ct);
                foreach (var s in job.Shots) s.RawImagePath = job.Path;
            }
        }
        await _context.SaveChangesAsync(ct);
    }

    private async Task EnsurePortraitAsync(FilmProject project, FilmEpisode episode, FilmCharacter character, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(character.FrontReferenceImageUrl) && File.Exists(character.FrontReferenceImageUrl)) return;

        var dir = Directory.CreateDirectory(Path.Combine(Root, "characters")).FullName;
        var path = Path.Combine(dir, $"{character.Id}_{Hash(character.ConsistencyPrompt)}.jpg");
        if (!File.Exists(path))
        {
            await EnsureBudgetAsync(project, _gemini.EstimatedImageCostUsd, ct);
            var prompt = $"Character reference portrait, waist-up, facing the camera, neutral expression, plain dark grey studio background, soft even lighting. " +
                         $"{character.ConsistencyPrompt}. {project.VisualArtStyle}, photorealistic, no text, no watermark.";
            var image = await _gemini.GenerateImageAsync(prompt, "3:4", Array.Empty<FilmGeminiClient.ReferenceImage>(), ct);
            await SaveAsJpegAsync(image.Bytes, path, ct);
            await _costController.RecordCostAsync(project.Id, episode.Id, null, "Gemini", "ImageGen", image.Tokens, image.CostUsd, image.FreeKey,
                $"Ảnh tham chiếu nhân vật {character.Name}", ct);
        }
        character.FrontReferenceImageUrl = path;
        await _context.SaveChangesAsync(ct);
    }

    private async Task EnsureBudgetAsync(FilmProject project, decimal estimate, CancellationToken ct)
    {
        if (!await _costController.CanExecutePaidTaskAsync(project.Id, estimate, ct))
        {
            throw new InvalidOperationException(
                $"Dự án đã chạm trần ngân sách {project.TotalBudgetLimitUsd:0.##} USD (đã chi {project.TotalActualCostUsd:0.####} USD). " +
                "Tăng ngân sách dự án để dựng tiếp.");
        }
    }

    private static IEnumerable<string> CharactersIn(FilmShot shot) =>
        (shot.CharacterIdsPresent ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ─────────────────────────────── Voices ───────────────────────────────

    private async Task EnsureVoicesAsync(FilmEpisode episode, IReadOnlyList<FilmShot> shots, CancellationToken ct)
    {
        var project = episode.FilmProject!;
        var voices = project.Characters.ToDictionary(c => c.CharacterId, c => c.VoiceModel, StringComparer.OrdinalIgnoreCase);
        var narrator = GeminiFilmVoices.Narrator(voices.Values);
        var audioPricePerM = decimal.TryParse(_configuration["Ai:Pricing:TtsAudioPerM"], NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p : 10m;

        foreach (var shot in shots.Where(s => !string.IsNullOrWhiteSpace(s.DialogueText)))
        {
            if (!string.IsNullOrWhiteSpace(shot.AudioVoicePath) && File.Exists(shot.AudioVoicePath)) continue;

            var voice = shot.SpeakerCharacterId != null && voices.TryGetValue(shot.SpeakerCharacterId, out var v) && GeminiFilmVoices.IsGeminiVoice(v)
                ? v
                : narrator;

            var result = await _tts.SynthesizeSpeechWithSubtitlesAsync(new TtsSynthesisRequest
            {
                ProjectId = shot.Id,
                Text = shot.DialogueText!,
                Voice = voice,
                Speed = "+0%"
            }, ct);

            if (string.IsNullOrEmpty(result.AudioFilePath) || !File.Exists(result.AudioFilePath))
                throw new InvalidOperationException($"Không tạo được giọng đọc cho cảnh {shot.FilmScene?.SceneNumber}, shot {shot.ShotNumber}.");

            shot.AudioVoicePath = result.AudioFilePath;
            shot.DurationSeconds = Math.Round(result.AudioDurationSeconds + GapAfterLine, 2);
            shot.RenderStatus = "Pending"; // new audio → the shot video must be rebuilt

            if (!result.FromCache)
            {
                // Gemini audio ≈ 25 tokens per second (estimate; the exact amount is in the AI usage log).
                var tokens = (long)Math.Ceiling(result.AudioDurationSeconds * 25);
                await _costController.RecordCostAsync(project.Id, episode.Id, shot.Id, "Gemini", "VoiceTTS", tokens,
                    Math.Round(tokens * audioPricePerM / 1_000_000m, 6), false, $"Giọng {voice}", ct);
            }
            await _context.SaveChangesAsync(ct); // keep finished lines if a later one fails
        }
    }

    // ─────────────────────────────── Shot video ───────────────────────────────

    private async Task<string> RenderShotVideoAsync(FilmEpisode episode, FilmShot shot, bool sceneStart, bool lastShot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(shot.RawImagePath) || !File.Exists(shot.RawImagePath))
            throw new InvalidOperationException($"Shot {shot.ShotNumber} chưa có khung hình.");

        var vertical = episode.FilmProject?.AspectRatio != "16:9";
        var (w, h) = vertical ? (1080, 1920) : (1920, 1080);
        var hasVoice = !string.IsNullOrWhiteSpace(shot.AudioVoicePath) && File.Exists(shot.AudioVoicePath);
        var duration = hasVoice ? shot.DurationSeconds : Math.Clamp(shot.DurationSeconds, 2.0, 5.0);
        var frames = Math.Max(Fps, (int)Math.Round(duration * Fps));
        duration = frames / (double)Fps;

        var dir = Directory.CreateDirectory(Path.Combine(Root, "shots")).FullName;
        var output = Path.Combine(dir, $"shot_{shot.Id}.mp4");

        shot.RenderStatus = "Rendering";
        await _context.SaveChangesAsync(ct);

        var d = duration.ToString("F3", CultureInfo.InvariantCulture);
        var fades = new StringBuilder();
        var afades = new StringBuilder();
        if (sceneStart) fades.Append(",fade=t=in:st=0:d=0.4");
        if (lastShot)
        {
            var st = Math.Max(0, duration - 1.2).ToString("F3", CultureInfo.InvariantCulture);
            fades.Append($",fade=t=out:st={st}:d=1.2");
            afades.Append($",afade=t=out:st={st}:d=1.2");
        }

        var clip = !string.IsNullOrWhiteSpace(shot.MotionVideoPath) && File.Exists(shot.MotionVideoPath) ? shot.MotionVideoPath : null;
        string video;
        string visualInput;
        if (clip != null)
        {
            // Real motion clip: upscale to the delivery size, sharpen lightly, slow it down a little when the line
            // is longer than the clip (max 1.5x) and hold the last frame for whatever remains.
            // The last part of an image-to-video clip is where it tends to smear, so only the first
            // Film:MotionKeepFraction (default 85%) is used.
            var keep = Math.Clamp(_configuration.GetValue("Film:MotionKeepFraction", 0.85), 0.5, 1.0);
            var clipSeconds = await ProbeDurationAsync(clip, ct) * keep;
            var slow = clipSeconds > 0 ? Math.Clamp(duration / clipSeconds, 1.0, 1.5) : 1.0;
            var s = slow.ToString("F3", CultureInfo.InvariantCulture);
            var kept = clipSeconds.ToString("F3", CultureInfo.InvariantCulture);
            video = $"[0:v]trim=duration={kept},setpts={s}*(PTS-STARTPTS),scale={w}:{h}:force_original_aspect_ratio=increase:flags=lanczos,crop={w}:{h},setsar=1," +
                    $"unsharp=5:5:0.6:5:5:0.0,fps={Fps},tpad=stop_mode=clone:stop_duration={d},trim=end_frame={frames},format=yuv420p{fades}[v]";
            visualInput = $"-i \"{clip}\"";
        }
        else
        {
            video = $"[0:v]scale={w * 2}:{h * 2}:force_original_aspect_ratio=increase,crop={w * 2}:{h * 2},setsar=1," +
                    $"{ZoomPan(shot.CameraMovement, frames, w, h)},format=yuv420p{fades}[v]";
            visualInput = $"-i \"{shot.RawImagePath}\"";
        }
        var audio = $"[1:a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,apad,atrim=0:{d}{afades}[a]";
        var audioInput = hasVoice
            ? $"-i \"{shot.AudioVoicePath}\""
            : $"-f lavfi -t {d} -i anullsrc=r=48000:cl=stereo";

        var args = $"-hide_banner -y {visualInput} {audioInput} -filter_complex \"{video};{audio}\" " +
                   $"-map \"[v]\" -map \"[a]\" -frames:v {frames} -r {Fps} -c:v libx264 -preset veryfast -crf 21 -pix_fmt yuv420p " +
                   $"-color_range tv -c:a aac -b:a 160k -ar 48000 -ac 2 -t {d} \"{output}\"";

        var (code, _, err) = await RunAsync(_ffmpeg, args, ct);
        if (code != 0 || !File.Exists(output) || new FileInfo(output).Length < 10_000)
        {
            shot.RenderStatus = "Failed";
            shot.ErrorMessage = Tail(err);
            await _context.SaveChangesAsync(ct);
            throw new InvalidOperationException($"FFmpeg dựng shot {shot.ShotNumber} thất bại: {Tail(err)}");
        }

        shot.DurationSeconds = Math.Round(duration, 2);
        shot.RenderedShotVideoPath = output;
        shot.RenderStatus = "Completed";
        shot.ErrorMessage = null;
        shot.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return output;
    }

    /// <summary>Ken Burns camera move over exactly <paramref name="n"/> frames on a 2× upscaled still (less jitter).</summary>
    private static string ZoomPan(string? move, int n, int w, int h)
    {
        const string cx = "(iw-iw/zoom)/2", cy = "(ih-ih/zoom)/2";
        var (z, x, y) = move switch
        {
            "SlowZoomOut" => ($"1.16-0.16*on/{n}", cx, cy),
            "PanRight" => ("1.12", $"(iw-iw/zoom)*on/{n}", cy),
            "PanLeft" => ("1.12", $"(iw-iw/zoom)*(1-on/{n})", cy),
            "TiltUp" => ("1.12", cx, $"(ih-ih/zoom)*(1-on/{n})"),
            "TiltDown" => ("1.12", cx, $"(ih-ih/zoom)*on/{n}"),
            "Static" => ($"1.03+0.03*on/{n}", cx, cy),
            _ => ($"1+0.16*on/{n}", cx, cy) // SlowZoomIn and anything unknown
        };
        return $"zoompan=z='{z}':x='{x}':y='{y}':d={n}:s={w}x{h}:fps={Fps}";
    }

    // ─────────────────────────────── Assembly & QA ───────────────────────────────

    private async Task ConcatAsync(IReadOnlyList<string> files, string output, FilmEpisode episode, CancellationToken ct)
    {
        var list = Path.Combine(Path.GetDirectoryName(output)!, $"concat_{episode.Id}.txt");
        var sb = new StringBuilder();
        foreach (var f in files) sb.Append("file '").Append(f.Replace("\\", "/").Replace("'", "'\\''")).Append("'\n");
        await File.WriteAllTextAsync(list, sb.ToString(), new UTF8Encoding(false), ct);

        if (File.Exists(output)) File.Delete(output);

        var brandName = episode.FilmProject?.FilmPage?.Name;
        if (string.IsNullOrWhiteSpace(brandName)) brandName = episode.FilmProject?.Title;
        if (string.IsNullOrWhiteSpace(brandName)) brandName = "FilmFactory";

        var cleanBrand = Regex.Replace(brandName, @"[^\w\d]", "");
        var brandTag = "@" + (cleanBrand.Length > 20 ? cleanBrand[..20] : cleanBrand);

        // Anti-reup & watermark filter pipeline:
        // 1. Watermark: sleek translucent pill badge at top right (safe from TikTok/Reels UI)
        // 2. Anti-reup micro-timing: setpts=PTS/1.001 (video) and atempo=1.001 (audio) breaks automated duplicate hash scrapers
        // 3. Cinematic micro-color grading: eq=contrast=1.02:saturation=1.03:brightness=0.005
        var vFilter = $"setpts=PTS/1.001,eq=contrast=1.02:saturation=1.03:brightness=0.005,drawtext=text='{brandTag}':x=w-tw-40:y=80:fontsize=26:fontcolor=white@0.85:box=1:boxcolor=black@0.4:boxborderw=8";
        var aFilter = "atempo=1.001";
        var titleMeta = (episode.Title ?? $"Episode {episode.EpisodeNumber}").Replace("\"", "\\\"");
        var artistMeta = brandTag.Replace("\"", "\\\"");

        var metaArgs = $"-metadata title=\"{titleMeta}\" -metadata artist=\"{artistMeta}\" -metadata copyright=\"Copyright (C) {artistMeta}. All Rights Reserved.\" -metadata comment=\"AI Film Factory Anti-Reup Protected\"";

        var (code, _, err) = await RunAsync(_ffmpeg,
            $"-hide_banner -y -f concat -safe 0 -i \"{list}\" -vf \"{vFilter}\" -af \"{aFilter}\" " +
            $"-c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -r {Fps} -c:a aac -b:a 160k -ar 48000 -ac 2 " +
            $"{metaArgs} -movflags +faststart \"{output}\"", ct);

        if (code != 0 || !File.Exists(output))
        {
            _logger.LogWarning("Watermarked concat failed ({Err}), falling back to direct concat", Tail(err));
            (code, _, err) = await RunAsync(_ffmpeg,
                $"-hide_banner -y -f concat -safe 0 -i \"{list}\" -c:v libx264 -preset veryfast -crf 21 -pix_fmt yuv420p -r {Fps} " +
                $"-c:a aac -b:a 160k -ar 48000 -ac 2 {metaArgs} -movflags +faststart \"{output}\"", ct);
        }
        try { File.Delete(list); } catch { /* best effort */ }

        if (code != 0 || !File.Exists(output) || new FileInfo(output).Length < 100_000)
            throw new InvalidOperationException($"FFmpeg ghép tập phim thất bại: {Tail(err)}");
    }

    /// <summary>Real checks on the finished file. Throws when it would not be watchable (no picture, silent, too short).</summary>
    private async Task<(double Score, double Duration)> InspectAsync(string path, bool expectSpeech, double expectedSeconds, CancellationToken ct)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("Không tìm thấy file video tập phim.");

        var (_, probe, _) = await RunAsync(_ffprobe,
            $"-v error -show_entries stream=codec_type,codec_name,width,height,pix_fmt:format=duration -of default=nw=1 \"{path}\"", ct);
        var hasVideo = probe.Contains("codec_type=video");
        var hasAudio = probe.Contains("codec_type=audio");
        var durMatch = Regex.Match(probe, @"duration=([\d.]+)");
        var duration = durMatch.Success ? double.Parse(durMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0;

        if (!hasVideo) throw new InvalidOperationException("Kiểm tra chất lượng: file không có hình.");
        if (!hasAudio) throw new InvalidOperationException("Kiểm tra chất lượng: file không có âm thanh.");

        var (_, _, vol) = await RunAsync(_ffmpeg, $"-hide_banner -nostats -i \"{path}\" -vn -af volumedetect -f null -", ct);
        var mean = Regex.Match(vol, @"mean_volume:\s*(-?[\d.]+|-inf) dB");
        var meanDb = mean.Success && double.TryParse(mean.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : -999;
        if (expectSpeech && meanDb < -45)
            throw new InvalidOperationException($"Kiểm tra chất lượng: âm thanh gần như câm (mean {meanDb:F1} dB) dù tập có lời thoại.");

        var score = 100.0;
        if (expectedSeconds > 0 && Math.Abs(duration - expectedSeconds) > Math.Max(3, expectedSeconds * 0.05)) score -= 15;
        if (!probe.Contains("pix_fmt=yuv420p")) score -= 10;
        if (meanDb < -30) score -= 10;
        return (score, duration);
    }

    // ─────────────────────────────── Helpers ───────────────────────────────

    private async Task<FilmEpisode> LoadEpisodeAsync(Guid episodeId, CancellationToken ct) =>
        await _context.FilmEpisodes
            .Include(e => e.FilmProject).ThenInclude(p => p!.Characters)
            .Include(e => e.FilmProject).ThenInclude(p => p!.FilmPage)
            .Include(e => e.Scenes).ThenInclude(s => s.Shots)
            .FirstOrDefaultAsync(e => e.Id == episodeId, ct)
        ?? throw new ArgumentException("Không tìm thấy tập phim.");

    private static List<FilmShot> OrderedShots(FilmEpisode episode) =>
        episode.Scenes.OrderBy(s => s.SceneNumber).SelectMany(s => s.Shots.OrderBy(sh => sh.ShotNumber)).ToList();

    private static bool IsSceneStart(List<FilmShot> shots, FilmShot shot)
    {
        var i = shots.IndexOf(shot);
        return i == 0 || shots[i - 1].FilmSceneId != shot.FilmSceneId;
    }

    private async Task SetStatusAsync(FilmEpisode episode, string status, CancellationToken ct)
    {
        episode.Status = status;
        episode.ErrorMessage = null;
        episode.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>Stores any returned image format as a high-quality JPEG (what FFmpeg and the reference upload expect).</summary>
    private async Task SaveAsJpegAsync(byte[] bytes, string path, CancellationToken ct)
    {
        var isJpeg = bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8;
        if (isJpeg)
        {
            await File.WriteAllBytesAsync(path, bytes, ct);
            return;
        }
        var tmp = path + ".src";
        await File.WriteAllBytesAsync(tmp, bytes, ct);
        var (code, _, err) = await RunAsync(_ffmpeg, $"-hide_banner -y -i \"{tmp}\" -q:v 2 \"{path}\"", ct);
        try { File.Delete(tmp); } catch { /* best effort */ }
        if (code != 0 || !File.Exists(path)) throw new InvalidOperationException($"Không lưu được ảnh khung hình: {Tail(err)}");
    }

    private async Task<double> ProbeDurationAsync(string path, CancellationToken ct)
    {
        var (_, output, _) = await RunAsync(_ffprobe, $"-v error -show_entries format=duration -of csv=p=0 \"{path}\"", ct);
        return double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..24].ToLowerInvariant();

    private static string Tail(string s)
    {
        var t = (s ?? string.Empty).Trim();
        return t.Length > 600 ? "…" + t[^600..] : t;
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

    private static (string Ffmpeg, string Ffprobe) FindFfmpeg(IConfiguration configuration)
    {
        var configured = configuration["Ffmpeg:BinaryPath"];
        var ffmpeg = !string.IsNullOrWhiteSpace(configured) && File.Exists(configured)
            ? configured
            : EdgeTtsService.FindFfmpegExecutable() ?? "ffmpeg";

        // ffprobe ships next to ffmpeg; otherwise rely on PATH (Linux containers).
        var dir = Path.GetDirectoryName(ffmpeg);
        var probeName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        var probe = !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, probeName)) ? Path.Combine(dir, probeName) : "ffprobe";
        return (ffmpeg, probe);
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Rendering;
using NewsStudio.Infrastructure.Services;
using NewsStudio.Infrastructure.Trending;

namespace NewsStudio.Infrastructure.Growth;

/// <summary>
/// News Pages in the autopilot: the hottest story of the Page's own topic (its "Tự động đăng" topic — sports for a
/// sports Page, the hottest of all for a general Page), never one the app already used:
/// <list type="bullet">
/// <item>wall post: the article's own photo with a short headline + summary written on it (the "Điểm tin" card style, no
/// voice) and a short post about the article (facts from the article only, source named);</item>
/// <item>Story: a voiced news video made by the same pipeline as the auto-posted news videos, kept under a minute.</item>
/// </list>
/// </summary>
public class GrowthNewsService
{
    public const int StoryVideoSeconds = 50;
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromDays(3);
    private static readonly int[] LookBackHours = { 24, 48 };

    private readonly IApplicationDbContext _db;
    private readonly ITrendingNewsService _trending;
    private readonly IArticleExtractorService _extractor;
    private readonly IVideoProjectService _projects;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<GrowthNewsService> _logger;

    public GrowthNewsService(IApplicationDbContext db, ITrendingNewsService trending, IArticleExtractorService extractor, IVideoProjectService projects,
        IHttpClientFactory http, ILogger<GrowthNewsService> logger)
    {
        _db = db;
        _trending = trending;
        _extractor = extractor;
        _projects = projects;
        _http = http;
        _logger = logger;
    }

    /// <summary>The Page's news settings when it is a news Page ("Tự động đăng" on), else null.</summary>
    public Task<AutoPostSettings?> NewsSettingsAsync(Guid pageId, CancellationToken ct) =>
        _db.AutoPostSettings.AsNoTracking().FirstOrDefaultAsync(s => s.FacebookAccountId == pageId && s.Enabled, ct);

    /// <summary>Scripts or News for a Page, from its choice (Auto = News when the Page makes auto news videos).</summary>
    public static string EffectiveMode(string? chosen, bool hasNewsVideos) =>
        chosen switch { "Scripts" => "Scripts", "News" => "News", _ => hasNewsVideos ? "News" : "Scripts" };

    // ───────────────────────── picking ─────────────────────────

    /// <summary>
    /// The hottest story of the topic that nobody used yet: not made into a video (by any Page), not used by the
    /// autopilot on any Page in the last days, not the same story cluster or title as a recent one.
    /// </summary>
    public async Task<TrendingArticleDto?> PickAsync(Guid pageId, AutoPostSettings settings, DateTime now, CancellationToken ct, ISet<string>? skip = null)
    {
        var keywords = NewsTopics.ParseKeywords(settings.TopicKeywords);
        var since = now - RepeatWindow;
        var usedUrls = (await _db.VideoProjects.AsNoTracking().Where(p => p.CreatedAtUtc >= now.AddDays(-14)).Select(p => p.SourceUrl).ToListAsync(ct))
            .Concat(await _db.AutoPostRuns.AsNoTracking().Where(r => r.ArticleUrl != null && r.CreatedAtUtc >= now.AddDays(-14)).Select(r => r.ArticleUrl!).ToListAsync(ct))
            .Select(AutoPostService.NormalizeUrl)
            .Concat(await _db.GrowthNewsUsages.AsNoTracking().Where(u => u.FacebookAccountId == pageId || u.CreatedAtUtc >= since).Select(u => u.Url).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (skip != null) usedUrls.UnionWith(skip);
        var usedClusters = (await _db.AutoPostRuns.AsNoTracking().Where(r => r.CreatedAtUtc >= since).Select(r => new { r.ClusterId, r.TrendingArticleId }).ToListAsync(ct))
            .SelectMany(r => new[] { r.ClusterId, r.TrendingArticleId })
            .Concat((await _db.GrowthNewsUsages.AsNoTracking().Where(u => u.CreatedAtUtc >= since).Select(u => new { u.ClusterId, u.TrendingArticleId }).ToListAsync(ct))
                .SelectMany(u => new[] { u.ClusterId, u.TrendingArticleId }))
            .Where(id => id != null).Select(id => id!.Value).ToHashSet();
        var usedTitles = (await _db.GrowthNewsUsages.AsNoTracking().Where(u => u.CreatedAtUtc >= since).Select(u => u.Title).ToListAsync(ct))
            .Concat(await _db.VideoProjects.AsNoTracking().Where(p => p.CreatedAtUtc >= since).Select(p => p.Title).ToListAsync(ct))
            .Select(NormalizeTitle).Where(t => t.Length > 0).ToHashSet();

        foreach (var hours in LookBackHours)
        {
            var overview = await _trending.GetTrendingAsync(new TrendingQuery { Region = "all", Hours = hours, Limit = 150, Topic = settings.Topic, Keywords = keywords }, ct);
            var items = overview.Items.OrderByDescending(i => i.HotScore).ToList();
            if (items.Count == 0) continue;
            var ids = items.Select(i => i.Id).ToList();
            var clusters = await _db.TrendingArticles.AsNoTracking().Where(t => ids.Contains(t.Id)).Select(t => new { t.Id, t.ClusterId }).ToDictionaryAsync(t => t.Id, t => t.ClusterId, ct);
            foreach (var item in items)
            {
                if (usedUrls.Contains(AutoPostService.NormalizeUrl(item.Url))) continue;
                if (usedClusters.Contains(item.Id) || (clusters.GetValueOrDefault(item.Id) is { } cl && usedClusters.Contains(cl))) continue;
                if (usedTitles.Contains(NormalizeTitle(item.Title))) continue;
                return item;
            }
        }
        return null;
    }

    public static string NormalizeTitle(string? title) => Regex.Replace((title ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    public async Task<Guid?> ClusterOfAsync(Guid trendingId, CancellationToken ct) =>
        await _db.TrendingArticles.AsNoTracking().Where(t => t.Id == trendingId).Select(t => t.ClusterId).FirstOrDefaultAsync(ct);

    // ───────────────────────── reading the article ─────────────────────────

    public sealed record ArticleText(string Text, List<string> ImageUrls);

    public async Task<ArticleText> ReadAsync(TrendingArticleDto article, CancellationToken ct)
    {
        var images = new List<string>();
        var text = article.Summary ?? "";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var raw = await _extractor.ExtractFromUrlAsync(article.Url, timeout.Token);
            if (!string.IsNullOrWhiteSpace(raw.ContentBody)) text = raw.ContentBody;
            else if (!string.IsNullOrWhiteSpace(raw.Description)) text = raw.Description;
            images.AddRange(raw.Images.OrderBy(i => i.Order).Select(i => i.Url));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogInformation("Growth news: could not read {Url} ({Message}); using the feed summary", article.Url, ex.Message);
        }
        if (!string.IsNullOrWhiteSpace(article.ImageUrl)) images.Add(article.ImageUrl);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length > 3000) text = text[..3000];
        return new ArticleText(text, images.Where(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase)).Distinct().Take(5).ToList());
    }

    /// <summary>The article's first real, large-enough photo (≥ 600 px wide), saved as a JPEG/PNG file; null when none.</summary>
    public async Task<string?> DownloadPhotoAsync(IReadOnlyList<string> urls, string referer, string pathWithoutExtension, CancellationToken ct)
    {
        var client = _http.CreateClient("growth-news-photo");
        client.Timeout = TimeSpan.FromSeconds(20);
        foreach (var url in urls)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124 Safari/537.36");
                req.Headers.TryAddWithoutValidation("Referer", referer);
                using var res = await client.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode) continue;
                var bytes = await res.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < 15_000 || bytes.Length > 15_000_000) continue;
                var kind = GrowthContentService.Sniff(bytes.AsSpan(0, Math.Min(16, bytes.Length)));
                if (kind is not { Kind: "Image" }) continue;
                var (w, h) = GrowthContentService.ImageSize(bytes);
                if (w < 600 || h < 300) continue;
                var path = pathWithoutExtension + (kind.Value.Mime == "image/png" ? ".png" : ".jpg");
                await File.WriteAllBytesAsync(path, bytes, ct);
                return path;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogInformation("Growth news: photo {Url} not usable: {Message}", url, ex.Message);
            }
        }
        return null;
    }

    // ───────────────────────── the card ─────────────────────────

    /// <summary>
    /// The article photo with the headline + summary written on it, in the Page's topic colours with its name / logo — the
    /// same drawing as the evening "Điểm tin" cards (Scripts/generate_digest_cards.py). Returns the 4:5 picture.
    /// </summary>
    public async Task<string> RenderCardAsync(Guid pageId, string pageName, string topic, string photo, string headline, string summary, string source,
        string folder, CancellationToken ct)
    {
        var python = FFmpegRendererService.FindPythonExecutable() ?? throw new InvalidOperationException("Không tìm thấy Python trên máy chủ để vẽ ảnh tin.");
        var script = FFmpegRendererService.FindScriptPath("generate_digest_cards.py") ?? throw new InvalidOperationException("Thiếu script vẽ ảnh tin (generate_digest_cards.py).");
        var logo = await _db.AutoPostSettings.AsNoTracking().Where(s => s.FacebookAccountId == pageId).Select(s => s.BrandLogo).FirstOrDefaultAsync(ct);
        string? logoPath = null;
        if (logo is { Length: > 0 })
        {
            logoPath = Path.Combine(folder, "brand_logo.png");
            await File.WriteAllBytesAsync(logoPath, logo, ct);
        }
        var spec = new
        {
            logo = logoPath, out_dir = folder, brand = pageName, theme = topic,
            topic_label = topic is NewsTopics.General or NewsTopics.Custom ? "" : NewsTopics.NameOf(topic),
            date = GrowthTime.Today(DateTime.UtcNow).ToString("dd/MM/yyyy"), title = "Tin nóng",
            items = new[] { new { photo, headline, summary, source } }
        };
        var specPath = Path.Combine(folder, "spec.json");
        await File.WriteAllTextAsync(specPath, JsonSerializer.Serialize(spec), new UTF8Encoding(false), ct);
        var psi = new ProcessStartInfo { FileName = python, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add(specPath);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Không chạy được Python.");
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        _ = proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        var card = Path.Combine(folder, "post_01.jpg");
        if (proc.ExitCode != 0 || !File.Exists(card))
        {
            var err = await stderr;
            throw new InvalidOperationException("Vẽ ảnh tin thất bại: " + (err.Length > 400 ? err[^400..] : err));
        }
        return card;
    }

    // ───────────────────────── AI text ─────────────────────────

    public static (string System, string User) BuildPostPrompt(string pageName, string topicName, TrendingArticleDto article, string text)
    {
        var system = "Bạn là biên tập viên tin tức của một Facebook Page Việt Nam. Chỉ dùng dữ kiện có trong bài báo, trung lập, không giật tít. Chỉ trả về JSON hợp lệ.";
        var user =
            $"Page: {pageName} — chủ đề: {topicName}\nNguồn: {article.SourceName}\nTiêu đề gốc: {article.Title}\nNội dung bài báo:\n{text}\n\n" +
            "Viết:\n" +
            "- headline: tiêu đề ngắn in lên ảnh, tối đa 12 chữ, đúng sự thật, không viết hoa toàn bộ.\n" +
            "- summary: 1–2 câu tóm tắt in lên ảnh, tối đa 35 chữ, chỉ dữ kiện quan trọng nhất.\n" +
            "- caption: bài đăng 60–140 chữ kể lại tin cho dễ hiểu (ai, cái gì, khi nào, ở đâu, vì sao); có thể kết bằng 1 câu hỏi mời thảo luận; " +
            "không bịa số liệu, không suy đoán, không thêm ý kiến cá nhân như sự thật; 0–2 emoji.\n" +
            "- hashtags: 2–4 hashtag tiếng Việt liên quan.\n" +
            "JSON: {\"headline\":\"...\",\"summary\":\"...\",\"caption\":\"...\",\"hashtags\":[\"#...\"]}";
        return (system, user);
    }

    public sealed record NewsPostText(string Headline, string Summary, string Caption, string Hashtags);

    public static NewsPostText? ParsePost(string json, string sourceName)
    {
        try
        {
            var root = JsonNode.Parse(json);
            string S(string k) => root?[k]?.GetValue<string>()?.Trim() ?? "";
            var headline = S("headline");
            var summary = S("summary");
            var caption = S("caption");
            if (headline.Length == 0 || summary.Length == 0 || caption.Length == 0) return null;
            var tags = root?["hashtags"] is JsonArray arr
                ? string.Join(" ", arr.Select(t => t?.GetValue<string>()?.Trim().Replace(" ", "") ?? "").Where(t => t.Length > 1).Select(t => t.StartsWith('#') ? t : "#" + t).Distinct().Take(4))
                : "";
            caption = caption + $"\n\n📰 Nguồn: {sourceName}";
            return new NewsPostText(Cut(headline, 120), Cut(summary, 300), Cut(caption, GrowthContentService.MaxCaption), tags);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    private static string Cut(string s, int max) => s.Length > max ? s[..max] : s;

    // ───────────────────────── voiced Story video ─────────────────────────

    /// <summary>Starts a short news video (same pipeline, voice and look as the auto-posted news videos) for a Story.</summary>
    public async Task<Guid> StartStoryVideoAsync(Guid pageId, AutoPostSettings settings, TrendingArticleDto article, CancellationToken ct)
    {
        var project = await _projects.CreateProjectAsync(new CreateVideoProjectDto
        {
            Title = string.Empty, SourceUrl = article.Url, Publisher = article.SourceName, TargetDurationSeconds = StoryVideoSeconds,
            AiProvider = settings.AiProvider, Voice = settings.Voice, VoiceSpeed = settings.VoiceSpeed, FacebookAccountId = pageId
        }, ct);
        await _projects.EnqueueProjectProcessingAsync(project.Id, ct);
        return project.Id;
    }
}

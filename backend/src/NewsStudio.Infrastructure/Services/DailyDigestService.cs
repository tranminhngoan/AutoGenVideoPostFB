using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
using NewsStudio.Infrastructure.Trending;

namespace NewsStudio.Infrastructure.Services;

/// <summary>
/// Daily "Điểm tin nóng" of every Facebook Page (evening, once a day):
/// <list type="number">
/// <item>The day's hottest stories of the page's topic are collected from every news source (hot score:
/// how many outlets cover it, Google Trends searches, front-page placement, freshness).</item>
/// <item>AI (Gemini) picks the most striking ones (distinct events), then reads each article and writes a
/// short headline + 1–2 sentence summary — only facts from the article.</item>
/// <item>Every story becomes a picture: its own photo with the headline and summary written on it, in the
/// page's topic colours and with the page name (4:5 for the wall, 9:16 for the Story), plus a cover.</item>
/// <item>Posted as ONE wall post with all pictures and ONE Story (slideshow video of the 9:16 pictures).</item>
/// </list>
/// Errors are recorded and shown in the app; nothing is made up when a step fails.
/// </summary>
public class DailyDigestService : IDailyDigestService
{
    public const int MinItems = 3;
    public const int MaxItems = 9;
    /// <summary>Story slideshow timing: cover + each story (9 stories → 58 s, under the 60 s Story limit).</summary>
    public const double CoverSeconds = 4;
    public const double ItemSeconds = 6;

    public const string Working = "Working";
    public const string Published = "Published";
    public const string Partial = "Partial";
    public const string Failed = "Failed";
    public const string Preview = "Preview";

    /// <summary>The digest still runs this long after its time (server restarting at that moment).</summary>
    private static readonly TimeSpan LateWindow = TimeSpan.FromHours(2);
    private static readonly TimeSpan KeepFilesFor = TimeSpan.FromDays(3);
    private const int CandidatePool = 30;
    private static readonly Regex ImageFileName = new(@"^(post|story)_\d{2}\.jpg$", RegexOptions.Compiled);
    private static readonly SemaphoreSlim Lock = new(1, 1);

    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private readonly IApplicationDbContext _context;
    private readonly ITrendingNewsService _trending;
    private readonly IArticleExtractorService _extractor;
    private readonly GeminiAiEditorService _gemini;
    private readonly IVideoRendererService _renderer;
    private readonly IFacebookPublishingService _facebook;
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DailyDigestService> _logger;

    public DailyDigestService(
        IApplicationDbContext context,
        ITrendingNewsService trending,
        IArticleExtractorService extractor,
        GeminiAiEditorService gemini,
        IVideoRendererService renderer,
        IFacebookPublishingService facebook,
        HttpClient http,
        IConfiguration configuration,
        ILogger<DailyDigestService> logger)
    {
        _context = context;
        _trending = trending;
        _extractor = extractor;
        _gemini = gemini;
        _renderer = renderer;
        _facebook = facebook;
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(30);
        _configuration = configuration;
        _logger = logger;
    }

    // ---------------------------------------------------------------- schedule

    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        if (!await Lock.WaitAsync(0, cancellationToken)) return; // a digest is being made right now
        try
        {
            var now = DateTime.UtcNow;
            var today = AutoPostService.VietnamDate(now);
            var todayKey = DateKey(today);

            await FailInterruptedRunsAsync(cancellationToken);

            foreach (var (page, settings) in await LoadPagesAsync(cancellationToken))
            {
                if (!settings.Enabled || !settings.DigestEnabled) continue;
                var time = AutoPostService.ParseSlots(settings.DigestTime).FirstOrDefault() ?? "20:30";
                var at = AutoPostService.SlotUtc(today, time);
                if (now < at || now > at + LateWindow) continue;
                var statuses = await _context.DailyDigestRuns
                    .Where(r => r.FacebookAccountId == page.Id && r.DigestDate == todayKey && !r.IsPreview)
                    .Select(r => r.Status)
                    .ToListAsync(cancellationToken);
                if (!ShouldRunScheduled(statuses)) continue;

                _logger.LogInformation("Daily digest {Date} starting for page {Page}", todayKey, page.PageName);
                try
                {
                    await RunAsync(page, settings, publish: true, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Daily digest for page {Page} crashed", page.PageName);
                }
            }

            DeleteOldFolders();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never fail the recurring job (Hangfire would retry it); the next tick tries again.
            _logger.LogError(ex, "Daily digest tick failed");
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>
    /// Today's scheduled digest runs unless one is done or in progress; a failed one is tried once more
    /// (e.g. interrupted by a restart), but not every 5 minutes for hours when the cause is lasting.
    /// </summary>
    public static bool ShouldRunScheduled(IReadOnlyCollection<string> todaysStatuses) =>
        !todaysStatuses.Any(s => s is Published or Partial or Working)
        && todaysStatuses.Count(s => s == Failed) < MaxScheduledAttempts;

    public const int MaxScheduledAttempts = 2;

    /// <summary>A run still "Working" long after it started was cut off (server restart / redeploy).</summary>
    private static readonly TimeSpan InterruptedAfter = TimeSpan.FromMinutes(20);

    private async Task FailInterruptedRunsAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - InterruptedAfter;
        var stuck = await _context.DailyDigestRuns
            .Where(r => r.Status == Working && (r.StartedAtUtc ?? r.CreatedAtUtc) < cutoff)
            .ToListAsync(ct);
        foreach (var run in stuck)
        {
            run.Status = Failed;
            run.Message = "Bị gián đoạn giữa chừng (máy chủ khởi động lại). Hệ thống sẽ tự làm lại nếu còn trong khung giờ.";
            run.UpdatedAtUtc = DateTime.UtcNow;
        }
        if (stuck.Count > 0) await _context.SaveChangesAsync(ct);
    }

    public Task<DailyDigestRunDto> PreviewAsync(Guid facebookAccountId, CancellationToken cancellationToken = default) =>
        RunForPageAsync(facebookAccountId, publish: false, cancellationToken);

    public async Task<DailyDigestRunDto> PublishNowAsync(Guid facebookAccountId, CancellationToken cancellationToken = default)
    {
        await FailInterruptedRunsAsync(cancellationToken);
        var todayKey = DateKey(AutoPostService.VietnamDate(DateTime.UtcNow));
        if (await _context.DailyDigestRuns.AnyAsync(r => r.FacebookAccountId == facebookAccountId && r.DigestDate == todayKey && !r.IsPreview
                                                        && (r.Status == Published || r.Status == Partial || r.Status == Working), cancellationToken))
            throw new InvalidOperationException("Hôm nay Page này đã đăng (hoặc đang đăng) bản tin rồi. Mỗi ngày chỉ đăng một bản tin.");
        return await RunForPageAsync(facebookAccountId, publish: true, cancellationToken);
    }

    private async Task<DailyDigestRunDto> RunForPageAsync(Guid facebookAccountId, bool publish, CancellationToken ct)
    {
        var (page, settings) = (await LoadPagesAsync(ct)).FirstOrDefault(p => p.Page.Id == facebookAccountId);
        if (page == null) throw new KeyNotFoundException("Page này chưa được kết nối hoặc đã ngắt kết nối.");

        if (!await Lock.WaitAsync(TimeSpan.FromMinutes(5), ct))
            throw new InvalidOperationException("Đang làm bản tin khác, thử lại sau ít phút.");
        try
        {
            var run = await RunAsync(page, settings!, publish, ct);
            return ToDto(run, page.PageName);
        }
        finally
        {
            Lock.Release();
        }
    }

    private async Task<List<(FacebookAccount Page, AutoPostSettings Settings)>> LoadPagesAsync(CancellationToken ct)
    {
        var pages = await _context.FacebookAccounts.Where(a => a.IsActive).ToListAsync(ct);
        var ids = pages.Select(p => (Guid?)p.Id).ToList();
        var settings = await _context.AutoPostSettings.Where(s => ids.Contains(s.FacebookAccountId)).ToListAsync(ct);
        return pages
            .Select(p => (Page: p, Settings: settings.FirstOrDefault(s => s.FacebookAccountId == p.Id)))
            .Where(p => p.Settings != null)
            .Select(p => (p.Page, p.Settings!))
            .ToList();
    }

    // ---------------------------------------------------------------- one digest

    private async Task<DailyDigestRun> RunAsync(FacebookAccount page, AutoPostSettings settings, bool publish, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = AutoPostService.VietnamDate(now);
        var run = new DailyDigestRun
        {
            FacebookAccountId = page.Id,
            DigestDate = DateKey(today),
            IsPreview = !publish,
            Status = Working,
            StartedAtUtc = now,
            Message = "Đang chọn tin nóng trong ngày."
        };
        _context.DailyDigestRuns.Add(run);
        await _context.SaveChangesAsync(ct);

        var folder = Path.Combine(TempStorage.GetRoot(_configuration), "digests", run.Id.ToString("N"));
        Directory.CreateDirectory(folder);
        // Never empty: the storage cleanup removes empty folders, and this one stays empty while AI picks stories
        await File.WriteAllTextAsync(Path.Combine(folder, "run.txt"), $"{page.PageName} {run.DigestDate}", ct);
        run.WorkFolder = folder;

        try
        {
            var count = Math.Clamp(settings.DigestItems, MinItems, MaxItems);
            var items = await SelectStoriesAsync(settings, count, folder, ct);
            run.ItemsJson = JsonSerializer.Serialize(items.Select(i => i.Item).ToList());

            run.Message = "Đang vẽ ảnh bản tin.";
            await _context.SaveChangesAsync(ct);
            var theme = NewsTopics.Find(settings.Topic)?.Key ?? NewsTopics.General;
            var (postImages, storyImages) = await RenderCardsAsync(page.PageName, theme, today, items, folder, ct);
            run.Caption = BuildCaption(theme, today, items.Select(i => i.Item).ToList());

            if (!publish)
            {
                run.Status = Preview;
                run.Message = $"Bản xem trước {items.Count} tin, chưa đăng lên Facebook.";
                await _context.SaveChangesAsync(ct);
                return run;
            }

            // Story: slideshow of the 9:16 pictures (cover 4 s, each story 6 s)
            run.Message = "Đang đăng lên Facebook.";
            await _context.SaveChangesAsync(ct);
            var seconds = storyImages.Select((_, i) => i == 0 ? CoverSeconds : ItemSeconds).ToList();

            try
            {
                run.PostId = await _facebook.PublishPhotoPostAsync(page.Id, postImages, run.Caption, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Digest wall post for page {Page} failed", page.PageName);
                run.PostError = Shorten(ex.Message, 1000);
            }

            try
            {
                var video = await _renderer.RenderSlideshowAsync(storyImages, seconds, Path.Combine(folder, "story.mp4"), ct);
                run.StoryPostId = await _facebook.PublishVideoStoryAsync(page.Id, video, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Digest Story for page {Page} failed", page.PageName);
                run.StoryError = Shorten(ex.Message, 1000);
            }

            run.PublishedAtUtc = run.PostId != null || run.StoryPostId != null ? DateTime.UtcNow : null;
            run.Status = run.PostId != null && run.StoryPostId != null ? Published
                : run.PostId != null || run.StoryPostId != null ? Partial
                : Failed;
            run.Message = run.Status switch
            {
                Published => $"Đã đăng bài viết {items.Count} tin và Story.",
                Partial when run.PostId != null => $"Đã đăng bài viết {items.Count} tin. Story lỗi: {Shorten(run.StoryError, 300)}",
                Partial => $"Đã đăng Story. Bài viết lỗi: {Shorten(run.PostError, 300)}",
                _ => $"Không đăng được. {Shorten(run.PostError, 300)}"
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Daily digest for page {Page} failed", page.PageName);
            run.Status = Failed;
            run.Message = Shorten(ex.Message, 1000);
        }

        run.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(CancellationToken.None);
        return run;
    }

    // ---------------------------------------------------------------- choosing the stories

    private sealed record Candidate(TrendingArticleDto Article, string Text, List<string> ImageUrls);

    private async Task<List<(DailyDigestItemDto Item, string Photo)>> SelectStoriesAsync(
        AutoPostSettings settings, int count, string folder, CancellationToken ct)
    {
        var keywords = NewsTopics.ParseKeywords(settings.TopicKeywords);
        if (settings.Topic == NewsTopics.Custom && keywords.Count == 0)
            throw new InvalidOperationException("Page dùng chủ đề \"Tự chọn từ khoá\" nhưng chưa có từ khoá nào.");

        // 1. Today's hottest stories of the topic, Vietnam + international (one per story cluster)
        var overview = await _trending.GetTrendingAsync(new TrendingQuery
        {
            Region = "all",
            Hours = 24,
            Limit = 150,
            Topic = settings.Topic,
            Keywords = keywords
        }, ct);
        var pool = overview.Items.OrderByDescending(i => i.HotScore).Take(CandidatePool).ToList();
        if (pool.Count < MinItems)
            throw new InvalidOperationException(
                $"24 giờ qua mới có {pool.Count} tin thuộc chủ đề \"{NewsTopics.NameOf(settings.Topic)}\", chưa đủ để làm bản tin (cần ít nhất {MinItems}).");

        // 2. AI picks the most striking, distinct events (a few extra in case some have no usable picture)
        var topicName = NewsTopics.NameOf(settings.Topic);
        var picksJson = await _gemini.GenerateJsonAsync(PickSystemPrompt, BuildPickPrompt(topicName, keywords, pool, count + 4), ct);
        var picks = ParsePicks(picksJson, pool.Count);
        if (picks.Count == 0) throw new InvalidOperationException("AI không chọn được tin nào. Thử lại sau.");

        // 3. Read each picked article (text for the summary, its pictures) and download one picture
        var chosen = new List<(Candidate Candidate, string Photo)>();
        foreach (var index in picks)
        {
            if (chosen.Count >= count) break;
            var article = pool[index];
            var candidate = await ReadArticleAsync(article, ct);
            var photo = await DownloadPhotoAsync(candidate.ImageUrls, article.Url, Path.Combine(folder, $"photo_{chosen.Count + 1:00}"), ct);
            if (photo == null)
            {
                _logger.LogInformation("Digest: no usable picture for \"{Title}\", skipped", article.Title);
                continue;
            }
            chosen.Add((candidate, photo));
        }
        if (chosen.Count < MinItems)
            throw new InvalidOperationException($"Chỉ có {chosen.Count} tin nóng có ảnh dùng được, chưa đủ để làm bản tin (cần ít nhất {MinItems}).");

        // 4. AI writes a short headline + summary for each, from the article text only
        var writtenJson = await _gemini.GenerateJsonAsync(WriteSystemPrompt, BuildWritePrompt(topicName, chosen.Select(c => c.Candidate).ToList()), ct);
        var written = ParseWritten(writtenJson);

        var result = new List<(DailyDigestItemDto, string)>();
        for (var i = 0; i < chosen.Count; i++)
        {
            var (c, photo) = chosen[i];
            if (!written.TryGetValue(i + 1, out var w) || string.IsNullOrWhiteSpace(w.Headline) || string.IsNullOrWhiteSpace(w.Summary))
            {
                _logger.LogWarning("Digest: AI wrote nothing for \"{Title}\", skipped", c.Article.Title);
                continue;
            }
            result.Add((new DailyDigestItemDto
            {
                Title = c.Article.Title,
                Headline = Shorten(w.Headline, 120),
                Summary = Shorten(w.Summary, 300),
                SourceName = c.Article.SourceName,
                Url = c.Article.Url,
                ImageUrl = c.ImageUrls.FirstOrDefault(),
                CoverageCount = c.Article.CoverageCount,
                HotScore = c.Article.HotScore
            }, photo));
        }
        if (result.Count < MinItems)
            throw new InvalidOperationException("AI không viết được tóm tắt cho đủ tin. Thử lại sau.");
        return result;
    }

    private async Task<Candidate> ReadArticleAsync(TrendingArticleDto article, CancellationToken ct)
    {
        var images = new List<string>();
        var text = article.Summary ?? string.Empty;
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
            // The feed's own summary is still a real description of the story
            _logger.LogInformation("Digest: could not read {Url} ({Message}); using the feed summary", article.Url, ex.Message);
        }
        // The article's large picture first; the feed thumbnail as a fallback
        if (!string.IsNullOrWhiteSpace(article.ImageUrl)) images.Add(article.ImageUrl);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length > 2500) text = text[..2500];
        return new Candidate(article, text, images.Where(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase)).Distinct().Take(4).ToList());
    }

    /// <summary>Downloads the first real, large-enough picture (small feed thumbnails look blurry on a 1080 px card).</summary>
    private async Task<string?> DownloadPhotoAsync(List<string> urls, string referer, string pathWithoutExtension, CancellationToken ct)
    {
        (byte[] Bytes, string Ext)? best = null;
        foreach (var url in urls)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, System.Net.WebUtility.HtmlDecode(url));
                request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
                request.Headers.TryAddWithoutValidation("Referer", referer);
                using var response = await _http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) continue;
                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                var ext = SceneImageService.DetectImageType(bytes);
                if (ext == null) continue;
                if (best == null || bytes.Length > best.Value.Bytes.Length) best = (bytes, ext);
                if (bytes.Length >= 40_000) break; // good enough
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogDebug("Digest: picture {Url} failed: {Message}", url, ex.Message);
            }
        }
        if (best == null || best.Value.Bytes.Length < 8_000) return null;
        var path = $"{pathWithoutExtension}.{best.Value.Ext}";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, best.Value.Bytes, ct);
        return path;
    }

    // ---------------------------------------------------------------- AI prompts

    private const string PickSystemPrompt =
        "Bạn là biên tập viên trang tin trên Facebook. Nhiệm vụ: từ danh sách tin trong 24 giờ qua, chọn ra những tin " +
        "\"nóng\", giật gân nhất trong ngày để làm bản tin tổng hợp buổi tối. Ưu tiên tin gây sốc, bất ngờ, gây tranh cãi, " +
        "ảnh hưởng lớn tới nhiều người, hoặc được nhiều báo cùng đưa / nhiều người tìm kiếm. Mỗi tin phải là một sự kiện khác nhau " +
        "(không chọn hai tin về cùng một việc). Bỏ qua quảng cáo, PR doanh nghiệp, tử vi, mẹo vặt, tin không thuộc chủ đề. " +
        "Chỉ trả về JSON.";

    private const string WriteSystemPrompt =
        "Bạn viết bản tin tóm tắt ngắn bằng tiếng Việt cho trang tin trên Facebook. Với mỗi tin: một tiêu đề ngắn, hấp dẫn " +
        "nhưng đúng sự thật (tối đa 12 chữ, không giật tít sai sự thật), và một tóm tắt 1–2 câu (tối đa 35 chữ) để người đọc " +
        "biết ngay chuyện gì đã xảy ra: ai, việc gì, con số chính. CHỈ dùng thông tin có trong nội dung được cung cấp, không thêm, " +
        "không đoán. Tin tiếng Anh thì viết lại bằng tiếng Việt. Không dùng emoji, không hashtag. Chỉ trả về JSON.";

    private static string BuildPickPrompt(string topicName, List<string> keywords, List<TrendingArticleDto> pool, int want)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Chủ đề của Page: {topicName}" + (keywords.Count > 0 ? $" (từ khoá: {string.Join(", ", keywords)})" : ""));
        sb.AppendLine($"Hãy chọn {want} tin, xếp từ nóng nhất xuống.");
        sb.AppendLine("Danh sách tin (số thứ tự | tiêu đề | nguồn | số báo cùng đưa | lượt tìm Google | điểm hot | tóm tắt):");
        for (var i = 0; i < pool.Count; i++)
        {
            var a = pool[i];
            var summary = Regex.Replace(a.Summary ?? "", @"\s+", " ").Trim();
            if (summary.Length > 220) summary = summary[..220] + "…";
            sb.AppendLine($"[{i + 1}] {a.Title} | {a.SourceName} ({(a.Region == "VN" ? "VN" : "quốc tế")}) | {a.CoverageCount} báo | " +
                          $"{(a.TrendTraffic is int t ? $"~{t:N0}" : "-")} | {a.HotScore:0} | {summary}");
        }
        sb.AppendLine();
        sb.AppendLine("Trả về đúng định dạng: {\"picks\": [số thứ tự, ...]}");
        return sb.ToString();
    }

    private static string BuildWritePrompt(string topicName, List<Candidate> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Chủ đề: {topicName}. Viết tiêu đề + tóm tắt cho từng tin sau.");
        for (var i = 0; i < items.Count; i++)
        {
            sb.AppendLine();
            sb.AppendLine($"### Tin {i + 1}");
            sb.AppendLine($"Tiêu đề gốc: {items[i].Article.Title}");
            sb.AppendLine($"Nguồn: {items[i].Article.SourceName}");
            sb.AppendLine($"Nội dung: {items[i].Text}");
        }
        sb.AppendLine();
        sb.AppendLine("Trả về đúng định dạng: {\"items\": [{\"id\": 1, \"headline\": \"...\", \"summary\": \"...\"}, ...]}");
        return sb.ToString();
    }

    /// <summary>0-based indexes of the picked candidates, in the AI's order, without repeats or out-of-range numbers.</summary>
    public static List<int> ParsePicks(string json, int poolSize)
    {
        var result = new List<int>();
        using var doc = JsonDocument.Parse(StripFences(json));
        var root = doc.RootElement;
        var array = root.ValueKind == JsonValueKind.Array ? root
            : root.TryGetProperty("picks", out var p) && p.ValueKind == JsonValueKind.Array ? p
            : default;
        if (array.ValueKind != JsonValueKind.Array) return result;
        foreach (var el in array.EnumerateArray())
        {
            int n;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out n)) { }
            else if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString()?.Trim('[', ']', ' '), out n)) { }
            else continue;
            var index = n - 1;
            if (index >= 0 && index < poolSize && !result.Contains(index)) result.Add(index);
        }
        return result;
    }

    /// <summary>1-based story number → (headline, summary).</summary>
    public static Dictionary<int, (string Headline, string Summary)> ParseWritten(string json)
    {
        var result = new Dictionary<int, (string, string)>();
        using var doc = JsonDocument.Parse(StripFences(json));
        var root = doc.RootElement;
        var array = root.ValueKind == JsonValueKind.Array ? root
            : root.TryGetProperty("items", out var p) && p.ValueKind == JsonValueKind.Array ? p
            : default;
        if (array.ValueKind != JsonValueKind.Array) return result;
        var position = 0;
        foreach (var el in array.EnumerateArray())
        {
            position++;
            if (el.ValueKind != JsonValueKind.Object) continue;
            var id = el.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var n) ? n : position;
            var headline = el.TryGetProperty("headline", out var h) ? Clean(h.GetString()) : "";
            var summary = el.TryGetProperty("summary", out var s) ? Clean(s.GetString()) : "";
            result[id] = (headline, summary);
        }
        return result;

        static string Clean(string? text) => Regex.Replace(text ?? "", @"\s+", " ").Trim().Trim('"');
    }

    private static string StripFences(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("```"))
        {
            t = Regex.Replace(t, @"^```[a-zA-Z]*\s*", "");
            t = Regex.Replace(t, @"\s*```$", "");
        }
        return t;
    }

    // ---------------------------------------------------------------- pictures + text

    private async Task<(List<string> Post, List<string> Story)> RenderCardsAsync(
        string pageName, string theme, DateTime vietnamDate, List<(DailyDigestItemDto Item, string Photo)> items, string folder, CancellationToken ct)
    {
        var python = FFmpegRendererService.FindPythonExecutable()
                     ?? throw new InvalidOperationException("Không tìm thấy Python trên máy chủ để vẽ ảnh bản tin.");
        var script = FFmpegRendererService.FindScriptPath("generate_digest_cards.py")
                     ?? throw new InvalidOperationException("Thiếu script vẽ ảnh bản tin (generate_digest_cards.py).");

        var spec = new
        {
            out_dir = folder,
            brand = pageName,
            theme,
            topic_label = theme is NewsTopics.General or NewsTopics.Custom ? "" : NewsTopics.NameOf(theme),
            date = vietnamDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            title = "Điểm tin nóng",
            items = items.Select(i => new { photo = i.Photo, headline = i.Item.Headline, summary = i.Item.Summary, source = i.Item.SourceName })
        };
        Directory.CreateDirectory(folder);
        var specPath = Path.Combine(folder, "spec.json");
        await File.WriteAllTextAsync(specPath, JsonSerializer.Serialize(spec), new UTF8Encoding(false), ct);

        var psi = new ProcessStartInfo
        {
            FileName = python,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add(specPath);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Không chạy được Python.");
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
        {
            var err = await stderr;
            throw new InvalidOperationException($"Vẽ ảnh bản tin thất bại: {Shorten(err, 400)}");
        }

        var post = Directory.GetFiles(folder, "post_*.jpg").OrderBy(f => f, StringComparer.Ordinal).ToList();
        var story = Directory.GetFiles(folder, "story_*.jpg").OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (post.Count != items.Count + 1 || story.Count != items.Count + 1)
            throw new InvalidOperationException("Vẽ ảnh bản tin không đủ ảnh. " + Shorten(await stdout, 200));
        return (post, story);
    }

    private static readonly string[] NumberEmoji = { "1️⃣", "2️⃣", "3️⃣", "4️⃣", "5️⃣", "6️⃣", "7️⃣", "8️⃣", "9️⃣", "🔟" };

    /// <summary>Wall post text: title line, then every story (headline, summary, source).</summary>
    public static string BuildCaption(string topic, DateTime vietnamDate, IReadOnlyList<DailyDigestItemDto> items)
    {
        var sb = new StringBuilder();
        var topicPart = topic is NewsTopics.General or NewsTopics.Custom ? "" : $" {NewsTopics.NameOf(topic).ToUpper(new CultureInfo("vi-VN"))}";
        sb.Append($"🔥 ĐIỂM TIN NÓNG{topicPart} NGÀY {vietnamDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}\n");
        sb.Append($"{items.Count} tin đáng chú ý nhất hôm nay, xem nhanh trong một phút.\n");
        for (var i = 0; i < items.Count; i++)
        {
            sb.Append('\n');
            sb.Append($"{NumberEmoji[Math.Min(i, NumberEmoji.Length - 1)]} {items[i].Headline}\n");
            sb.Append($"{items[i].Summary}\n");
            sb.Append($"(Nguồn: {items[i].SourceName})\n");
        }
        sb.Append("\n#DiemTinNong #TinTuc");
        return sb.ToString();
    }

    // ---------------------------------------------------------------- reading

    public async Task<List<DailyDigestRunDto>> GetRecentAsync(Guid? facebookAccountId, CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddDays(-7);
        var query = _context.DailyDigestRuns.AsNoTracking().Where(r => r.CreatedAtUtc >= since);
        if (facebookAccountId != null) query = query.Where(r => r.FacebookAccountId == facebookAccountId);
        var runs = await query.OrderByDescending(r => r.CreatedAtUtc).Take(50).ToListAsync(cancellationToken);
        var names = await _context.FacebookAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.PageName, cancellationToken);
        return runs.Select(r => ToDto(r, r.FacebookAccountId is Guid id && names.TryGetValue(id, out var n) ? n : null)).ToList();
    }

    public async Task<string?> GetImagePathAsync(Guid runId, string fileName, CancellationToken cancellationToken = default)
    {
        if (!ImageFileName.IsMatch(fileName)) return null;
        var folder = await _context.DailyDigestRuns.AsNoTracking().Where(r => r.Id == runId).Select(r => r.WorkFolder).FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(folder)) return null;
        var path = Path.Combine(folder, fileName);
        return File.Exists(path) ? path : null;
    }

    private static DailyDigestRunDto ToDto(DailyDigestRun r, string? pageName)
    {
        List<string> Images(string prefix) =>
            !string.IsNullOrWhiteSpace(r.WorkFolder) && Directory.Exists(r.WorkFolder)
                ? Directory.GetFiles(r.WorkFolder, $"{prefix}_*.jpg").Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal)
                    .Select(f => $"/api/daily-digest/runs/{r.Id}/images/{f}").ToList()
                : new List<string>();

        List<DailyDigestItemDto> items;
        try
        {
            items = string.IsNullOrWhiteSpace(r.ItemsJson) ? new() : JsonSerializer.Deserialize<List<DailyDigestItemDto>>(r.ItemsJson) ?? new();
        }
        catch (JsonException)
        {
            items = new();
        }

        return new DailyDigestRunDto
        {
            Id = r.Id,
            FacebookAccountId = r.FacebookAccountId,
            PageName = pageName,
            DigestDate = r.DigestDate,
            IsPreview = r.IsPreview,
            Status = r.Status,
            Message = r.Message,
            Caption = r.Caption,
            Items = items,
            PostImages = Images("post"),
            StoryImages = Images("story"),
            PostId = r.PostId,
            PostUrl = string.IsNullOrWhiteSpace(r.PostId) ? null : $"https://www.facebook.com/{r.PostId}",
            PostError = r.PostError,
            StoryPostId = r.StoryPostId,
            StoryError = r.StoryError,
            CreatedAtUtc = r.CreatedAtUtc,
            PublishedAtUtc = r.PublishedAtUtc
        };
    }

    // ---------------------------------------------------------------- helpers

    private void DeleteOldFolders()
    {
        try
        {
            var root = Path.Combine(TempStorage.GetRoot(_configuration), "digests");
            if (!Directory.Exists(root)) return;
            foreach (var dir in Directory.GetDirectories(root))
            {
                // Last-write time: creation time isn't reliable on every Linux filesystem
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir) > KeepFilesFor)
                    Directory.Delete(dir, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Digest folder cleanup: {Message}", ex.Message);
        }
    }

    private static string DateKey(DateTime vietnamDate) => vietnamDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Shorten(string? text, int max)
    {
        text ??= string.Empty;
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}

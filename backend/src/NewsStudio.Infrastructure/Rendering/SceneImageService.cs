using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;
using NewsStudio.Domain.Enums;
using NewsStudio.Infrastructure.Common;

namespace NewsStudio.Infrastructure.Rendering;

/// <summary>
/// Makes sure every scene has a real picture before the video is rendered.
/// Rule: a video is never rendered on a blank/black background.
///  1. Download the article's own images (same headers the article page was fetched with,
///     Referer = article URL) and check they really are pictures of a usable size.
///  2. If fewer than <c>Images:MinPerVideo</c> usable images remain, search Openverse for
///     related, commercially-usable Creative Commons photos and store them as
///     "WebSearch" images (shown as "Ảnh minh hoạ" with attribution).
///  3. Scenes whose image failed are moved to a working image.
///  4. Still no usable image → fail with a clear error instead of rendering a black video.
/// </summary>
public class SceneImageService
{
    private const string WebSearchStatus = ImageStatus.WebSearch;
    private const string UnavailableStatus = ImageStatus.Unavailable;

    private const string ArticleUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 AI-News-Studio/1.0";
    private const string OpenverseUserAgent = "TramTin247-ImageSearch/1.0";
    private const int MaxImageBytes = 15 * 1024 * 1024;
    private const int MinImageBytes = 6 * 1024;
    private const int MinImageWidth = 360;

    private readonly HttpClient _http;
    private readonly CommonsImageFinder _commons;
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SceneImageService> _logger;

    public SceneImageService(
        HttpClient http,
        CommonsImageFinder commons,
        IApplicationDbContext context,
        IConfiguration configuration,
        ILogger<SceneImageService> logger)
    {
        _http = http;
        _commons = commons;
        _http.Timeout = TimeSpan.FromSeconds(25);
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task EnsureSceneImagesAsync(VideoProject project, CancellationToken cancellationToken)
    {
        // Test runs use made-up image URLs; no downloads or web searches there.
        if (SimulationMode.IsEnabled(_configuration) || project.ArticleId == null) return;

        var article = await _context.Articles
            .Include(a => a.Images)
            .FirstOrDefaultAsync(a => a.Id == project.ArticleId, cancellationToken);
        if (article == null) return;

        var dir = Path.Combine(TempStorage.GetRoot(_configuration), project.Id.ToString(), "images");
        Directory.CreateDirectory(dir);
        var referer = !string.IsNullOrWhiteSpace(article.CanonicalUrl) ? article.CanonicalUrl : article.Url;

        // 1. The article's own images
        var usable = new List<ArticleImage>();
        var candidates = article.Images
            // "Unavailable" ones are retried: a temporary error shouldn't remove a real article photo for good
            .Where(i => !i.OriginalImageUrl.StartsWith("chart://", StringComparison.Ordinal)
                        && (i.Status == UnavailableStatus || ImageStatus.IsSelectedForVideo(i.Status))
                        && i.RightsStatus != RightsStatus.Rejected && i.Category != ImageCategory.Unsuitable)
            .OrderBy(i => i.Status == WebSearchStatus ? 1 : 0)
            .ThenBy(i => i.ArticleImageOrder)
            .ToList();

        var sheet = ReadFactSheet(project.Script?.FactSheetJson);
        var entities = sheet?.Entities ?? new List<FactEntityDto>();
        foreach (var img in candidates)
        {
            if (await TryDownloadAsync(img, dir, img.Status == WebSearchStatus ? null : referer, cancellationToken))
            {
                if (img.Status != WebSearchStatus) TagArticleImage(img, article, entities);
                usable.Add(img);
            }
        }

        // 2. Related pictures from the web when the article's images are missing/blocked
        var minImages = Math.Max(1, _configuration.GetValue<int>("Images:MinPerVideo", 2));
        var nextOrder = article.Images.Count == 0 ? 1 : article.Images.Max(i => i.ArticleImageOrder) + 1;
        var known = article.Images.Select(i => i.OriginalImageUrl).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 2a. The subject's own picture on Wikimedia Commons (identity from Wikidata, licence from Commons)
        if (usable.Count < minImages)
        {
            foreach (var entity in entities.Where(e => e.Category is "A" or "B" or "C").Take(4))
            {
                if (usable.Count >= Math.Max(minImages, 3)) break;
                if (usable.Any(i => string.Equals(i.SubjectName, entity.Name, StringComparison.OrdinalIgnoreCase))) continue;
                var found = await _commons.FindAsync(entity, cancellationToken);
                if (found == null || !known.Add(found.Url)) continue;
                var commonsImage = new ArticleImage
                {
                    ArticleId = article.Id,
                    OriginalImageUrl = found.Url,
                    Caption = found.SubjectName,
                    AltText = $"{found.Author ?? "Không rõ tác giả"} / Wikimedia Commons, {found.License}",
                    ArticleImageOrder = nextOrder,
                    RightsStatus = RightsStatus.Licensed,
                    Category = ImageCategory.Panorama,
                    Status = WebSearchStatus,
                    ImageType = ImageKinds.Licensed,
                    SourceName = "Wikimedia Commons",
                    SourcePageUrl = found.FilePageUrl,
                    LicenseType = found.License,
                    LicenseEvidence = Shorten($"{found.FilePageUrl} · Tác giả: {found.Author ?? "không rõ"}{(found.LicenseUrl != null ? $" · {found.LicenseUrl}" : "")} · Wikidata {found.WikidataId}", 1900),
                    SubjectName = entity.Name,
                    SubjectType = entity.Category,
                    // Identity comes from the Wikidata item (label/alias match; people: an item about a human)
                    VerificationStatus = "VERIFIED",
                    Confidence = found.Confidence
                };
                if (!await TryDownloadAsync(commonsImage, dir, null, cancellationToken)) continue;
                nextOrder++;
                _context.ArticleImages.Add(commonsImage);
                usable.Add(commonsImage);
            }
        }

        // 2b. Generic illustration photos (Openverse, CC with commercial use) — only for stories without a
        //     named person or a specific event: those must never show someone else / another incident.
        var personOrEvent = !AllowGenericIllustrations(entities);
        if (usable.Count < minImages && !personOrEvent)
        {
            var wanted = Math.Max(minImages, 4) - usable.Count;
            _logger.LogWarning("Project {ProjectId}: only {Count} usable image(s); searching illustration photos on Openverse.",
                project.Id, usable.Count);

            var queries = (sheet?.ImageQueries ?? new List<string>())
                .Concat(entities.Where(e => e.Category is "B" or "C").Select(e => e.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var found in await SearchRelatedImagesAsync(queries, wanted * 3, cancellationToken))
            {
                if (usable.Count(i => i.Status == WebSearchStatus) >= wanted) break;
                if (!known.Add(found.Url)) continue;

                var webImage = new ArticleImage
                {
                    ArticleId = article.Id,
                    OriginalImageUrl = found.Url,
                    Caption = $"Ảnh minh hoạ: {found.Title}".Trim().TrimEnd(':'),
                    AltText = found.Attribution,
                    ArticleImageOrder = nextOrder,
                    Width = found.Width,
                    Height = found.Height,
                    RightsStatus = RightsStatus.Licensed,
                    Category = ImageCategory.Panorama,
                    Status = WebSearchStatus,
                    ImageType = ImageKinds.Licensed,
                    SourceName = "Openverse",
                    SourcePageUrl = found.LandingUrl,
                    LicenseType = found.License,
                    LicenseEvidence = Shorten(found.Attribution, 1900),
                    VerificationStatus = "UNVERIFIED"
                };

                var ok = await TryDownloadAsync(webImage, dir, null, cancellationToken);
                if (!ok && !string.IsNullOrWhiteSpace(found.ThumbnailUrl))
                {
                    // Some originals are huge or hot-link protected; Openverse's own thumbnail always works
                    webImage.OriginalImageUrl = found.ThumbnailUrl;
                    ok = await TryDownloadAsync(webImage, dir, null, cancellationToken);
                    webImage.OriginalImageUrl = found.Url;
                }
                if (!ok) continue;

                webImage.ErrorMessage = null;
                webImage.Status = WebSearchStatus;
                nextOrder++;
                _context.ArticleImages.Add(webImage);
                usable.Add(webImage);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        if (usable.Count == 0)
        {
            throw new InvalidOperationException(
                personOrEvent
                    ? "Không có ảnh để dựng video: ảnh trong bài báo không tải được và không tìm thấy ảnh có giấy phép của đúng nhân vật/sự kiện. " +
                      "Không dùng ảnh người khác hay vụ việc khác để thay — chọn bài khác."
                    : "Không có ảnh để dựng video: ảnh trong bài báo không tải được (bị chặn hoặc lỗi) và không tìm được ảnh minh hoạ liên quan trên mạng. " +
                      "Video không được dựng khi thiếu ảnh — thử lại sau hoặc chọn bài khác.");
        }

        // 2c. A chart drawn only from the article's own numbers (checked), shown in the scene that talks about them
        var chartImage = await RenderChartAsync(project, article, sheet, dir, nextOrder, cancellationToken);

        // 3. Every scene gets a working picture; spread them so the same photo doesn't repeat back to back
        var usableIds = usable.Select(i => i.Id).ToHashSet();
        var useCount = usable.ToDictionary(i => i.Id, _ => 0);
        var scenes = project.Scenes.OrderBy(s => s.SceneNumber).ToList();
        foreach (var s in scenes)
        {
            if (s.SelectedArticleImageId is Guid id && usableIds.Contains(id)) useCount[id]++;
        }

        Guid? previous = null;
        foreach (var scene in scenes)
        {
            if (scene.SelectedArticleImageId is Guid current && usableIds.Contains(current))
            {
                previous = current;
                continue;
            }

            var pick = usable
                .OrderBy(i => i.Id == previous ? 1 : 0)
                .ThenBy(i => useCount[i.Id])
                .ThenBy(i => i.Status == WebSearchStatus ? 1 : 0)
                .ThenBy(i => i.ArticleImageOrder)
                .First();

            scene.SelectedArticleImageId = pick.Id;
            scene.SelectedArticleImage = pick;
            scene.ImageCaption = pick.Caption;
            useCount[pick.Id]++;
            previous = pick.Id;
        }

        if (chartImage != null && scenes.Count >= 3)
        {
            // The scene (not the first) that says the most numbers
            var target = scenes.Skip(1).OrderByDescending(s => System.Text.RegularExpressions.Regex.Matches(s.VoiceText ?? "", @"\d").Count).First();
            target.SelectedArticleImageId = chartImage.Id;
            target.SelectedArticleImage = chartImage;
            target.ImageCaption = chartImage.Caption;
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Project {ProjectId}: {Usable} usable image(s) ({Web} from web search) for {Scenes} scenes.",
            project.Id, usable.Count, usable.Count(i => i.Status == WebSearchStatus), scenes.Count);
    }

    // ── Download + validation ────────────────────────────────────────────────

    private async Task<bool> TryDownloadAsync(ArticleImage image, string dir, string? referer, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(image.LocalTempPath) && File.Exists(image.LocalTempPath)) return true;

        var url = WebUtility.HtmlDecode(image.OriginalImageUrl ?? string.Empty).Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            MarkUnavailable(image, "Đường dẫn ảnh không hợp lệ");
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", referer == null ? OpenverseUserAgent : ArticleUserAgent);
            // No AVIF: FFmpeg builds on the server may not decode it
            request.Headers.TryAddWithoutValidation("Accept", "image/jpeg,image/png,image/webp;q=0.9,image/*;q=0.5");
            if (referer != null && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri)) request.Headers.Referrer = refererUri;

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                MarkUnavailable(image, $"HTTP {(int)response.StatusCode} khi tải ảnh");
                return false;
            }
            if (response.Content.Headers.ContentLength > MaxImageBytes)
            {
                MarkUnavailable(image, "Ảnh quá lớn");
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxImageBytes)
                {
                    MarkUnavailable(image, "Ảnh quá lớn");
                    return false;
                }
            }

            var bytes = buffer.ToArray();
            var ext = DetectImageType(bytes);
            if (ext == null)
            {
                MarkUnavailable(image, "Nội dung tải về không phải ảnh JPEG/PNG/WebP");
                return false;
            }
            if (bytes.Length < MinImageBytes)
            {
                MarkUnavailable(image, "Ảnh quá nhỏ");
                return false;
            }

            var (w, h) = ReadDimensions(bytes, ext);
            if (w > 0 && w < MinImageWidth)
            {
                MarkUnavailable(image, $"Ảnh quá nhỏ ({w}px)");
                return false;
            }

            var name = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant();
            var path = Path.Combine(dir, $"{name}.{ext}");
            await File.WriteAllBytesAsync(path, bytes, ct);

            image.LocalTempPath = path;
            if (w > 0) { image.Width = w; image.Height = h; }
            image.ImageHash = Convert.ToHexString(SHA256.HashData(bytes))[..32];
            if (image.Status == UnavailableStatus) image.Status = ImageStatus.Extracted;
            image.ErrorMessage = null;
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Image download failed for {Url}: {Message}", url, ex.Message);
            MarkUnavailable(image, ex is TaskCanceledException ? "Hết thời gian tải ảnh" : ex.Message);
            return false;
        }
    }

    private static void MarkUnavailable(ArticleImage image, string reason)
    {
        if (image.Status != WebSearchStatus) image.Status = UnavailableStatus;
        image.ErrorMessage = reason.Length > 500 ? reason[..500] : reason;
    }

    public static string? DetectImageType(byte[] b)
    {
        if (b.Length < 12) return null;
        if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "png";
        if (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "webp";
        return null;
    }

    internal static (int Width, int Height) ReadDimensions(byte[] b, string type)
    {
        try
        {
            switch (type)
            {
                case "png":
                    return (BigEndian(b, 16), BigEndian(b, 20));
                case "jpg":
                    var i = 2;
                    while (i + 9 < b.Length)
                    {
                        if (b[i] != 0xFF) { i++; continue; }
                        var marker = b[i + 1];
                        if (marker is >= 0xC0 and <= 0xCF && marker is not 0xC4 and not 0xC8 and not 0xCC)
                            return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                        i += 2 + ((b[i + 2] << 8) | b[i + 3]);
                    }
                    break;
                case "webp":
                    var chunk = Encoding.ASCII.GetString(b, 12, 4);
                    if (chunk == "VP8 " && b.Length > 30) return (((b[27] << 8) | b[26]) & 0x3FFF, ((b[29] << 8) | b[28]) & 0x3FFF);
                    if (chunk == "VP8L" && b.Length > 25)
                        return (1 + (((b[22] & 0x3F) << 8) | b[21]), 1 + (((b[24] & 0x0F) << 10) | (b[23] << 2) | ((b[22] & 0xC0) >> 6)));
                    if (chunk == "VP8X" && b.Length > 30)
                        return (1 + (b[24] | (b[25] << 8) | (b[26] << 16)), 1 + (b[27] | (b[28] << 8) | (b[29] << 16)));
                    break;
            }
        }
        catch (IndexOutOfRangeException) { }
        return (0, 0);
    }

    private static int BigEndian(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];

    // ── Related-image search (Openverse: CC-licensed, commercial use allowed) ──

    private sealed record FoundImage(string Url, string? ThumbnailUrl, string Title, string Attribution, int? Width, int? Height, string License, string? LandingUrl);

    private async Task<List<FoundImage>> SearchRelatedImagesAsync(List<string> queries, int max, CancellationToken ct)
    {
        var results = new List<FoundImage>();

        foreach (var q in queries)
        {
            if (results.Count >= max) break;
            try
            {
                var url = "https://api.openverse.org/v1/images/?q=" + Uri.EscapeDataString(q) +
                          "&page_size=20&license_type=commercial&mature=false";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", OpenverseUserAgent);
                using var response = await _http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Openverse search '{Query}' returned HTTP {Status}", q, (int)response.StatusCode);
                    continue;
                }

                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (!doc.RootElement.TryGetProperty("results", out var items)) continue;

                foreach (var it in items.EnumerateArray())
                {
                    var imgUrl = Str(it, "url");
                    if (string.IsNullOrWhiteSpace(imgUrl)) continue;
                    var w = Int(it, "width");
                    var h = Int(it, "height");
                    // Landscape/portrait photos of reasonable size; skip tiny or panorama strips
                    if (w is > 0 and < 700) continue;
                    if (w > 0 && h > 0 && (w / (double)h > 2.6 || h / (double)w > 2.2)) continue;
                    var ft = Str(it, "filetype")?.ToLowerInvariant();
                    if (ft is "svg" or "gif" or "tiff" or "tif") continue;

                    var title = Str(it, "title") ?? q;

                    // Relevance: the photo's own title/tags must mention at least one query word
                    var described = title + " " + (it.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array
                        ? string.Join(' ', tags.EnumerateArray().Select(t => t.TryGetProperty("name", out var n) ? n.GetString() : null))
                        : string.Empty);
                    var queryWords = Regex.Matches(q, @"[\p{L}\p{N}]+").Select(m => m.Value)
                        .Where(w => w.Length > 2 && !StopWords.Contains(w)).ToList();
                    if (queryWords.Count > 0 && !queryWords.Any(w => described.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
                    if (results.Any(r => r.Url == imgUrl)) continue;
                    var attribution = Str(it, "attribution")
                        ?? $"\"{title}\" — {Str(it, "creator") ?? "không rõ tác giả"}, {Str(it, "license")?.ToUpperInvariant()} {Str(it, "license_version")}".Trim();
                    var landing = Str(it, "foreign_landing_url");
                    if (!string.IsNullOrWhiteSpace(landing) && !attribution.Contains(landing)) attribution += $" Nguồn: {landing}";

                    var license = $"CC {Str(it, "license")?.ToUpperInvariant()} {Str(it, "license_version")}".Trim();
                    results.Add(new FoundImage(imgUrl, Str(it, "thumbnail"), Shorten(title, 160), Shorten(attribution, 900), w, h, license, landing));
                    if (results.Count >= max) break;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Openverse search '{Query}' failed: {Message}", q, ex.Message);
            }
        }

        return results;
    }

    // ── Provenance, charts ───────────────────────────────────────────────────

    /// <summary>
    /// Generic illustration photos found by keyword are only allowed when the story has no named person (A) and
    /// no specific event (D): such a photo could show someone else, or another accident/disaster.
    /// </summary>
    public static bool AllowGenericIllustrations(IEnumerable<FactEntityDto> entities) =>
        !entities.Any(e => e.Category is "A" or "D");

    private static FactSheetDto? ReadFactSheet(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<FactSheetDto>(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// A photo published with the article: shown with the outlet's name. When its caption names one of the
    /// story's subjects, the publisher itself says who/what it shows (identity verified by the caption).
    /// </summary>
    public static void TagArticleImage(ArticleImage img, Article article, IReadOnlyList<FactEntityDto> entities)
    {
        img.ImageType ??= ImageKinds.Real;
        img.SourceName ??= string.IsNullOrWhiteSpace(article.Publisher) ? "Báo gốc" : article.Publisher;
        img.SourcePageUrl ??= string.IsNullOrWhiteSpace(article.CanonicalUrl) ? article.Url : article.CanonicalUrl;
        img.LicenseType ??= "Ảnh báo — dùng có ghi nguồn";
        img.LicenseEvidence ??= "Ảnh đăng kèm bài báo gốc; video ghi rõ nguồn ảnh.";
        if (img.SubjectName != null) return;
        var described = CommonsImageFinder.Fold($"{img.Caption} {img.AltText}");
        var named = entities
            .Where(e => e.Category != "E" && CommonsImageFinder.Fold(e.Name).Length > 2 && described.Contains(CommonsImageFinder.Fold(e.Name)))
            .ToList();
        var people = named.Where(e => e.Category == "A").ToList();
        if (people.Count > 1)
        {
            // Several people in the caption: we can't tell who is who, so no name tag on the video
            img.SubjectName = string.Join(", ", people.Select(p => p.Name));
            img.SubjectType = "A+";
            img.VerificationStatus = "VERIFIED";
            img.Confidence = 0.6;
            return;
        }
        var subject = people.FirstOrDefault() ?? named.FirstOrDefault();
        if (subject != null)
        {
            img.SubjectName = subject.Name;
            img.SubjectType = subject.Category;
            img.VerificationStatus = "VERIFIED";
            img.Confidence = 0.8;
        }
        else
        {
            img.VerificationStatus ??= "UNVERIFIED";
        }
    }

    private async Task<ArticleImage?> RenderChartAsync(VideoProject project, Article article, FactSheetDto? sheet, string dir, int order, CancellationToken ct)
    {
        if (sheet?.Chart == null || sheet.Chart.Points.Count < 3) return null;
        var python = FFmpegRendererService.FindPythonExecutable();
        var script = FFmpegRendererService.FindScriptPath("generate_chart.py");
        if (python == null || script == null) return null;

        var output = Path.Combine(dir, "chart.png");
        var specPath = Path.Combine(dir, "chart.json");
        var theme = NewsStudio.Infrastructure.Trending.NewsTopics.Find(project.Topic)?.Key ?? NewsStudio.Infrastructure.Trending.NewsTopics.General;
        // Numbers come from the article: name the outlet, and the original source when the article cites one
        var source = string.IsNullOrWhiteSpace(sheet.Chart.Source) || string.Equals(sheet.Chart.Source.Trim(), article.Publisher, StringComparison.OrdinalIgnoreCase)
            ? article.Publisher
            : $"{sheet.Chart.Source.Trim()}, theo {article.Publisher}";
        await File.WriteAllTextAsync(specPath, JsonSerializer.Serialize(new
        {
            output,
            title = sheet.Chart.Title,
            unit = sheet.Chart.Unit,
            source,
            theme,
            points = sheet.Chart.Points.Select(p => new { label = p.Label, value = p.Value })
        }), new UTF8Encoding(false), ct);

        var psi = new System.Diagnostics.ProcessStartInfo { FileName = python, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add(specPath);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) return null;
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0 || !File.Exists(output))
        {
            _logger.LogWarning("Chart for project {ProjectId} failed: {Error}", project.Id, await proc.StandardError.ReadToEndAsync(ct));
            return null;
        }

        // One chart picture per project, refreshed on every render
        var chartUrl = $"chart://{project.Id}";
        var existing = article.Images.FirstOrDefault(i => i.OriginalImageUrl == chartUrl);
        if (existing != null)
        {
            existing.LocalTempPath = output;
            existing.Caption = sheet.Chart.Title;
            existing.Status = WebSearchStatus;
            existing.ErrorMessage = null;
            return existing;
        }
        var chart = new ArticleImage
        {
            ArticleId = article.Id,
            OriginalImageUrl = chartUrl,
            LocalTempPath = output,
            Caption = sheet.Chart.Title,
            ArticleImageOrder = order,
            Width = 1080,
            Height = 1050,
            RightsStatus = RightsStatus.Authorized,
            Category = ImageCategory.Chart,
            Status = WebSearchStatus,
            ImageType = ImageKinds.Infographic,
            SourceName = $"Đồ hoạ tự thiết kế · số liệu {source}",
            SourcePageUrl = string.IsNullOrWhiteSpace(article.CanonicalUrl) ? article.Url : article.CanonicalUrl,
            LicenseType = "Tự thiết kế",
            LicenseEvidence = "Vẽ từ các con số có trong bài gốc (đã đối chiếu tự động).",
            SubjectType = "E",
            VerificationStatus = "VERIFIED",
            Confidence = 1
        };
        _context.ArticleImages.Add(chart);
        return chart;
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "from", "that", "this", "after", "over", "into", "about", "amid", "says", "said",
        "will", "has", "have", "are", "was", "were", "its", "his", "her", "their", "new", "more", "than", "how", "why",
        "what", "who", "not", "but", "can", "could", "would", "should", "may", "might", "out", "off", "all", "being"
    };

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";
}

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Infrastructure.Growth;

/// <summary>Kinds of publishing / reading failures, deciding whether a retry is safe.</summary>
public static class GrowthErrorKind
{
    public const string Transient = "Transient";   // network, Meta busy, rate limit → retry later
    public const string Token = "Token";           // token expired / revoked → reconnect the Page
    public const string Permission = "Permission"; // the app/Page lacks a permission → fix in Meta
    public const string Unsupported = "Unsupported"; // this Page/app can't use the feature → post by hand
    public const string Content = "Content";       // bad file / caption → edit the content
    public const string Unknown = "Unknown";
}

/// <summary>A failure with its kind (so the scheduler knows whether to retry) and a message for the owner.</summary>
public class GrowthPublishException : InvalidOperationException
{
    public string Kind { get; }
    public GrowthPublishException(string kind, string message) : base(message) => Kind = kind;
}

/// <summary>
/// The few Graph API calls the existing <see cref="IFacebookPublishingService"/> doesn't have (photo Stories, text posts,
/// Page Insights, numbers of photo posts). The Page and its token come from the existing connection (FacebookAccounts,
/// decrypted with the existing token protection); the Graph API version is the app's own (Facebook:GraphApiVersion).
/// Tokens are never logged: errors keep Meta's message, never the URL.
/// </summary>
public class GrowthGraphClient
{
    private readonly HttpClient _http;
    private readonly IApplicationDbContext _db;
    private readonly IFacebookTokenProtectionService _tokens;
    private readonly ILogger<GrowthGraphClient> _logger;

    public GrowthGraphClient(HttpClient http, IApplicationDbContext db, IFacebookTokenProtectionService tokens, IConfiguration configuration,
        ILogger<GrowthGraphClient> logger)
    {
        _http = http;
        _db = db;
        _tokens = tokens;
        _logger = logger;
        Version = configuration["Facebook:GraphApiVersion"] ?? "v21.0";
    }

    public string Version { get; }
    private string Base => $"https://graph.facebook.com/{Version}";

    /// <summary>Page metrics tried one by one (Meta retired several in 2025–2026; whichever answers is used).</summary>
    public static readonly string[] PageDayMetrics =
    {
        "page_daily_follows_unique", "page_daily_unfollows_unique", "page_views_total",
        "page_media_view", "page_total_media_view_unique", "page_post_engagements"
    };

    public async Task<(string PageId, string Token)> PageTokenAsync(Guid facebookAccountId, CancellationToken ct)
    {
        var account = await _db.FacebookAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == facebookAccountId, ct);
        if (account == null)
            throw new GrowthPublishException(GrowthErrorKind.Token, "Page không còn trong ứng dụng. Kết nối lại Page ở Cài đặt → Facebook.");
        if (!account.IsActive || string.IsNullOrWhiteSpace(account.EncryptedAccessToken))
            throw new GrowthPublishException(GrowthErrorKind.Token, "Page đã ngắt kết nối. Vào Cài đặt → Facebook để kết nối lại.");
        if (account.TokenExpiresAtUtc is { } exp && exp <= DateTime.UtcNow)
            throw new GrowthPublishException(GrowthErrorKind.Token, "Mã truy cập của Page đã hết hạn. Vào Cài đặt → Facebook để kết nối lại.");
        return (account.PageId, _tokens.DecryptToken(account.EncryptedAccessToken));
    }

    // ───────────────────────── error handling ─────────────────────────

    private static readonly System.Text.RegularExpressions.Regex TokenLike =
        new(@"\bEAA[A-Za-z0-9_\-]{6,}|access_token=[^&\s""']+", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Removes access tokens from a message before it is stored, logged or shown: Meta echoes a malformed token back in
    /// its error text ("Malformed access token EAAB…"), and URLs carry it as access_token=….
    /// </summary>
    public static string Redact(string? message, string? token = null)
    {
        var m = message ?? "";
        if (!string.IsNullOrEmpty(token) && token.Length >= 6) m = m.Replace(token, "***");
        return TokenLike.Replace(m, x => x.Value.StartsWith("access_token=", StringComparison.Ordinal) ? "access_token=***" : "EAA***");
    }

    /// <summary>Meta error code + message from a failed response.</summary>
    public static (int? Code, string Message) ParseError(string body, HttpStatusCode status)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                int? code = err.TryGetProperty("code", out var c) && c.TryGetInt32(out var n) ? n : null;
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                return (code, msg);
            }
        }
        catch (JsonException) { /* not JSON */ }
        return (null, $"Meta trả về lỗi HTTP {(int)status}.");
    }

    /// <summary>Kind of a Graph error from its code / HTTP status.</summary>
    public static string Classify(int? code, HttpStatusCode? status)
    {
        if (code is 190 or 102 or 463 or 467) return GrowthErrorKind.Token;
        if (code is 10 || code is >= 200 and <= 299) return GrowthErrorKind.Permission;
        if (code is 1 or 2 or 4 or 17 or 32 or 341 or 613 or 80001) return GrowthErrorKind.Transient;
        if (status is { } s && ((int)s >= 500 || s == HttpStatusCode.TooManyRequests || s == HttpStatusCode.RequestTimeout)) return GrowthErrorKind.Transient;
        if (code is 100 or 368 or 506 or 1500) return GrowthErrorKind.Content;
        return GrowthErrorKind.Unknown;
    }

    /// <summary>
    /// Kind of an error thrown by the existing publishing service (it only keeps Meta's message text, e.g.
    /// "Meta báo lỗi: (#200) …" or "Error validating access token: Session has expired").
    /// </summary>
    public static string ClassifyMessage(Exception ex)
    {
        if (ex is GrowthPublishException g) return g.Kind;
        if (ex is HttpRequestException or TaskCanceledException or TimeoutException or IOException) return GrowthErrorKind.Transient;
        var m = ex.Message ?? "";
        bool Has(string s) => m.Contains(s, StringComparison.OrdinalIgnoreCase);
        if (Has("(#190)") || Has("validating access token") || Has("Session has expired") || Has("access token") && Has("invalid")
            || Has("hết hạn") || Has("ngắt kết nối") || Has("chưa được kết nối")) return GrowthErrorKind.Token;
        if (Has("(#10)") || Has("(#200)") || Has("permission") || Has("quyền")) return GrowthErrorKind.Permission;
        if (Has("Không kết nối được") || Has("temporarily") || Has("try again") || Has("rate limit") || Has("(#4)") || Has("(#17)")
            || Has("(#32)") || Has("(#613)") || Has("(#1)") || Has("(#2)") || Has("lỗi 5") || Has("timed out")) return GrowthErrorKind.Transient;
        if (Has("(#100)") || Has("không tìm thấy file") || Has("Mất file") || Has("quá dài") || Has("định dạng")) return GrowthErrorKind.Content;
        return GrowthErrorKind.Unknown;
    }

    private async Task<JsonElement> SendAsync(HttpRequestMessage request, string what, CancellationToken ct)
    {
        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new GrowthPublishException(GrowthErrorKind.Transient, $"{what}: không kết nối được tới Meta ({ex.GetType().Name}).");
        }
        using (res)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                var (code, msg) = ParseError(body, res.StatusCode);
                var kind = Classify(code, res.StatusCode);
                throw new GrowthPublishException(kind, Redact($"{what}: Meta báo lỗi{(code != null ? $" (#{code})" : "")} {msg}".Trim()));
            }
            try { return JsonDocument.Parse(body).RootElement.Clone(); }
            catch (JsonException) { throw new GrowthPublishException(GrowthErrorKind.Transient, $"{what}: Meta trả về dữ liệu không đọc được."); }
        }
    }

    private Task<JsonElement> GetAsync(string pathAndQuery, string token, string what, CancellationToken ct)
    {
        var sep = pathAndQuery.Contains('?') ? '&' : '?';
        var req = new HttpRequestMessage(HttpMethod.Get, $"{Base}/{pathAndQuery}{sep}access_token={Uri.EscapeDataString(token)}");
        return SendAsync(req, what, ct);
    }

    private Task<JsonElement> PostFormAsync(string path, string token, IEnumerable<KeyValuePair<string, string>> fields, string what, CancellationToken ct)
    {
        var all = fields.Append(new("access_token", token)).ToList();
        var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{path}") { Content = new FormUrlEncodedContent(all) };
        return SendAsync(req, what, ct);
    }

    // ───────────────────────── publishing ─────────────────────────

    /// <summary>A text-only post on the Page's wall (discussion question). Returns the post id.</summary>
    public async Task<string> PublishTextPostAsync(Guid facebookAccountId, string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new GrowthPublishException(GrowthErrorKind.Content, "Bài viết chưa có nội dung.");
        var (pageId, token) = await PageTokenAsync(facebookAccountId, ct);
        var body = await PostFormAsync($"{pageId}/feed", token, new[] { new KeyValuePair<string, string>("message", message) }, "Đăng bài viết", ct);
        return body.GetProperty("id").GetString()!;
    }

    /// <summary>
    /// A picture as a Page Story: uploaded unpublished (/photos?published=false), then /photo_stories with its id.
    /// Meta: the picture must not have been used in a published post. Returns the story's post id.
    /// </summary>
    public async Task<string> PublishPhotoStoryAsync(Guid facebookAccountId, string imagePath, string mimeType, CancellationToken ct)
    {
        if (!File.Exists(imagePath)) throw new GrowthPublishException(GrowthErrorKind.Content, "Không tìm thấy file ảnh Story.");
        var (pageId, token) = await PageTokenAsync(facebookAccountId, ct);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "access_token");
        form.Add(new StringContent("false"), "published");
        var bytes = new ByteArrayContent(await File.ReadAllBytesAsync(imagePath, ct));
        bytes.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        form.Add(bytes, "source", Path.GetFileName(imagePath));
        var photo = await SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{Base}/{pageId}/photos") { Content = form }, "Tải ảnh Story", ct);
        var photoId = photo.GetProperty("id").GetString()!;
        JsonElement story;
        try
        {
            story = await PostFormAsync($"{pageId}/photo_stories", token, new[] { new KeyValuePair<string, string>("photo_id", photoId) }, "Đăng Story ảnh", ct);
        }
        catch (GrowthPublishException ex) when (ex.Kind is GrowthErrorKind.Content or GrowthErrorKind.Unknown)
        {
            // Meta answers "unsupported" for Pages / apps that can't post Stories: say so, the owner posts it by hand
            throw new GrowthPublishException(GrowthErrorKind.Unsupported, ex.Message);
        }
        return story.TryGetProperty("post_id", out var pid) && pid.GetString() is { Length: > 0 } p ? p
            : story.TryGetProperty("id", out var id) ? id.GetString()! : photoId;
    }

    // ───────────────────────── Instagram (the Page's linked professional account) ─────────────────────────

    private static readonly System.Text.RegularExpressions.Regex HashtagRx = new(@"#[\p{L}\p{N}_]+", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The linked Instagram account id (kept up to date by the existing Page connection) and the Page token.</summary>
    public async Task<(string IgUserId, string Token)> InstagramAsync(Guid facebookAccountId, CancellationToken ct)
    {
        var (_, token) = await PageTokenAsync(facebookAccountId, ct);
        var ig = await _db.FacebookAccounts.AsNoTracking().Where(a => a.Id == facebookAccountId).Select(a => a.InstagramAccountId).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(ig))
            throw new GrowthPublishException(GrowthErrorKind.Unsupported, "Page chưa liên kết tài khoản Instagram chuyên nghiệp (Cài đặt Page → Tài khoản được liên kết → Instagram, rồi bấm \"Làm mới Instagram\" ở Cài đặt → Facebook).");
        return (ig, token);
    }

    /// <summary>Instagram caption: at most 2 200 characters and 30 hashtags (Instagram refuses more).</summary>
    public static string InstagramCaption(string text)
    {
        text = (text ?? "").Trim();
        var tags = 0;
        text = HashtagRx.Replace(text, m => ++tags > 30 ? "" : m.Value);
        text = System.Text.RegularExpressions.Regex.Replace(text, "[ \t]{2,}", " ").Trim();
        return text.Length > 2200 ? text[..2200] : text;
    }

    private async Task<string> IgContainerAsync(string igUserId, string token, Dictionary<string, string> fields, string what, CancellationToken ct) =>
        (await PostFormAsync($"{igUserId}/media", token, fields, what, ct)).GetProperty("id").GetString()!;

    /// <summary>Waits until Instagram has processed a container (pictures: seconds; videos: up to a few minutes).</summary>
    private async Task WaitReadyAsync(string containerId, string token, TimeSpan max, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + max;
        while (true)
        {
            var body = await GetAsync($"{containerId}?fields=status_code,status", token, "Kiểm tra Instagram xử lý", ct);
            var status = body.TryGetProperty("status_code", out var sc) ? sc.GetString() : null;
            if (status is "FINISHED" or "PUBLISHED") return;
            if (status is "ERROR" or "EXPIRED")
                throw new GrowthPublishException(GrowthErrorKind.Content, "Instagram xử lý nội dung lỗi: " + (body.TryGetProperty("status", out var st) ? st.GetString() : status));
            if (DateTime.UtcNow > deadline) throw new GrowthPublishException(GrowthErrorKind.Transient, $"Instagram xử lý quá lâu (trạng thái: {status ?? "không rõ"}).");
            await Task.Delay(TimeSpan.FromSeconds(4), ct);
        }
    }

    private async Task<(string MediaId, string? Permalink)> IgPublishAsync(string igUserId, string token, string containerId, CancellationToken ct)
    {
        var published = await PostFormAsync($"{igUserId}/media_publish", token, new[] { new KeyValuePair<string, string>("creation_id", containerId) }, "Đăng lên Instagram", ct);
        var mediaId = published.GetProperty("id").GetString()!;
        string? permalink = null;
        try
        {
            var link = await GetAsync($"{mediaId}?fields=permalink", token, "Đọc liên kết Instagram", ct);
            permalink = link.TryGetProperty("permalink", out var pl) ? pl.GetString() : null;
        }
        catch (GrowthPublishException) { /* the post is live; the link is optional */ }
        return (mediaId, permalink);
    }

    /// <summary>
    /// One picture, or a carousel of 2–10, on Instagram. Instagram fetches each picture from its public JPEG link (it has no
    /// upload for pictures).
    /// </summary>
    public async Task<(string MediaId, string? Permalink)> PublishInstagramImagesAsync(Guid facebookAccountId, IReadOnlyList<string> imageUrls, string caption, CancellationToken ct)
    {
        if (imageUrls.Count == 0) throw new GrowthPublishException(GrowthErrorKind.Content, "Instagram cần ít nhất 1 ảnh.");
        var (ig, token) = await InstagramAsync(facebookAccountId, ct);
        string container;
        if (imageUrls.Count == 1)
        {
            container = await IgContainerAsync(ig, token, new() { ["image_url"] = imageUrls[0], ["caption"] = InstagramCaption(caption) }, "Instagram nhận ảnh", ct);
        }
        else
        {
            var children = new List<string>();
            foreach (var url in imageUrls.Take(10))
                children.Add(await IgContainerAsync(ig, token, new() { ["image_url"] = url, ["is_carousel_item"] = "true" }, $"Instagram nhận ảnh {children.Count + 1}", ct));
            foreach (var child in children) await WaitReadyAsync(child, token, TimeSpan.FromMinutes(1), ct);
            container = await IgContainerAsync(ig, token, new()
            {
                ["media_type"] = "CAROUSEL", ["children"] = string.Join(',', children), ["caption"] = InstagramCaption(caption)
            }, "Instagram tạo bài nhiều ảnh", ct);
        }
        await WaitReadyAsync(container, token, TimeSpan.FromMinutes(1), ct);
        return await IgPublishAsync(ig, token, container, ct);
    }

    /// <summary>An Instagram Story: a picture (from its public link) or a video (uploaded from the file, or fetched from its link).</summary>
    public async Task<(string MediaId, string? Permalink)> PublishInstagramStoryAsync(Guid facebookAccountId, string? imageUrl, string? videoPath, CancellationToken ct)
    {
        var (ig, token) = await InstagramAsync(facebookAccountId, ct);
        string container;
        if (imageUrl != null)
        {
            container = await IgContainerAsync(ig, token, new() { ["media_type"] = "STORIES", ["image_url"] = imageUrl }, "Instagram nhận ảnh Tin", ct);
            await WaitReadyAsync(container, token, TimeSpan.FromMinutes(1), ct);
        }
        else
        {
            if (videoPath == null || !File.Exists(videoPath)) throw new GrowthPublishException(GrowthErrorKind.Content, "Không tìm thấy video Tin.");
            var created = await PostFormAsync($"{ig}/media", token, new Dictionary<string, string> { ["media_type"] = "STORIES", ["upload_type"] = "resumable" }, "Instagram nhận video Tin", ct);
            container = created.GetProperty("id").GetString()!;
            var uploadUri = created.TryGetProperty("uri", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(uploadUri)) throw new GrowthPublishException(GrowthErrorKind.Unknown, "Instagram không trả về địa chỉ tải video lên.");
            var size = new FileInfo(videoPath).Length;
            await using (var stream = File.OpenRead(videoPath))
            {
                var upload = new HttpRequestMessage(HttpMethod.Post, uploadUri) { Content = new StreamContent(stream) };
                upload.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);
                upload.Headers.TryAddWithoutValidation("offset", "0");
                upload.Headers.TryAddWithoutValidation("file_size", size.ToString(CultureInfo.InvariantCulture));
                upload.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                await SendAsync(upload, "Tải video Tin lên Instagram", ct);
            }
            await WaitReadyAsync(container, token, TimeSpan.FromMinutes(5), ct);
        }
        return await IgPublishAsync(ig, token, container, ct);
    }

    // ───────────────────────── reading ─────────────────────────

    /// <summary>Permissions granted to the Page token (debug_token), or the reason they can't be read.</summary>
    public async Task<(List<string> Scopes, string? Error)> PermissionsAsync(Guid facebookAccountId, CancellationToken ct)
    {
        try
        {
            var (_, token) = await PageTokenAsync(facebookAccountId, ct);
            var body = await GetAsync($"debug_token?input_token={Uri.EscapeDataString(token)}", token, "Đọc quyền của mã truy cập", ct);
            var data = body.GetProperty("data");
            if (data.TryGetProperty("is_valid", out var valid) && valid.ValueKind == JsonValueKind.False)
                return (new(), "Mã truy cập của Page không còn hợp lệ — kết nối lại Page.");
            var scopes = data.TryGetProperty("scopes", out var sc) && sc.ValueKind == JsonValueKind.Array
                ? sc.EnumerateArray().Select(s => s.GetString() ?? "").Where(s => s.Length > 0).ToList()
                : new List<string>();
            return (scopes, null);
        }
        catch (GrowthPublishException ex)
        {
            return (new(), ex.Message);
        }
    }

    /// <summary>Current followers of the Page (followers_count), or the error.</summary>
    public async Task<(long? Followers, string? Error)> FollowersAsync(Guid facebookAccountId, CancellationToken ct)
    {
        try
        {
            var (pageId, token) = await PageTokenAsync(facebookAccountId, ct);
            var body = await GetAsync($"{pageId}?fields=followers_count,fan_count", token, "Đọc số người theo dõi", ct);
            foreach (var f in new[] { "followers_count", "fan_count" })
                if (body.TryGetProperty(f, out var v) && v.TryGetInt64(out var n)) return (n, null);
            return (null, "Meta không trả về số người theo dõi cho Page này.");
        }
        catch (GrowthPublishException ex)
        {
            return (null, ex.Message);
        }
    }

    public sealed record MetricSeries(string Metric, Dictionary<DateOnly, long> Values, string? Error);

    /// <summary>
    /// Day values of each metric between two dates, one request per metric so a retired / forbidden metric doesn't hide
    /// the others. Meta counts days in Pacific time; a value is filed under the day it covers.
    /// </summary>
    public async Task<List<MetricSeries>> PageInsightsAsync(Guid facebookAccountId, DateOnly from, DateOnly to, IEnumerable<string> metrics, CancellationToken ct)
    {
        var result = new List<MetricSeries>();
        string pageId, token;
        try
        {
            (pageId, token) = await PageTokenAsync(facebookAccountId, ct);
        }
        catch (GrowthPublishException ex)
        {
            return metrics.Select(m => new MetricSeries(m, new(), ex.Message)).ToList();
        }
        // Meta's since/until are unix times; until is exclusive, add a day of margin each side
        var since = new DateTimeOffset(from.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var until = new DateTimeOffset(to.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        foreach (var metric in metrics)
        {
            try
            {
                var body = await GetAsync($"{pageId}/insights?metric={metric}&period=day&since={since}&until={until}", token, metric, ct);
                result.Add(new MetricSeries(metric, ParseDayValues(body, metric, from, to), null));
            }
            catch (GrowthPublishException ex)
            {
                result.Add(new MetricSeries(metric, new(), ex.Message));
            }
        }
        return result;
    }

    /// <summary>Insights JSON → {day covered: value} for <paramref name="metric"/>, within [from, to].</summary>
    public static Dictionary<DateOnly, long> ParseDayValues(JsonElement body, string metric, DateOnly from, DateOnly to)
    {
        var values = new Dictionary<DateOnly, long>();
        if (!body.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return values;
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var name) || name.GetString() != metric) continue;
            if (!item.TryGetProperty("values", out var vals) || vals.ValueKind != JsonValueKind.Array) continue;
            foreach (var v in vals.EnumerateArray())
            {
                if (!v.TryGetProperty("end_time", out var et) || !DateTimeOffset.TryParse(et.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var end)) continue;
                // end_time is the (Pacific) midnight closing the day: 12 h earlier is inside that day
                var day = DateOnly.FromDateTime(end.UtcDateTime.AddHours(-12));
                if (day < from || day > to) continue;
                if (!v.TryGetProperty("value", out var value)) continue;
                long n = value.ValueKind switch
                {
                    JsonValueKind.Number => value.TryGetInt64(out var x) ? x : (long)value.GetDouble(),
                    JsonValueKind.Object => value.EnumerateObject().Sum(p => p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt64(out var y) ? y : 0),
                    _ => -1
                };
                if (n >= 0) values[day] = n;
            }
        }
        return values;
    }

    public sealed class PostStats
    {
        public long? Views { get; set; }
        public long? Reach { get; set; }
        public long? Reactions { get; set; }
        public long? Comments { get; set; }
        public long? Shares { get; set; }
        public long? Clicks { get; set; }
        public long? Follows { get; set; }
        public List<string> Errors { get; } = new();
    }

    /// <summary>Numbers of a wall post (photo / album / text): counts from the post, views/reach/clicks from its insights when allowed.</summary>
    public async Task<PostStats> PostStatsAsync(Guid facebookAccountId, string postId, CancellationToken ct)
    {
        var stats = new PostStats();
        var (_, token) = await PageTokenAsync(facebookAccountId, ct);
        try
        {
            var body = await GetAsync($"{postId}?fields=shares,reactions.limit(0).summary(total_count),comments.limit(0).summary(total_count)", token, "Đọc tương tác bài viết", ct);
            stats.Shares = body.TryGetProperty("shares", out var sh) && sh.TryGetProperty("count", out var sc) && sc.TryGetInt64(out var s) ? s : 0;
            stats.Reactions = Summary(body, "reactions");
            stats.Comments = Summary(body, "comments");
        }
        catch (GrowthPublishException ex) { stats.Errors.Add(ex.Message); }

        foreach (var (metric, set) in new (string, Action<long>)[]
                 {
                     ("post_media_view", v => stats.Views = v),
                     ("post_total_media_view_unique", v => stats.Reach = v),
                     ("post_clicks", v => stats.Clicks = v)
                 })
        {
            try
            {
                var body = await GetAsync($"{postId}/insights?metric={metric}&period=lifetime", token, metric, ct);
                if (FirstValue(body, metric) is { } v) set(v);
            }
            catch (GrowthPublishException ex) { stats.Errors.Add(ex.Message); }
        }
        return stats;
    }

    /// <summary>New followers Meta attributes to a Reel (post_video_followers), or null when not given to this Page.</summary>
    public async Task<(long? Follows, string? Error)> ReelFollowsAsync(Guid facebookAccountId, string videoId, CancellationToken ct)
    {
        try
        {
            var (_, token) = await PageTokenAsync(facebookAccountId, ct);
            var body = await GetAsync($"{videoId}/video_insights?metric=post_video_followers", token, "post_video_followers", ct);
            return (FirstValue(body, "post_video_followers"), null);
        }
        catch (GrowthPublishException ex)
        {
            return (null, ex.Message);
        }
    }

    private static long? Summary(JsonElement body, string field) =>
        body.TryGetProperty(field, out var f) && f.TryGetProperty("summary", out var s) && s.TryGetProperty("total_count", out var t) && t.TryGetInt64(out var n) ? n : null;

    public static long? FirstValue(JsonElement body, string metric)
    {
        if (!body.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in data.EnumerateArray())
        {
            if (item.TryGetProperty("name", out var n) && n.GetString() != metric) continue;
            if (item.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Array && vals.GetArrayLength() > 0
                && vals[0].TryGetProperty("value", out var v))
            {
                if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt64(out var x) ? x : (long)v.GetDouble();
                if (v.ValueKind == JsonValueKind.Object)
                    return v.EnumerateObject().Sum(p => p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt64(out var y) ? y : 0);
            }
        }
        return null;
    }
}

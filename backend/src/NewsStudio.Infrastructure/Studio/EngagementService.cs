using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.FilmFactory;

namespace NewsStudio.Infrastructure.Studio;

/// <summary>
/// Engagement on the channel's own posts, also the ones posted by hand:
/// <list type="bullet">
/// <item>a first comment from the page with a question that invites memories (AI-written for that video), on Facebook
/// and Instagram, for posts of the last 2 days that don't have one;</item>
/// <item>new comments from viewers gathered with an AI-drafted friendly reply, which the owner edits and sends (never
/// sent automatically).</item>
/// </list>
/// Runs every 30 minutes (and after "Tôi đã đăng xong"). Costs a fraction of a cent per post (Gemini text).
/// </summary>
public class EngagementService : IEngagementService
{
    public const string FirstCommentKey = "Engagement.FirstComment";
    private static readonly TimeSpan FirstCommentWindow = TimeSpan.FromDays(2);
    private static readonly TimeSpan CommentsWindow = TimeSpan.FromDays(3);
    private const int MaxSuggestionsPerRun = 40;
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    private readonly IApplicationDbContext _db;
    private readonly IFacebookPublishingService _facebook;
    private readonly FilmGeminiClient _gemini;
    private readonly ILogger<EngagementService> _logger;

    public EngagementService(IApplicationDbContext db, IFacebookPublishingService facebook, FilmGeminiClient gemini, ILogger<EngagementService> logger)
    {
        _db = db;
        _facebook = facebook;
        _gemini = gemini;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(1800)]
    public async Task RunAsync(CancellationToken ct)
    {
        if (!await RunLock.WaitAsync(0, ct)) return;
        try
        {
            await FirstCommentsAsync(ct);
            await GatherCommentsAsync(ct);
            await SuggestRepliesAsync(ct);
        }
        finally
        {
            RunLock.Release();
        }
    }

    public async Task<bool> FirstCommentEnabledAsync(CancellationToken ct) =>
        await _db.SystemSettings.AsNoTracking().Where(s => s.SettingKey == FirstCommentKey).Select(s => s.SettingValue).FirstOrDefaultAsync(ct) != "off";

    public async Task SetFirstCommentEnabledAsync(bool enabled, CancellationToken ct)
    {
        var row = await _db.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == FirstCommentKey, ct);
        if (row == null) _db.SystemSettings.Add(new SystemSetting { SettingKey = FirstCommentKey, SettingValue = enabled ? "on" : "off", Group = "Engagement" });
        else row.SettingValue = enabled ? "on" : "off";
        await _db.SaveChangesAsync(ct);
    }

    // ── 1. the page's first comment ──

    private async Task FirstCommentsAsync(CancellationToken ct)
    {
        if (!await FirstCommentEnabledAsync(ct)) return;
        var since = DateTime.UtcNow - FirstCommentWindow;
        // news Reels get their own first comment when published; this is for Video AI posts and posts made by hand
        var posts = await _db.FacebookPosts
            .Include(p => p.FacebookAccount)
            .Where(p => p.Source != "Tool" && p.Status == "Published" && p.PublishedAtUtc != null && p.PublishedAtUtc >= since
                        && p.FacebookAccount != null && p.FacebookAccount.IsActive
                        && ((p.FacebookVideoId != null && p.FirstCommentId == null && p.FirstCommentError == null)
                            || (p.InstagramMediaId != null && p.InstagramFirstCommentId == null && p.InstagramFirstCommentError == null)))
            .OrderBy(p => p.PublishedAtUtc)
            .Take(10)
            .ToListAsync(ct);
        foreach (var post in posts)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(post.FirstCommentText))
                {
                    post.FirstCommentText = await WriteFirstCommentAsync(post, ct);
                    await _db.SaveChangesAsync(ct);
                }
                if (string.IsNullOrWhiteSpace(post.FirstCommentText)) continue;
                if (post.FacebookVideoId != null && post.FirstCommentId == null && post.FirstCommentError == null)
                {
                    try { post.FirstCommentId = await _facebook.CommentOnPostAsync(post.Id, "facebook", post.FirstCommentText, ct); }
                    catch (InvalidOperationException ex) { post.FirstCommentError = Trim(ex.Message, 1000); }
                }
                if (post.InstagramMediaId != null && post.InstagramFirstCommentId == null && post.InstagramFirstCommentError == null)
                {
                    try { post.InstagramFirstCommentId = await _facebook.CommentOnPostAsync(post.Id, "instagram", post.FirstCommentText, ct); }
                    catch (InvalidOperationException ex) { post.InstagramFirstCommentError = Trim(ex.Message, 1000); }
                }
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("First comment for post {Id} failed: {Message}", post.Id, ex.Message);
            }
        }
    }

    /// <summary>A short, warm question about the video's memory that viewers want to answer (not "comment 1 if…" bait).</summary>
    private async Task<string> WriteFirstCommentAsync(FacebookPost post, CancellationToken ct)
    {
        var about = await AboutAsync(post, ct);
        var page = post.FacebookAccount?.PageName ?? "";
        var system = "Bạn là người quản lý fanpage hoài niệm tuổi thơ Việt Nam, viết bình luận ghim đầu tiên dưới video. Chỉ trả về JSON hợp lệ.";
        var user = new StringBuilder()
            .AppendLine($"Page: {page}")
            .AppendLine($"Nội dung video:\n{about}")
            .AppendLine(@"
Viết 1 bình luận (1–2 câu, tối đa 35 chữ) của chính Page: một câu hỏi gợi người xem kể kỷ niệm của họ liên quan ĐÚNG chi tiết trong video
(vd ""Hồi nhỏ nhà bạn có món tóp mỡ chan nước mắm không? Kể Trạm nghe với 👇""). Giọng gần gũi, mộc mạc, 1 emoji.
KHÔNG xin like/share, KHÔNG ""comment 1 nếu…"", KHÔNG hashtag, KHÔNG link.
Trả về: { ""comment"": ""..."" }").ToString();
        var result = await _gemini.GenerateJsonAsync(system, user, 1024, 0.8, ct, null, true);
        return ReadString(result.Text, "comment") ?? "";
    }

    // ── 5. viewers' comments, with a reply drafted by AI ──

    private async Task GatherCommentsAsync(CancellationToken ct)
    {
        var since = DateTime.UtcNow - CommentsWindow;
        var posts = await _db.FacebookPosts.AsNoTracking()
            .Where(p => p.Status == "Published" && p.PublishedAtUtc != null && p.PublishedAtUtc >= since
                        && p.FacebookAccount != null && p.FacebookAccount.IsActive
                        && (p.FacebookVideoId != null || p.InstagramMediaId != null))
            .Select(p => p.Id)
            .ToListAsync(ct);
        foreach (var postId in posts)
        {
            try
            {
                var remote = await _facebook.ListPostCommentsAsync(postId, ct);
                if (remote.Count == 0) continue;
                var ids = remote.Select(r => r.Id).ToList();
                var known = await _db.PostComments.Where(c => ids.Contains(c.ExternalId)).Select(c => c.ExternalId).ToListAsync(ct);
                foreach (var r in remote.Where(r => !known.Contains(r.Id) && !r.FromPage && !string.IsNullOrWhiteSpace(r.Message)))
                {
                    _db.PostComments.Add(new PostComment
                    {
                        FacebookPostId = postId, Network = r.Network, ExternalId = r.Id, Author = Trim(r.Author, 200),
                        Message = Trim(r.Message, 2000)!, CommentedAtUtc = r.CreatedUtc, Status = "New"
                    });
                }
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogInformation("Reading comments of post {Id} failed: {Message}", postId, ex.Message);
            }
        }
    }

    private async Task SuggestRepliesAsync(CancellationToken ct)
    {
        var waiting = await _db.PostComments
            .Where(c => c.Status == "New" && c.SuggestedReply == null)
            .OrderBy(c => c.CommentedAtUtc)
            .Take(MaxSuggestionsPerRun)
            .ToListAsync(ct);
        foreach (var group in waiting.GroupBy(c => c.FacebookPostId))
        {
            try
            {
                var post = await _db.FacebookPosts.Include(p => p.FacebookAccount).FirstAsync(p => p.Id == group.Key, ct);
                var list = group.ToList();
                var replies = await DraftRepliesAsync(post, list, ct);
                for (var i = 0; i < list.Count; i++)
                {
                    var reply = i < replies.Count ? replies[i] : null;
                    if (reply == null) continue;
                    if (reply.Length == 0) list[i].Status = "Skipped"; // spam / insult: nothing worth answering
                    else list[i].SuggestedReply = Trim(reply, 1000);
                }
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Drafting replies for post {Id} failed: {Message}", group.Key, ex.Message);
            }
        }
    }

    private async Task<List<string?>> DraftRepliesAsync(FacebookPost post, List<PostComment> comments, CancellationToken ct)
    {
        var about = await AboutAsync(post, ct);
        var page = post.FacebookAccount?.PageName ?? "Page";
        var system = "Bạn trả lời bình luận thay cho chủ fanpage Việt Nam. Chỉ trả về JSON hợp lệ.";
        var sb = new StringBuilder()
            .AppendLine($"Page: {page}")
            .AppendLine($"Video: {about}")
            .AppendLine("\nBÌNH LUẬN (theo thứ tự):");
        for (var i = 0; i < comments.Count; i++) sb.AppendLine($"{i + 1}. {comments[i].Author}: {comments[i].Message}");
        sb.AppendLine($@"
Viết câu trả lời cho TỪNG bình luận, đúng thứ tự, đúng số lượng ({comments.Count}):
- Giọng của Page, thân thiện, mộc mạc, ấm áp như người nhà (xưng ""Trạm"" hoặc ""mình"", gọi người xem là ""bạn""), 1–2 câu, có thể 1 emoji.
- Đáp đúng điều họ nói (kỷ niệm, câu hỏi, lời khen, góp ý); hỏi lại 1 câu ngắn nếu tự nhiên để họ trò chuyện tiếp.
- Không hứa hẹn, không quảng cáo, không link. Góp ý / chê thì cảm ơn chân thành.
- Bình luận spam, quảng cáo, chửi bới, vô nghĩa → chuỗi rỗng """".
Trả về: {{ ""replies"": [""..."", ""...""] }}");
        var result = await _gemini.GenerateJsonAsync(system, sb.ToString(), 4096, 0.7, ct, null, true);
        var text = result.Text.Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        var replies = new List<string?>();
        if (start < 0 || end <= start) return replies;
        using var doc = JsonDocument.Parse(text[start..(end + 1)]);
        if (doc.RootElement.TryGetProperty("replies", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var r in arr.EnumerateArray()) replies.Add(r.ValueKind == JsonValueKind.String ? r.GetString()?.Trim() ?? "" : null);
        return replies;
    }

    public async Task<List<PostCommentDto>> ListCommentsAsync(string status, CancellationToken ct)
    {
        var rows = await _db.PostComments.AsNoTracking()
            .Where(c => status == "all" || c.Status == status)
            .OrderByDescending(c => c.CommentedAtUtc)
            .Take(200)
            .Select(c => new
            {
                c.Id, c.Network, c.Author, c.Message, c.CommentedAtUtc, c.Status, c.SuggestedReply, c.SentReply, c.ReplyError,
                PostTitle = c.FacebookPost!.VideoProject != null ? c.FacebookPost.VideoProject.Title : c.FacebookPost.Title,
                PageName = c.FacebookPost.FacebookAccount != null ? c.FacebookPost.FacebookAccount.PageName : null,
                c.FacebookPost.FacebookVideoId, c.FacebookPost.InstagramPermalink
            })
            .ToListAsync(ct);
        return rows.Select(c => new PostCommentDto
        {
            Id = c.Id, Network = c.Network, Author = c.Author, Message = c.Message, CommentedAtUtc = c.CommentedAtUtc, Status = c.Status,
            SuggestedReply = c.SuggestedReply, SentReply = c.SentReply, ReplyError = c.ReplyError, PostTitle = c.PostTitle ?? "(không tên)", PageName = c.PageName,
            PostUrl = c.Network == "instagram" ? c.InstagramPermalink : c.FacebookVideoId != null ? $"https://www.facebook.com/reel/{c.FacebookVideoId}" : null
        }).ToList();
    }

    /// <summary>Sends the owner's (possibly edited) reply under the comment, on the network it was written on.</summary>
    public async Task<PostCommentDto> ReplyAsync(Guid commentId, string message, CancellationToken ct)
    {
        var c = await _db.PostComments.FirstOrDefaultAsync(x => x.Id == commentId, ct) ?? throw new KeyNotFoundException("Không tìm thấy bình luận.");
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Câu trả lời đang trống.");
        if (c.Status == "Sent") throw new InvalidOperationException("Bình luận này đã được trả lời.");
        try
        {
            c.ReplyId = await _facebook.ReplyToCommentAsync(c.FacebookPostId, c.Network, c.ExternalId, message.Trim(), ct);
            c.SentReply = Trim(message.Trim(), 1000);
            c.Status = "Sent";
            c.ReplyError = null;
            c.RepliedAtUtc = DateTime.UtcNow;
        }
        catch (InvalidOperationException ex)
        {
            c.ReplyError = Trim(ex.Message, 1000);
            await _db.SaveChangesAsync(ct);
            throw;
        }
        await _db.SaveChangesAsync(ct);
        return (await ListCommentsAsync("all", ct)).First(x => x.Id == commentId);
    }

    public async Task DismissAsync(Guid commentId, CancellationToken ct)
    {
        var c = await _db.PostComments.FirstOrDefaultAsync(x => x.Id == commentId, ct) ?? throw new KeyNotFoundException("Không tìm thấy bình luận.");
        if (c.Status != "Sent") c.Status = "Dismissed";
        await _db.SaveChangesAsync(ct);
    }

    // ── helpers ──

    /// <summary>What the video is about: the studio video's caption and title, or the post's own text.</summary>
    private async Task<string> AboutAsync(FacebookPost post, CancellationToken ct)
    {
        if (post.StudioVideoId is { } sid)
        {
            var v = await _db.StudioVideos.AsNoTracking().Where(x => x.Id == sid).Select(x => new { x.Title, x.Caption, x.Script }).FirstOrDefaultAsync(ct);
            if (v != null)
            {
                var script = v.Script.Length > 1500 ? v.Script[..1500] : v.Script;
                return $"{v.Title}\n{v.Caption}\n{script}".Trim();
            }
        }
        if (post.VideoProjectId is { } pid)
        {
            var title = await _db.VideoProjects.AsNoTracking().Where(x => x.Id == pid).Select(x => x.Title).FirstOrDefaultAsync(ct);
            if (title != null) return title;
        }
        return post.Title ?? "";
    }

    private static string? ReadString(string json, string field)
    {
        var text = json.Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            return doc.RootElement.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;
        }
        catch (JsonException) { return null; }
    }

    private static string? Trim(string? s, int max) => s == null ? null : s.Length > max ? s[..max] : s;
}

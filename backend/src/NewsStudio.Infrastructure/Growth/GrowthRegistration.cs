using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;

namespace NewsStudio.Infrastructure.Growth;

public static class GrowthRegistration
{
    public static IServiceCollection AddGrowthCampaignServices(this IServiceCollection services)
    {
        services.AddHttpClient<GrowthGraphClient>(c => c.Timeout = TimeSpan.FromMinutes(3));
        services.AddScoped<GrowthCampaignService>();
        services.AddScoped<GrowthContentService>();
        services.AddScoped<IGrowthPublishGateway, GrowthPublishGateway>();
        services.AddScoped<GrowthPublisher>();
        services.AddScoped<GrowthInsightsService>();
        services.AddScoped<IGrowthTextAi, GeminiGrowthTextAi>();
        services.AddScoped<IGrowthImageAi, GeminiGrowthImageAi>();
        services.AddSingleton<GrowthMedia>();
        services.AddScoped<GrowthAutopilotService>();
        services.AddScoped<GrowthAiService>();
        services.AddScoped<GrowthReportService>();
        services.AddScoped<GrowthCompetitorService>();
        services.AddScoped<GrowthCapabilityService>();
        return services;
    }
}

/// <summary>
/// "What can this Page do through the API?" — checked live with the Page's own token: granted permissions, follower
/// read, each Page Insights metric. Posting can't be tested without posting, so it is judged from the permissions; Stories
/// are only known on the first try (a refusal turns the item into a manual post).
/// </summary>
public class GrowthCapabilityService
{
    private readonly IApplicationDbContext _db;
    private readonly GrowthGraphClient _graph;

    public GrowthCapabilityService(IApplicationDbContext db, GrowthGraphClient graph)
    {
        _db = db;
        _graph = graph;
    }

    public async Task<GrowthCapabilitiesDto> CheckAsync(Guid facebookAccountId, CancellationToken ct)
    {
        if (!await _db.FacebookAccounts.AnyAsync(a => a.Id == facebookAccountId, ct)) throw new KeyNotFoundException("Không tìm thấy Page.");
        var dto = new GrowthCapabilitiesDto { FacebookAccountId = facebookAccountId, GraphApiVersion = _graph.Version, CheckedAtUtc = DateTime.UtcNow };
        var (scopes, tokenError) = await _graph.PermissionsAsync(facebookAccountId, ct);
        dto.GrantedPermissions = scopes;
        dto.TokenError = tokenError;
        bool Has(string p) => scopes.Contains(p);
        string ByPerm(params string[] perms) => tokenError != null ? "Unknown" : perms.All(Has) ? "Yes" : "No";
        string? Missing(params string[] perms) => tokenError ?? (perms.All(Has) ? null : "Thiếu quyền: " + string.Join(", ", perms.Where(p => !Has(p))));

        void Add(string key, string feature, string api, string perms, string available, string? detail) =>
            dto.Items.Add(new GrowthCapabilityDto { Key = key, Feature = feature, Api = api, Permissions = perms, Available = available, Detail = detail });

        Add("reels", "Đăng Reels", "POST /{page}/video_reels", "pages_manage_posts, pages_read_engagement", ByPerm("pages_manage_posts", "pages_read_engagement"),
            Missing("pages_manage_posts", "pages_read_engagement") ?? "Dùng chức năng đăng video hiện có của ứng dụng.");
        Add("photo", "Đăng ảnh / nhiều ảnh", "POST /{page}/photos + /{page}/feed", "pages_manage_posts, pages_read_engagement",
            ByPerm("pages_manage_posts", "pages_read_engagement"), Missing("pages_manage_posts", "pages_read_engagement"));
        Add("text", "Đăng bài chữ (tương tác)", "POST /{page}/feed", "pages_manage_posts", ByPerm("pages_manage_posts"), Missing("pages_manage_posts"));
        Add("video_story", "Story video", "POST /{page}/video_stories", "pages_manage_posts", tokenError != null ? "Unknown" : Has("pages_manage_posts") ? "Unknown" : "No",
            Missing("pages_manage_posts") ?? "Có quyền; Meta chỉ xác nhận khi đăng thử. Nếu bị từ chối, nội dung chuyển sang hướng dẫn đăng thủ công.");
        Add("photo_story", "Story ảnh", "POST /{page}/photo_stories", "pages_manage_posts", tokenError != null ? "Unknown" : Has("pages_manage_posts") ? "Unknown" : "No",
            Missing("pages_manage_posts") ?? "Có quyền; Meta chỉ xác nhận khi đăng thử. Nếu bị từ chối, nội dung chuyển sang hướng dẫn đăng thủ công.");

        var (followers, followersError) = await _graph.FollowersAsync(facebookAccountId, ct);
        Add("followers", "Đọc số follower", "GET /{page}?fields=followers_count", "pages_read_engagement", followers != null ? "Yes" : "No",
            followers != null ? $"Hiện tại: {followers:N0}" : followersError);

        var yesterday = GrowthTime.Today(DateTime.UtcNow).AddDays(-2);
        foreach (var s in await _graph.PageInsightsAsync(facebookAccountId, yesterday, yesterday.AddDays(1), GrowthGraphClient.PageDayMetrics, ct))
            Add("insight:" + s.Metric, "Page Insights · " + s.Metric, $"GET /{{page}}/insights?metric={s.Metric}&period=day", "read_insights",
                s.Error == null ? "Yes" : "No", s.Error ?? (s.Values.Count == 0 ? "Có quyền nhưng Meta chưa có giá trị cho ngày gần nhất." : null));

        Add("post_stats", "Số liệu bài ảnh / chữ", "GET /{post}?fields=shares,reactions,comments + /{post}/insights", "pages_read_engagement, read_insights",
            ByPerm("pages_read_engagement", "read_insights"), Missing("pages_read_engagement", "read_insights") ?? "Từng metric insight được thử khi đồng bộ.");
        Add("reel_stats", "Số liệu Reels", "GET /{video}/video_insights", "read_insights", ByPerm("read_insights"), Missing("read_insights"));
        Add("story_stats", "Số liệu Story", "—", "—", "No", "Meta không cung cấp số liệu Story của Page qua Graph API ổn định.");
        Add("meta_one", "Meta One (nút Theo dõi nổi bật, tự mời theo dõi)", "—", "—", "Manual", "Không có API — dùng checklist và nhập tay số lời mời.");
        Add("competitor_insights", "Insights của Page đối thủ", "—", "—", "No", "Không được phép truy cập Insights của Page không do bạn quản lý — chỉ nhập tay số liệu công khai.");
        return dto;
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Infrastructure.Growth;

public static class GrowthStatus
{
    // campaign
    public const string Draft = "Draft";
    public const string Running = "Running";
    public const string Paused = "Paused";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    // content (Draft / Cancelled shared)
    public const string PendingApproval = "PendingApproval";
    public const string Scheduled = "Scheduled";
    public const string Publishing = "Publishing";
    public const string Published = "Published";
    public const string Failed = "Failed";

    public static readonly string[] CampaignStatuses = { Draft, Running, Paused, Completed, Cancelled };
    public static readonly string[] ContentStatuses = { Draft, PendingApproval, Scheduled, Publishing, Published, Failed, Cancelled };
}

public static class GrowthKinds
{
    public const string Reel = "Reel";
    public const string Photo = "Photo";
    public const string Album = "Album";
    public const string Story = "Story";
    public const string Interactive = "Interactive";
    public static readonly string[] All = { Reel, Photo, Album, Story, Interactive };
}

/// <summary>Campaigns: create (default "Meta One 7-Day Growth Challenge"), edit, lifecycle, Meta One checklist, typed-in numbers.</summary>
public class GrowthCampaignService
{
    public const string DefaultName = "Meta One 7-Day Growth Challenge";
    public const int MaxDays = 31;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly IFacebookPublishingService _facebook;
    private readonly ILogger<GrowthCampaignService> _logger;

    public GrowthCampaignService(IApplicationDbContext db, IFacebookPublishingService facebook, ILogger<GrowthCampaignService> logger)
    {
        _db = db;
        _facebook = facebook;
        _logger = logger;
    }

    // ───────────────────────── checklist & plan ─────────────────────────

    public static readonly (string Key, string Label, string Help)[] ChecklistItems =
    {
        ("follow_button", "Đã bật nút Theo dõi nổi bật",
            "Trong ứng dụng Facebook: Page → Meta Verified / Meta One → quyền lợi → nút Theo dõi nổi bật. Không có API để bật — hãy tự kiểm tra."),
        ("auto_invite", "Đã kiểm tra tính năng tự động mời theo dõi",
            "Meta tự mời người đã tương tác theo dõi Page nếu tài khoản được cấp quyền lợi này. Công cụ không gửi lời mời thay Meta."),
        ("benefits", "Đã kiểm tra quyền lợi Meta One",
            "Ghi lại quyền lợi gói đang có (huy hiệu, hỗ trợ, bảo vệ mạo danh, tăng hiển thị…) và giá thực trả vào Cài đặt chiến dịch."),
        ("invite_results", "Đã kiểm tra kết quả lời mời",
            "Xem số lời mời đã gửi / được chấp nhận trong ứng dụng Facebook và nhập vào mục \"Số liệu nhập tay\" từng ngày."),
        ("new_followers", "Đã ghi nhận follower mới",
            "Số follower được đọc tự động từ Meta mỗi giờ; đánh dấu khi đã đối chiếu với Facebook."),
    };

    /// <summary>The suggested 7-day plan (a template — the owner can change it; nothing forces posting every slot).</summary>
    public static List<GrowthPlanDayDto> DefaultPlan(DateOnly start, int days)
    {
        var template = new (string Focus, string[] Tasks)[]
        {
            ("Đo dữ liệu ban đầu", new[] { "Đo follower, lượt xem ban đầu", "Kiểm tra cấu hình Page & quyền lợi Meta One", "Đăng 1 Reels", "Đăng 2 bài ảnh", "Đăng 3 Story" }),
            ("Thử nội dung cảm xúc", new[] { "Đăng 1 Reels", "Caption cảm xúc / gợi ký ức", "Đo lượt chia sẻ" }),
            ("Kích thích tương tác", new[] { "Đăng 1 Reels", "Bài hỏi đáp / thảo luận", "Theo dõi follower mới" }),
            ("Thử caption & CTA", new[] { "Đăng 1 Reels", "2 phiên bản caption + CTA theo dõi", "So sánh hiệu quả" }),
            ("Nhân bản chủ đề tốt nhất", new[] { "Ưu tiên chủ đề có kết quả tốt nhất", "Tạo biến thể nội dung" }),
            ("Điều chỉnh giờ đăng", new[] { "Dời giờ đăng theo dữ liệu thực tế", "Đẩy mạnh nội dung hiệu quả" }),
            ("Tổng kết", new[] { "Hoàn thành chiến dịch", "Tổng hợp dữ liệu", "Xuất báo cáo" }),
        };
        var plan = new List<GrowthPlanDayDto>();
        for (var i = 0; i < days; i++)
        {
            (string Focus, string[] Tasks) t = i == days - 1 && days > 1 ? template[^1]
                : i < template.Length - 1 ? template[i]
                : ("Duy trì & tối ưu", new[] { "Đăng theo lịch", "Theo dõi số liệu", "Điều chỉnh theo gợi ý" });
            plan.Add(new GrowthPlanDayDto { Day = i + 1, Date = start.AddDays(i), Focus = t.Focus, Tasks = t.Tasks.ToList() });
        }
        return plan;
    }

    private sealed class CheckState
    {
        public bool Done { get; set; }
        public string? Note { get; set; }
        public DateTime? AtUtc { get; set; }
    }

    private static Dictionary<string, CheckState> ReadChecklist(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, CheckState>>(json, Json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static List<GrowthChecklistItemDto> Checklist(GrowthCampaign c)
    {
        var state = ReadChecklist(c.ChecklistJson);
        return ChecklistItems.Select(i => new GrowthChecklistItemDto
        {
            Key = i.Key, Label = i.Label, Help = i.Help,
            Done = state.TryGetValue(i.Key, out var s) && s.Done, Note = state.GetValueOrDefault(i.Key)?.Note, AtUtc = state.GetValueOrDefault(i.Key)?.AtUtc
        }).ToList();
    }

    public static List<GrowthPlanDayDto> Plan(GrowthCampaign c)
    {
        try
        {
            var plan = JsonSerializer.Deserialize<List<GrowthPlanDayDto>>(c.PlanJson, Json) ?? new();
            foreach (var d in plan) d.Date = c.StartDate.AddDays(d.Day - 1);
            return plan;
        }
        catch (JsonException) { return new(); }
    }

    public static List<GrowthMetricAvailabilityDto> Availability(GrowthCampaign c)
    {
        try
        {
            return (JsonSerializer.Deserialize<Dictionary<string, GrowthMetricAvailabilityDto>>(c.MetricAvailabilityJson, Json) ?? new())
                .Select(kv => { kv.Value.Metric = kv.Key; return kv.Value; }).OrderBy(x => x.Metric).ToList();
        }
        catch (JsonException) { return new(); }
    }

    // ───────────────────────── mapping ─────────────────────────

    public static GrowthCampaignDto ToDto(GrowthCampaign c, FacebookAccount? page, Dictionary<string, int>? counts = null)
    {
        var today = GrowthTime.Today(DateTime.UtcNow);
        return new GrowthCampaignDto
        {
            Id = c.Id, Name = c.Name, FacebookAccountId = c.FacebookAccountId,
            PageName = page?.PageName ?? c.PageName, PageId = page?.PageId,
            PageConnected = page != null && page.IsActive && !string.IsNullOrWhiteSpace(page.EncryptedAccessToken),
            Status = c.Status, StartDate = c.StartDate, EndDate = c.EndDate,
            TotalDays = c.EndDate.DayNumber - c.StartDate.DayNumber + 1,
            DayNumber = GrowthTime.DayNumber(c.StartDate, c.EndDate, today),
            StartedAtUtc = c.StartedAtUtc, EndedAtUtc = c.EndedAtUtc,
            TargetFollowers = c.TargetFollowers, TargetViews = c.TargetViews,
            ReelsPerDay = c.ReelsPerDay, PhotosPerDay = c.PhotosPerDay, StoriesPerDay = c.StoriesPerDay,
            TimeSlots = GrowthTime.ParseSlots(c.TimeSlots), AiBudgetUsd = c.AiBudgetUsd, AiSpentUsd = c.AiSpentUsd,
            ApprovalMode = c.ApprovalMode, AlsoInstagram = c.AlsoInstagram, InstagramUsername = page?.InstagramUsername, StartingFollowers = c.StartingFollowers, StartingFollowersAtUtc = c.StartingFollowersAtUtc,
            EndingFollowers = c.EndingFollowers, EndingFollowersAtUtc = c.EndingFollowersAtUtc,
            MetaOneCost = c.MetaOneCost, MetaOneCurrency = c.MetaOneCurrency,
            Checklist = Checklist(c), Plan = Plan(c), MetricAvailability = Availability(c),
            LastSyncedAtUtc = c.LastSyncedAtUtc, LastSyncError = c.LastSyncError, Notes = c.Notes, CreatedAtUtc = c.CreatedAtUtc,
            ContentCounts = counts ?? new()
        };
    }

    // ───────────────────────── loading (IDOR guard) ─────────────────────────

    public async Task<GrowthCampaign> LoadAsync(Guid id, CancellationToken ct) =>
        await _db.GrowthCampaigns.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy chiến dịch.");

    public async Task<GrowthCampaignDto> GetAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.GrowthCampaigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Không tìm thấy chiến dịch.");
        return await DtoAsync(c, ct);
    }

    public async Task<GrowthCampaignDto> DtoAsync(GrowthCampaign c, CancellationToken ct)
    {
        var page = c.FacebookAccountId is { } pid ? await _db.FacebookAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == pid, ct) : null;
        var counts = await _db.GrowthScheduledContents.AsNoTracking().Where(x => x.CampaignId == c.Id)
            .GroupBy(x => x.Status).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return ToDto(c, page, counts);
    }

    public async Task<List<GrowthCampaignDto>> ListAsync(Guid? facebookAccountId, CancellationToken ct)
    {
        var q = _db.GrowthCampaigns.AsNoTracking().AsQueryable();
        if (facebookAccountId is { } pid) q = q.Where(c => c.FacebookAccountId == pid);
        var list = await q.OrderByDescending(c => c.CreatedAtUtc).Take(100).ToListAsync(ct);
        var pageIds = list.Where(c => c.FacebookAccountId != null).Select(c => c.FacebookAccountId!.Value).Distinct().ToList();
        var pages = await _db.FacebookAccounts.AsNoTracking().Where(a => pageIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var ids = list.Select(c => c.Id).ToList();
        var counts = await _db.GrowthScheduledContents.AsNoTracking().Where(x => ids.Contains(x.CampaignId))
            .GroupBy(x => new { x.CampaignId, x.Status }).Select(g => new { g.Key.CampaignId, g.Key.Status, N = g.Count() }).ToListAsync(ct);
        return list.Select(c => ToDto(c, c.FacebookAccountId is { } p ? pages.GetValueOrDefault(p) : null,
            counts.Where(x => x.CampaignId == c.Id).ToDictionary(x => x.Status, x => x.N))).ToList();
    }

    public async Task<List<GrowthPageDto>> PagesAsync(CancellationToken ct)
    {
        var pages = await _db.FacebookAccounts.AsNoTracking().OrderBy(a => a.PageName).ToListAsync(ct);
        var ids = pages.Select(p => p.Id).ToList();
        var latest = await _db.PageFollowerSnapshots.AsNoTracking().Where(s => ids.Contains(s.FacebookAccountId))
            .GroupBy(s => s.FacebookAccountId)
            .Select(g => g.OrderByDescending(s => s.Date).Select(s => new { s.FacebookAccountId, s.Date, s.FacebookFollowers }).First())
            .ToListAsync(ct);
        var active = await _db.GrowthCampaigns.AsNoTracking()
            .Where(c => c.FacebookAccountId != null && ids.Contains(c.FacebookAccountId.Value) && (c.Status == GrowthStatus.Running || c.Status == GrowthStatus.Paused))
            .Select(c => new { c.FacebookAccountId, c.Id, c.Name }).ToListAsync(ct);
        return pages.Select(p =>
        {
            var f = latest.FirstOrDefault(x => x.FacebookAccountId == p.Id);
            var a = active.FirstOrDefault(x => x.FacebookAccountId == p.Id);
            return new GrowthPageDto
            {
                Id = p.Id, PageId = p.PageId, PageName = p.PageName, IsActive = p.IsActive, Status = p.Status,
                HasToken = !string.IsNullOrWhiteSpace(p.EncryptedAccessToken), TokenExpiresAtUtc = p.TokenExpiresAtUtc,
                InstagramUsername = p.InstagramUsername, PictureUrl = PictureUrl(p.PageId),
                LatestFollowers = f?.FacebookFollowers, LatestFollowersDate = f?.Date,
                ActiveCampaignId = a?.Id, ActiveCampaignName = a?.Name
            };
        }).ToList();
    }

    /// <summary>The Page's public profile picture (no token needed).</summary>
    public static string PictureUrl(string pageId) =>
        string.IsNullOrWhiteSpace(pageId) || !pageId.All(char.IsDigit) ? "" : $"https://graph.facebook.com/{pageId}/picture?type=large";

    // ───────────────────────── create / edit ─────────────────────────

    public async Task<GrowthCampaignDto> CreateAsync(GrowthCampaignInputDto input, CancellationToken ct)
    {
        if (input.FacebookAccountId is not { } pageId) throw new ArgumentException("Hãy chọn Page cho chiến dịch.");
        var page = await _db.FacebookAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == pageId, ct)
                   ?? throw new ArgumentException("Không tìm thấy Page này trong danh sách Page đã kết nối.");
        var today = GrowthTime.Today(DateTime.UtcNow);
        var start = input.StartDate ?? today;
        if (start < today) throw new ArgumentException("Ngày bắt đầu không được ở quá khứ.");
        var end = input.EndDate ?? start.AddDays(Math.Clamp(input.Days ?? 7, 1, MaxDays) - 1);
        ValidateDates(start, end);

        var c = new GrowthCampaign
        {
            Name = Clean(input.Name, 200) is { Length: > 0 } n ? n : DefaultName,
            FacebookAccountId = page.Id, PageName = page.PageName, Status = GrowthStatus.Draft,
            StartDate = start, EndDate = end,
            TimeSlots = string.Join(",", GrowthTime.NormalizeSlots(input.TimeSlots ?? new List<string>())),
            ApprovalMode = "Review"
        };
        Apply(c, input, isCreate: true);
        c.PlanJson = JsonSerializer.Serialize(input.Plan is { Count: > 0 } ? SanitizePlan(input.Plan, c) : DefaultPlan(start, end.DayNumber - start.DayNumber + 1), Json);
        _db.GrowthCampaigns.Add(c);
        Log(c.Id, null, "Info", "CampaignCreated", $"Tạo chiến dịch \"{c.Name}\" cho Page {page.PageName} ({start:dd/MM} – {end:dd/MM}).");
        await _db.SaveChangesAsync(ct);
        return await GetAsync(c.Id, ct);
    }

    public async Task<GrowthCampaignDto> UpdateAsync(Guid id, GrowthCampaignInputDto input, CancellationToken ct)
    {
        var c = await LoadAsync(id, ct);
        if (c.Status is GrowthStatus.Completed or GrowthStatus.Cancelled) throw new InvalidOperationException("Chiến dịch đã kết thúc — không sửa được nữa.");
        if (input.FacebookAccountId is { } pageId && pageId != c.FacebookAccountId)
        {
            if (c.Status != GrowthStatus.Draft) throw new InvalidOperationException("Chỉ đổi Page khi chiến dịch chưa bắt đầu.");
            if (await _db.GrowthScheduledContents.AnyAsync(x => x.CampaignId == id, ct))
                throw new InvalidOperationException("Chiến dịch đã có nội dung trong lịch — không đổi Page được (hãy tạo chiến dịch mới).");
            var page = await _db.FacebookAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == pageId, ct) ?? throw new ArgumentException("Không tìm thấy Page này.");
            c.FacebookAccountId = page.Id;
            c.PageName = page.PageName;
        }
        var start = c.StartDate;
        if (input.StartDate is { } s && s != c.StartDate)
        {
            if (c.Status != GrowthStatus.Draft) throw new InvalidOperationException("Chỉ đổi ngày bắt đầu khi chiến dịch chưa bắt đầu.");
            if (s < GrowthTime.Today(DateTime.UtcNow)) throw new ArgumentException("Ngày bắt đầu không được ở quá khứ.");
            start = s;
        }
        var end = input.EndDate ?? (input.Days is { } d ? start.AddDays(Math.Clamp(d, 1, MaxDays) - 1) : c.EndDate.AddDays(start.DayNumber - c.StartDate.DayNumber));
        ValidateDates(start, end);
        if (c.Status != GrowthStatus.Draft && end < GrowthTime.Today(DateTime.UtcNow)) throw new ArgumentException("Ngày kết thúc không được trước hôm nay.");
        var outside = await _db.GrowthScheduledContents.AnyAsync(x => x.CampaignId == id && x.ScheduledAtUtc != null
            && (x.Status == GrowthStatus.Scheduled || x.Status == GrowthStatus.PendingApproval)
            && (x.ScheduledAtUtc < GrowthTime.DayStartUtc(start) || x.ScheduledAtUtc >= GrowthTime.EndUtc(end)), ct);
        if (outside) throw new InvalidOperationException("Có nội dung đã lên lịch nằm ngoài khoảng ngày mới — hãy dời hoặc huỷ chúng trước.");
        c.StartDate = start;
        c.EndDate = end;
        if (input.Name != null) c.Name = Clean(input.Name, 200) is { Length: > 0 } n ? n : c.Name;
        if (input.TimeSlots != null) c.TimeSlots = string.Join(",", GrowthTime.NormalizeSlots(input.TimeSlots));
        Apply(c, input, isCreate: false);
        if (input.Plan != null) c.PlanJson = JsonSerializer.Serialize(SanitizePlan(input.Plan, c), Json);
        Log(c.Id, null, "Info", "CampaignUpdated", "Cập nhật thông tin chiến dịch.");
        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private static void ValidateDates(DateOnly start, DateOnly end)
    {
        if (end < start) throw new ArgumentException("Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.");
        if (end.DayNumber - start.DayNumber + 1 > MaxDays) throw new ArgumentException($"Chiến dịch dài tối đa {MaxDays} ngày.");
    }

    private static void Apply(GrowthCampaign c, GrowthCampaignInputDto input, bool isCreate)
    {
        if (input.TargetFollowers is { } tf) c.TargetFollowers = tf < 0 ? throw new ArgumentException("Mục tiêu follower không hợp lệ.") : tf == 0 ? null : tf;
        if (input.TargetViews is { } tv) c.TargetViews = tv < 0 ? throw new ArgumentException("Mục tiêu lượt xem không hợp lệ.") : tv == 0 ? null : tv;
        if (input.ReelsPerDay is { } r) c.ReelsPerDay = Range(r, 0, 5, "Số Reels mỗi ngày");
        if (input.PhotosPerDay is { } p) c.PhotosPerDay = Range(p, 0, 10, "Số bài ảnh mỗi ngày");
        if (input.StoriesPerDay is { } s) c.StoriesPerDay = Range(s, 0, 20, "Số Story mỗi ngày");
        if (input.AiBudgetUsd is { } b) c.AiBudgetUsd = b < 0 || b > 50 ? throw new ArgumentException("Ngân sách AI từ 0 đến 50 USD.") : Math.Round(b, 2);
        if (input.ApprovalMode != null)
            c.ApprovalMode = input.ApprovalMode is "Auto" or "Review" ? input.ApprovalMode : throw new ArgumentException("Chế độ đăng là Auto (tự động) hoặc Review (duyệt trước).");
        if (input.AlsoInstagram is { } ig) c.AlsoInstagram = ig;
        if (input.MetaOneCost is { } mc) c.MetaOneCost = mc < 0 ? throw new ArgumentException("Chi phí Meta One không hợp lệ.") : mc;
        if (input.MetaOneCurrency != null) c.MetaOneCurrency = input.MetaOneCurrency is "VND" or "USD" ? input.MetaOneCurrency : throw new ArgumentException("Tiền tệ là VND hoặc USD.");
        if (input.Notes != null) c.Notes = Clean(input.Notes, 4000);
        if (isCreate && string.IsNullOrEmpty(c.TimeSlots)) c.TimeSlots = "07:30,11:30,20:00";
    }

    private static int Range(int v, int min, int max, string what) =>
        v < min || v > max ? throw new ArgumentException($"{what} phải từ {min} đến {max}.") : v;

    private static List<GrowthPlanDayDto> SanitizePlan(List<GrowthPlanDayDto> plan, GrowthCampaign c)
    {
        var days = c.EndDate.DayNumber - c.StartDate.DayNumber + 1;
        return plan.Where(d => d.Day >= 1 && d.Day <= days).GroupBy(d => d.Day).Select(g => g.First()).OrderBy(d => d.Day)
            .Select(d => new GrowthPlanDayDto
            {
                Day = d.Day, Focus = Clean(d.Focus, 200) ?? "",
                Tasks = (d.Tasks ?? new()).Select(t => Clean(t, 300) ?? "").Where(t => t.Length > 0).Take(12).ToList()
            }).ToList();
    }

    public static string? Clean(string? s, int max)
    {
        if (s == null) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var c = await LoadAsync(id, ct);
        if (c.Status is not (GrowthStatus.Draft or GrowthStatus.Cancelled))
            throw new InvalidOperationException("Chỉ xoá được chiến dịch chưa bắt đầu hoặc đã huỷ.");
        if (await _db.GrowthScheduledContents.AnyAsync(x => x.CampaignId == id && x.Status == GrowthStatus.Published, ct))
            throw new InvalidOperationException("Chiến dịch đã có bài đăng — giữ lại để làm báo cáo.");
        _db.GrowthCampaigns.Remove(c);
        await _db.SaveChangesAsync(ct);
    }

    // ───────────────────────── lifecycle ─────────────────────────

    public async Task<GrowthCampaignDto> StartAsync(Guid id, CancellationToken ct)
    {
        var c = await LoadAsync(id, ct);
        if (c.Status != GrowthStatus.Draft) throw new InvalidOperationException("Chỉ bắt đầu được chiến dịch chưa chạy.");
        var page = c.FacebookAccountId is { } pid ? await _db.FacebookAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == pid, ct) : null;
        if (page == null || !page.IsActive || string.IsNullOrWhiteSpace(page.EncryptedAccessToken))
            throw new InvalidOperationException("Page của chiến dịch chưa kết nối hoặc đã ngắt kết nối. Vào Cài đặt → Facebook để kết nối lại.");
        var today = GrowthTime.Today(DateTime.UtcNow);
        if (c.EndDate < today) throw new InvalidOperationException("Chiến dịch đã qua ngày kết thúc — hãy sửa ngày trước khi bắt đầu.");
        if (await _db.GrowthCampaigns.AnyAsync(x => x.Id != id && x.FacebookAccountId == c.FacebookAccountId
                && (x.Status == GrowthStatus.Running || x.Status == GrowthStatus.Paused), ct))
            throw new InvalidOperationException("Page này đang có một chiến dịch khác chạy — kết thúc nó trước.");

        if (c.StartDate > today)
        {
            // starting early moves the window to today, keeping its length
            var length = c.EndDate.DayNumber - c.StartDate.DayNumber;
            c.StartDate = today;
            c.EndDate = today.AddDays(length);
        }
        c.Status = GrowthStatus.Running;
        c.StartedAtUtc = DateTime.UtcNow;
        var (followers, at, note) = await ReadFollowersAsync(page.Id, ct);
        c.StartingFollowers = followers;
        c.StartingFollowersAtUtc = at;
        Log(c.Id, null, "Info", "CampaignStarted", followers != null
            ? $"Bắt đầu chiến dịch. Follower ban đầu: {followers:N0}{note}."
            : $"Bắt đầu chiến dịch. Chưa đọc được follower ban đầu từ Meta{note}.");
        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Followers now from Meta (existing ReadFollowersAsync); else the latest daily snapshot the app stored, with its date.</summary>
    private async Task<(long? Followers, DateTime? AtUtc, string Note)> ReadFollowersAsync(Guid pageId, CancellationToken ct)
    {
        try
        {
            var (facebook, _) = await _facebook.ReadFollowersAsync(pageId, ct);
            if (facebook != null) return (facebook, DateTime.UtcNow, "");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation("Growth: reading followers failed: {Message}", GrowthGraphClient.Redact(ex.Message));
        }
        var snap = await _db.PageFollowerSnapshots.AsNoTracking().Where(s => s.FacebookAccountId == pageId)
            .OrderByDescending(s => s.Date).FirstOrDefaultAsync(ct);
        return snap != null && snap.Date >= GrowthTime.Today(DateTime.UtcNow).AddDays(-1)
            ? (snap.FacebookFollowers, snap.CollectedAtUtc, $" (bản ghi ngày {snap.Date:dd/MM})")
            : (null, null, "");
    }

    public async Task<GrowthCampaignDto> PauseAsync(Guid id, CancellationToken ct)
    {
        var c = await LoadAsync(id, ct);
        if (c.Status != GrowthStatus.Running) throw new InvalidOperationException("Chỉ tạm dừng được chiến dịch đang chạy.");
        c.Status = GrowthStatus.Paused;
        Log(c.Id, null, "Info", "CampaignPaused", "Tạm dừng — không bài nào được đăng cho tới khi tiếp tục.");
        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<GrowthCampaignDto> ResumeAsync(Guid id, CancellationToken ct)
    {
        var c = await LoadAsync(id, ct);
        if (c.Status != GrowthStatus.Paused) throw new InvalidOperationException("Chỉ tiếp tục được chiến dịch đang tạm dừng.");
        if (DateTime.UtcNow >= GrowthTime.EndUtc(c.EndDate)) throw new InvalidOperationException("Chiến dịch đã qua ngày kết thúc — hãy kết thúc nó.");
        c.Status = GrowthStatus.Running;
        Log(c.Id, null, "Info", "CampaignResumed", "Tiếp tục chiến dịch.");
        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<GrowthCampaignDto> CompleteAsync(Guid id, string actor, CancellationToken ct)
    {
        var c = await LoadAsync(id, ct);
        if (c.Status is not (GrowthStatus.Running or GrowthStatus.Paused)) throw new InvalidOperationException("Chỉ kết thúc được chiến dịch đang chạy hoặc tạm dừng.");
        await FinishAsync(c, GrowthStatus.Completed, actor, ct);
        return await GetAsync(id, ct);
    }

    public async Task<GrowthCampaignDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var c = await LoadAsync(id, ct);
        if (c.Status is GrowthStatus.Completed or GrowthStatus.Cancelled) throw new InvalidOperationException("Chiến dịch đã kết thúc.");
        await FinishAsync(c, GrowthStatus.Cancelled, "user", ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Ends the campaign: nothing still waiting is posted afterwards, the final follower count is read.</summary>
    private async Task FinishAsync(GrowthCampaign c, string status, string actor, CancellationToken ct)
    {
        var wasRunning = c.Status is GrowthStatus.Running or GrowthStatus.Paused;
        c.Status = status;
        c.EndedAtUtc = DateTime.UtcNow;
        var waiting = await _db.GrowthScheduledContents
            .Where(x => x.CampaignId == c.Id && (x.Status == GrowthStatus.Scheduled || x.Status == GrowthStatus.PendingApproval))
            .ToListAsync(ct);
        foreach (var w in waiting)
        {
            w.Status = GrowthStatus.Cancelled;
            w.LastError = "Chiến dịch đã kết thúc trước giờ đăng.";
        }
        if (wasRunning && c.FacebookAccountId is { } pid)
        {
            var (followers, at, _) = await ReadFollowersAsync(pid, ct);
            c.EndingFollowers = followers;
            c.EndingFollowersAtUtc = at;
        }
        Log(c.Id, null, "Info", status == GrowthStatus.Completed ? "CampaignCompleted" : "CampaignCancelled",
            (status == GrowthStatus.Completed ? "Kết thúc chiến dịch." : "Huỷ chiến dịch.")
            + (waiting.Count > 0 ? $" {waiting.Count} nội dung chưa đăng đã được huỷ." : "")
            + (c.EndingFollowers is { } ef ? $" Follower cuối kỳ: {ef:N0}." : ""), actor);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Scheduler: running campaigns past their last day are completed (nothing is posted after the end).</summary>
    public async Task<List<Guid>> CompleteExpiredAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var running = await _db.GrowthCampaigns.Where(c => c.Status == GrowthStatus.Running || c.Status == GrowthStatus.Paused).ToListAsync(ct);
        var done = new List<Guid>();
        foreach (var c in running.Where(c => now >= GrowthTime.EndUtc(c.EndDate)))
        {
            await FinishAsync(c, GrowthStatus.Completed, "scheduler", ct);
            done.Add(c.Id);
        }
        return done;
    }

    // ───────────────────────── checklist & manual numbers ─────────────────────────

    public async Task<GrowthCampaignDto> SetChecklistAsync(Guid id, GrowthChecklistUpdateDto input, CancellationToken ct)
    {
        var c = await LoadAsync(id, ct);
        if (ChecklistItems.All(i => i.Key != input.Key)) throw new ArgumentException("Mục checklist không tồn tại.");
        var state = ReadChecklist(c.ChecklistJson);
        state[input.Key] = new CheckState { Done = input.Done, Note = Clean(input.Note, 500), AtUtc = DateTime.UtcNow };
        c.ChecklistJson = JsonSerializer.Serialize(state, Json);
        Log(c.Id, null, "Info", "ChecklistUpdated", $"{ChecklistItems.First(i => i.Key == input.Key).Label}: {(input.Done ? "đã xong" : "chưa")}.");
        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<GrowthCampaignDto> SetManualMetricsAsync(Guid id, GrowthManualMetricsDto input, CancellationToken ct)
    {
        var c = await LoadAsync(id, ct);
        if (c.FacebookAccountId is not { } pid) throw new InvalidOperationException("Page của chiến dịch đã bị xoá khỏi ứng dụng.");
        if (input.Date < c.StartDate.AddDays(-7) || input.Date > c.EndDate) throw new ArgumentException("Ngày nằm ngoài chiến dịch (cho phép 7 ngày trước khi bắt đầu).");
        if (input.Date > GrowthTime.Today(DateTime.UtcNow)) throw new ArgumentException("Không nhập số liệu cho ngày chưa tới.");
        foreach (var v in new[] { input.InvitesSent, input.InvitesAccepted, input.PageVisits, input.NewFollowers })
            if (v is < 0) throw new ArgumentException("Số liệu không được âm.");
        if (input.InvitesAccepted is { } acc && input.InvitesSent is { } sent && acc > sent)
            throw new ArgumentException("Số lời mời được chấp nhận không thể lớn hơn số lời mời đã gửi.");
        var row = await _db.GrowthDailyMetrics.FirstOrDefaultAsync(m => m.FacebookAccountId == pid && m.Date == input.Date, ct);
        if (row == null)
        {
            row = new GrowthDailyMetric { FacebookAccountId = pid, Date = input.Date };
            _db.GrowthDailyMetrics.Add(row);
        }
        row.ManualInvitesSent = input.InvitesSent;
        row.ManualInvitesAccepted = input.InvitesAccepted;
        row.ManualPageVisits = input.PageVisits;
        row.ManualNewFollowers = input.NewFollowers;
        row.ManualNote = Clean(input.Note, 1000);
        Log(c.Id, null, "Info", "ManualMetrics", $"Nhập tay số liệu ngày {input.Date:dd/MM}.");
        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<List<GrowthActivityLogDto>> LogsAsync(Guid id, CancellationToken ct)
    {
        await LoadAsync(id, ct);
        return await _db.GrowthActivityLogs.AsNoTracking().Where(l => l.CampaignId == id).OrderByDescending(l => l.CreatedAtUtc).Take(300)
            .Select(l => new GrowthActivityLogDto { Id = l.Id, ContentId = l.ContentId, Level = l.Level, Action = l.Action, Message = l.Message, Actor = l.Actor, CreatedAtUtc = l.CreatedAtUtc })
            .ToListAsync(ct);
    }

    private void Log(Guid campaignId, Guid? contentId, string level, string action, string message, string actor = "user") =>
        _db.GrowthActivityLogs.Add(new GrowthActivityLog
        {
            CampaignId = campaignId, ContentId = contentId, Level = level, Action = action,
            Message = message.Length > 2000 ? message[..2000] : message, Actor = actor
        });
}

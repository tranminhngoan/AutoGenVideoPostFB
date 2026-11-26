using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Infrastructure.Growth;

/// <summary>
/// End-of-campaign report: what happened (Meta's numbers only), what it cost, and whether Meta One looks worth keeping.
/// The verdict compares the campaign with the week before it; it never claims Meta One caused the growth.
/// </summary>
public class GrowthReportService
{
    public const string Continue = "Continue";
    public const string TestMore = "TestMore";
    public const string DontBuy = "DontBuy";
    public const string NotEnoughData = "NotEnoughData";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly GrowthCampaignService _campaigns;
    private readonly GrowthInsightsService _insights;
    private readonly GrowthAiService _ai;

    public GrowthReportService(IApplicationDbContext db, GrowthCampaignService campaigns, GrowthInsightsService insights, GrowthAiService ai)
    {
        _db = db;
        _campaigns = campaigns;
        _insights = insights;
        _ai = ai;
    }

    public static string VerdictLabel(string v) => v switch
    {
        Continue => "Nên tiếp tục mua",
        TestMore => "Nên thử nghiệm thêm",
        DontBuy => "Chưa nên mua",
        _ => "Chưa đủ dữ liệu để kết luận"
    };

    /// <summary>
    /// Verdict from followers gained per day before vs during the campaign (Meta's daily follows when available, else the
    /// follower count difference over the campaign).
    /// </summary>
    public static (string Verdict, string Reason) Decide(GrowthPeriodDto? before, GrowthPeriodDto? during, long? totalGain, int campaignDays)
    {
        const string caveat = " Follower tăng có thể đến từ nội dung, thời điểm và xu hướng — không thể quy toàn bộ cho Meta One.";
        if (totalGain == null && during?.NewFollows == null)
            return (NotEnoughData, "Chưa đọc được follower đầu/cuối kỳ hay số theo dõi mới theo ngày từ Meta.");
        double? perDayBefore = before is { NewFollows: { } bn, DaysWithData: > 0 } ? bn / (double)before.DaysWithData : null;
        double? perDayDuring = during is { NewFollows: { } dn, DaysWithData: > 0 } ? dn / (double)during.DaysWithData
            : totalGain is { } g && campaignDays > 0 ? g / (double)campaignDays : null;
        if (perDayDuring == null) return (NotEnoughData, "Không có số follower mới theo ngày trong chiến dịch.");
        if (perDayDuring <= 0) return (DontBuy, $"Follower không tăng trong chiến dịch ({perDayDuring:0.#}/ngày).{caveat}");
        if (perDayBefore == null)
            return (TestMore, $"Follower tăng {perDayDuring:0.#}/ngày nhưng không có số liệu tuần trước để so sánh — nên thử thêm một kỳ có đối chứng.{caveat}");
        if (perDayBefore <= 0)
            return (perDayDuring >= 1 ? Continue : TestMore,
                $"Trước chiến dịch Page không tăng follower; trong chiến dịch tăng {perDayDuring:0.#}/ngày.{caveat}");
        var ratio = perDayDuring.Value / perDayBefore.Value;
        if (ratio >= 1.5) return (Continue, $"Follower mới/ngày gấp {ratio:0.#} lần tuần trước ({perDayBefore:0.#} → {perDayDuring:0.#}).{caveat}");
        if (ratio > 1.05) return (TestMore, $"Follower mới/ngày tăng nhẹ so với tuần trước ({perDayBefore:0.#} → {perDayDuring:0.#}, ×{ratio:0.##}).{caveat}");
        return (DontBuy, $"Follower mới/ngày không cao hơn tuần trước ({perDayBefore:0.#} → {perDayDuring:0.#}).{caveat}");
    }

    public async Task<GrowthReportDto?> LatestAsync(Guid campaignId, CancellationToken ct)
    {
        await _campaigns.LoadAsync(campaignId, ct);
        var r = await _db.GrowthCampaignReports.AsNoTracking().Where(x => x.CampaignId == campaignId).OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        return r == null ? null : ToDto(r);
    }

    private static GrowthReportDto ToDto(GrowthCampaignReport r)
    {
        var data = JsonSerializer.Deserialize<GrowthReportDataDto>(r.DataJson, Json) ?? new();
        var (_, reason) = Decide(data.Before, data.During, data.FollowerGain, Math.Max(1, data.EndDate.DayNumber - data.StartDate.DayNumber + 1));
        return new GrowthReportDto
        {
            Id = r.Id, CampaignId = r.CampaignId, CreatedAtUtc = r.CreatedAtUtc, Verdict = r.Verdict, VerdictLabel = VerdictLabel(r.Verdict),
            VerdictReason = reason, AiCommentary = r.AiCommentary, Data = data
        };
    }

    [Hangfire.AutomaticRetry(Attempts = 0)]
    public async Task<GrowthReportDto> GenerateAsync(Guid campaignId, bool withAi, CancellationToken ct)
    {
        var c = await _campaigns.LoadAsync(campaignId, ct);
        var o = await _insights.OverviewAsync(campaignId, ct);
        var posts = (await _insights.PostsAsync(campaignId, ct)).Where(p => p.Kind != GrowthKinds.Story).ToList();
        var ranking = GrowthInsightsService.Rank(posts);
        var hours = await _insights.HoursAsync(campaignId, ct);
        double? Kpi(string key) => o.Kpis.FirstOrDefault(k => k.Key == key)?.Value;
        long? L(string key) => Kpi(key) is { } v ? (long)v : null;

        var gain = L("new_followers");
        var items = await _db.GrowthScheduledContents.AsNoTracking().Where(x => x.CampaignId == c.Id && x.Status == GrowthStatus.Published && x.SourceId != null).ToListAsync(ct);
        var studioIds = items.Where(i => i.SourceType == GrowthSources.StudioVideo).Select(i => i.SourceId!.Value).Distinct().ToList();
        var aiIds = items.Where(i => i.SourceType == GrowthSources.AiStudioProject).Select(i => i.SourceId!.Value).Distinct().ToList();
        var videoCost = await _db.StudioVideos.AsNoTracking().Where(v => studioIds.Contains(v.Id)).SumAsync(v => (decimal?)v.ActualCostUsd, ct) ?? 0m;
        videoCost += await _db.AiStudioGenerations.AsNoTracking().Where(g => aiIds.Contains(g.ProjectId)).SumAsync(g => (decimal?)g.CostUsd, ct) ?? 0m;
        var contentCost = Math.Round(c.AiSpentUsd + videoCost, 4);

        var checklist = GrowthCampaignService.Checklist(c);
        var data = new GrowthReportDataDto
        {
            CampaignName = c.Name, PageName = o.Page?.PageName ?? c.PageName, PageId = o.Page?.PageId, StartDate = c.StartDate, EndDate = c.EndDate,
            Status = c.Status, FollowersStart = c.StartingFollowers, FollowersEnd = c.EndingFollowers ?? o.CurrentFollowers, FollowerGain = gain,
            GrowthPercent = Kpi("growth_rate"), TotalViews = L("views"), TotalReach = L("reach"), TotalEngagement = L("engagement"),
            TotalPublished = (int)(Kpi("posts") ?? 0), ByKind = o.ByKind,
            MetaOneInvitesSent = o.Daily.Where(d => d.Date >= c.StartDate).Select(d => d.ManualInvitesSent).Aggregate((long?)null, (a, b) => b == null ? a : (a ?? 0) + b),
            MetaOneInvitesAccepted = o.Daily.Where(d => d.Date >= c.StartDate).Select(d => d.ManualInvitesAccepted).Aggregate((long?)null, (a, b) => b == null ? a : (a ?? 0) + b),
            MetaOneChecklist = checklist,
            Top = posts.Where(p => p.Views != null || p.Engagement != null).OrderByDescending(p => p.Views ?? 0).ThenByDescending(p => p.Engagement ?? 0).Take(5).ToList(),
            Weak = ranking.Weakest, BestSlots = hours.SuggestedSlots,
            BestSlotsBasis = hours.EnoughData ? hours.Basis : $"Chưa đủ dữ liệu ({hours.Samples}/{hours.MinSamples} bài có số liệu).",
            AiCostUsd = Math.Round(c.AiSpentUsd, 4), VideoCostUsd = Math.Round(videoCost, 4), ContentCostUsd = contentCost,
            MetaOneCost = c.MetaOneCost, MetaOneCurrency = c.MetaOneCurrency,
            ContentCostPerFollowerUsd = gain is > 0 ? Math.Round(contentCost / gain.Value, 4) : null,
            MetaOneCostPerFollower = gain is > 0 && c.MetaOneCost is { } mc ? Math.Round(mc / gain.Value, 2) : null,
            Before = o.Before, During = o.During
        };
        data.DataGaps = Gaps(c, o, data);
        data.Recommendations = Recommend(o, data, hours);
        var days = c.EndDate.DayNumber - c.StartDate.DayNumber + 1;
        var (verdict, reason) = Decide(o.Before, o.During, gain, days);

        string? commentary = null;
        if (withAi)
        {
            var facts = new StringBuilder()
                .AppendLine($"Chiến dịch {c.Name}, Page {data.PageName}, {c.StartDate:dd/MM}–{c.EndDate:dd/MM}.")
                .AppendLine($"Follower: {Fmt(data.FollowersStart)} → {Fmt(data.FollowersEnd)} (tăng {Fmt(gain)}, {Fmt(data.GrowthPercent)}%).")
                .AppendLine($"Trước chiến dịch 7 ngày: follow mới {Fmt(o.Before?.NewFollows)}; trong chiến dịch: {Fmt(o.During?.NewFollows)}.")
                .AppendLine($"Lượt xem {Fmt(data.TotalViews)}, reach {Fmt(data.TotalReach)}, tương tác {Fmt(data.TotalEngagement)}, {data.TotalPublished} bài.")
                .AppendLine("Theo loại: " + string.Join("; ", data.ByKind.Select(k => $"{k.Kind}: {k.Published} bài, view TB {Fmt(k.AvgViews)}, tương tác TB {Fmt(k.AvgEngagement)}")))
                .AppendLine($"Meta One: lời mời {Fmt(data.MetaOneInvitesSent)}, chấp nhận {Fmt(data.MetaOneInvitesAccepted)}, chi phí {(c.MetaOneCost is { } m ? $"{m:N0} {c.MetaOneCurrency}" : "chưa nhập")}.")
                .AppendLine($"Chi phí nội dung {contentCost:0.00} USD.")
                .AppendLine($"Đánh giá theo quy tắc: {VerdictLabel(verdict)} — {reason}")
                .ToString();
            commentary = await _ai.ReportCommentaryAsync(c, facts, ct);
        }

        var report = new GrowthCampaignReport { CampaignId = c.Id, DataJson = JsonSerializer.Serialize(data, Json), Verdict = verdict, AiCommentary = commentary };
        _db.GrowthCampaignReports.Add(report);
        _db.GrowthActivityLogs.Add(new GrowthActivityLog { CampaignId = c.Id, Action = "ReportGenerated", Message = $"Tạo báo cáo — {VerdictLabel(verdict)}.", Actor = withAi ? "user" : "system" });
        await _db.SaveChangesAsync(ct);
        return ToDto(report);
    }

    private static List<string> Gaps(GrowthCampaign c, GrowthOverviewDto o, GrowthReportDataDto d)
    {
        var gaps = new List<string>();
        if (d.FollowersStart == null) gaps.Add("Không có follower đầu kỳ (Meta không trả về khi bắt đầu).");
        if (c.MetaOneCost == null) gaps.Add("Chưa nhập chi phí Meta One — không tính được chi phí/follower của Meta One.");
        if (d.MetaOneInvitesSent == null) gaps.Add("Chưa nhập số lời mời theo dõi của Meta One (Meta không có API cho số này).");
        foreach (var a in o.Campaign.MetricAvailability.Where(a => !a.Ok)) gaps.Add($"Meta không trả về {a.Metric}: {a.Error}");
        if (o.Before is not { DaysWithData: > 0 }) gaps.Add("Không có số liệu 7 ngày trước chiến dịch để đối chứng.");
        if (d.ByKind.Any(k => k.Kind == GrowthKinds.Story)) gaps.Add("Meta không cung cấp số liệu Story của Page qua API — hiệu quả Story chỉ tính số lượng đã đăng.");
        return gaps;
    }

    private static List<string> Recommend(GrowthOverviewDto o, GrowthReportDataDto d, GrowthHoursDto hours)
    {
        var list = new List<string>();
        var best = d.ByKind.Where(k => k.WithMetrics >= 2 && k.AvgEngagement != null).OrderByDescending(k => k.AvgEngagement).FirstOrDefault();
        if (best != null) list.Add($"Ưu tiên {GrowthContentService.KindLabel(best.Kind)}: tương tác trung bình cao nhất ({best.AvgEngagement:0.#}/bài).");
        if (hours.EnoughData && hours.SuggestedSlots.Count > 0) list.Add($"Đăng vào {string.Join(", ", hours.SuggestedSlots)} — khung giờ có lượt xem trung bình cao nhất ({hours.Basis}).");
        else list.Add("Tiếp tục thử nhiều khung giờ để có đủ dữ liệu chọn giờ đăng.");
        var styles = d.Top.Where(p => p.Style != null).GroupBy(p => p.Style!).OrderByDescending(g => g.Count()).FirstOrDefault();
        if (styles is { } s && s.Count() >= 2) list.Add($"Phong cách caption xuất hiện nhiều nhất trong top bài: {s.Key}.");
        if (d.Weak.Count > 0) list.Add($"Xem lại kiểu nội dung của các bài yếu nhất (ví dụ \"{(d.Weak[0].Title.Length > 0 ? d.Weak[0].Title : d.Weak[0].CaptionExcerpt)}\").");
        if (d.MetaOneInvitesSent is > 0 && d.MetaOneInvitesAccepted is { } acc)
            list.Add($"Tỷ lệ chấp nhận lời mời Meta One: {acc * 100.0 / d.MetaOneInvitesSent.Value:0.#}%.");
        list.Add("Tháng tới: giữ 1 video AI/ngày, nhân bản chủ đề tốt nhất thành ảnh và Story, đo lại với cùng cách tính để so sánh.");
        return list;
    }

    private static string Fmt(long? v) => v?.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) ?? "chưa có";
    private static string Fmt(double? v) => v?.ToString("0.##", CultureInfo.InvariantCulture) ?? "chưa có";

    // ───────────────────────── exports ─────────────────────────

    public static List<(string Section, string Item, string Value)> SummaryRows(GrowthReportDto r)
    {
        var d = r.Data;
        string N(long? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "Chưa có dữ liệu";
        string D(double? v) => v?.ToString("0.##", CultureInfo.InvariantCulture) ?? "Chưa có dữ liệu";
        string M(decimal? v) => v?.ToString("0.####", CultureInfo.InvariantCulture) ?? "Chưa có dữ liệu";
        var rows = new List<(string, string, string)>
        {
            ("Thông tin", "Chiến dịch", d.CampaignName), ("Thông tin", "Page", d.PageName), ("Thông tin", "Page ID", d.PageId ?? ""),
            ("Thông tin", "Thời gian", $"{d.StartDate:dd/MM/yyyy} - {d.EndDate:dd/MM/yyyy}"), ("Thông tin", "Trạng thái", d.Status),
            ("Follower", "Đầu kỳ", N(d.FollowersStart)), ("Follower", "Cuối kỳ", N(d.FollowersEnd)), ("Follower", "Tăng", N(d.FollowerGain)),
            ("Follower", "Tỷ lệ tăng (%)", D(d.GrowthPercent)),
            ("Hiệu quả", "Tổng lượt xem", N(d.TotalViews)), ("Hiệu quả", "Tổng reach", N(d.TotalReach)), ("Hiệu quả", "Tổng tương tác", N(d.TotalEngagement)),
            ("Hiệu quả", "Tổng số bài đã đăng", d.TotalPublished.ToString(CultureInfo.InvariantCulture)),
        };
        foreach (var k in d.ByKind)
            rows.Add(("Theo loại", k.Kind, $"{k.Published} bài; view {N(k.Views)}; tương tác {N(k.Engagement)}; view TB {D(k.AvgViews)}"));
        rows.Add(("Meta One", "Lời mời đã gửi (nhập tay)", N(d.MetaOneInvitesSent)));
        rows.Add(("Meta One", "Lời mời được chấp nhận (nhập tay)", N(d.MetaOneInvitesAccepted)));
        foreach (var i in d.MetaOneChecklist) rows.Add(("Meta One", i.Label, i.Done ? "Đã xong" : "Chưa"));
        rows.Add(("Trước/Trong", "Follow mới 7 ngày trước", N(d.Before?.NewFollows)));
        rows.Add(("Trước/Trong", "Follow mới trong chiến dịch", N(d.During?.NewFollows)));
        rows.Add(("Chi phí", "AI (USD)", M(d.AiCostUsd)));
        rows.Add(("Chi phí", "Video AI (USD)", M(d.VideoCostUsd)));
        rows.Add(("Chi phí", "Sản xuất nội dung (USD)", M(d.ContentCostUsd)));
        rows.Add(("Chi phí", $"Meta One ({d.MetaOneCurrency})", M(d.MetaOneCost)));
        rows.Add(("Chi phí", "Nội dung / follower mới (USD)", M(d.ContentCostPerFollowerUsd)));
        rows.Add(("Chi phí", $"Meta One / follower mới ({d.MetaOneCurrency})", M(d.MetaOneCostPerFollower)));
        rows.Add(("Giờ đăng", "Khung giờ tốt nhất", d.BestSlots.Count > 0 ? string.Join(" ", d.BestSlots) : d.BestSlotsBasis));
        rows.Add(("Đánh giá", "Meta One", r.VerdictLabel));
        rows.Add(("Đánh giá", "Lý do", r.VerdictReason));
        if (!string.IsNullOrWhiteSpace(r.AiCommentary)) rows.Add(("Đánh giá", "Nhận xét AI", r.AiCommentary!));
        foreach (var rec in d.Recommendations) rows.Add(("Đề xuất", "", rec));
        foreach (var g in d.DataGaps) rows.Add(("Thiếu dữ liệu", "", g));
        return rows;
    }

    public static string Csv(GrowthReportDto r, IReadOnlyList<GrowthPostAnalyticsDto> posts)
    {
        static string Q(string? s)
        {
            s ??= "";
            // spreadsheet formula injection: text starting with = + - @ is kept as text
            if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var sb = new StringBuilder();
        sb.AppendLine("Mục,Chỉ số,Giá trị");
        foreach (var (section, item, value) in SummaryRows(r)) sb.AppendLine($"{Q(section)},{Q(item)},{Q(value)}");
        sb.AppendLine();
        sb.AppendLine("Loại,Tiêu đề / nội dung,Ngày,Giờ,Views,Reach,Cảm xúc,Bình luận,Chia sẻ,Tương tác,Follow,Liên kết");
        foreach (var p in posts)
            sb.AppendLine(string.Join(",", Q(p.Kind), Q(p.Title.Length > 0 ? p.Title : p.CaptionExcerpt), Q(p.LocalDate), Q(p.LocalTime),
                Q(p.Views?.ToString()), Q(p.Reach?.ToString()), Q(p.Reactions?.ToString()), Q(p.Comments?.ToString()), Q(p.Shares?.ToString()),
                Q(p.Engagement?.ToString()), Q(p.Follows?.ToString()), Q(p.PermalinkUrl)));
        return sb.ToString();
    }

    /// <summary>Excel 2003 XML workbook (opens in Excel / LibreOffice; the project has no xlsx library).</summary>
    public static string SpreadsheetXml(GrowthReportDto r, IReadOnlyList<GrowthPostAnalyticsDto> posts)
    {
        static string X(string? s) => SecurityElement.Escape(s ?? "") ?? "";
        static string Cell(string? s) => $"<Cell><Data ss:Type=\"String\">{X(s)}</Data></Cell>";
        static string NumCell(long? v) => v == null ? "<Cell/>" : $"<Cell><Data ss:Type=\"Number\">{v.Value.ToString(CultureInfo.InvariantCulture)}</Data></Cell>";
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<?mso-application progid=\"Excel.Sheet\"?>\n");
        sb.Append("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">\n");
        sb.Append("<Styles><Style ss:ID=\"h\"><Font ss:Bold=\"1\"/></Style></Styles>\n");
        sb.Append("<Worksheet ss:Name=\"Tong ket\"><Table>\n<Row ss:StyleID=\"h\">").Append(Cell("Mục")).Append(Cell("Chỉ số")).Append(Cell("Giá trị")).Append("</Row>\n");
        foreach (var (section, item, value) in SummaryRows(r)) sb.Append("<Row>").Append(Cell(section)).Append(Cell(item)).Append(Cell(value)).Append("</Row>\n");
        sb.Append("</Table></Worksheet>\n<Worksheet ss:Name=\"Bai dang\"><Table>\n<Row ss:StyleID=\"h\">");
        foreach (var h in new[] { "Loại", "Tiêu đề / nội dung", "Ngày", "Giờ", "Views", "Reach", "Cảm xúc", "Bình luận", "Chia sẻ", "Tương tác", "Follow", "Liên kết" }) sb.Append(Cell(h));
        sb.Append("</Row>\n");
        foreach (var p in posts)
            sb.Append("<Row>").Append(Cell(p.Kind)).Append(Cell(p.Title.Length > 0 ? p.Title : p.CaptionExcerpt)).Append(Cell(p.LocalDate)).Append(Cell(p.LocalTime))
                .Append(NumCell(p.Views)).Append(NumCell(p.Reach)).Append(NumCell(p.Reactions)).Append(NumCell(p.Comments)).Append(NumCell(p.Shares))
                .Append(NumCell(p.Engagement)).Append(NumCell(p.Follows)).Append(Cell(p.PermalinkUrl)).Append("</Row>\n");
        sb.Append("</Table></Worksheet>\n</Workbook>\n");
        return sb.ToString();
    }

    public async Task<(GrowthReportDto Report, List<GrowthPostAnalyticsDto> Posts)> ExportDataAsync(Guid campaignId, CancellationToken ct)
    {
        var r = await LatestAsync(campaignId, ct) ?? await GenerateAsync(campaignId, false, ct);
        var posts = await _insights.PostsAsync(campaignId, ct);
        return (r, posts);
    }
}

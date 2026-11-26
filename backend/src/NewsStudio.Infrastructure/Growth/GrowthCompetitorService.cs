using System.Text;
using Microsoft.EntityFrameworkCore;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Infrastructure.Growth;

/// <summary>
/// Competitor Pages the owner watches (at most 5 per Page). Nothing is fetched from them: no scraping, no access to
/// their insights — the owner types in what is publicly visible, and the analysis summarises those notes.
/// </summary>
public class GrowthCompetitorService
{
    public const int MaxPerPage = 5;
    public static readonly string[] Formats = { "Reel", "Photo", "Album", "Story", "Text", "Live", "Video" };

    private readonly IApplicationDbContext _db;
    private readonly GrowthCampaignService _campaigns;
    private readonly GrowthAiService _ai;

    public GrowthCompetitorService(IApplicationDbContext db, GrowthCampaignService campaigns, GrowthAiService ai)
    {
        _db = db;
        _campaigns = campaigns;
        _ai = ai;
    }

    private static GrowthCompetitorSnapshotDto SnapDto(GrowthCompetitorSnapshot s) => new()
    {
        Id = s.Id, Date = s.Date, Followers = s.Followers, PostsPerWeek = s.PostsPerWeek, PostUrl = s.PostUrl, PostTitle = s.PostTitle, Format = s.Format,
        Topic = s.Topic, Reactions = s.Reactions, Comments = s.Comments, Shares = s.Shares, Views = s.Views, Note = s.Note
    };

    private static GrowthCompetitorDto Dto(GrowthCompetitor c)
    {
        var snaps = c.Snapshots.OrderByDescending(s => s.Date).ThenByDescending(s => s.CreatedAtUtc).ToList();
        return new GrowthCompetitorDto
        {
            Id = c.Id, FacebookAccountId = c.FacebookAccountId, Name = c.Name, Url = c.Url, Niche = c.Niche, Notes = c.Notes, CreatedAtUtc = c.CreatedAtUtc,
            Snapshots = snaps.Take(100).Select(SnapDto).ToList(),
            LatestFollowers = snaps.FirstOrDefault(s => s.Followers != null)?.Followers,
            LatestPostsPerWeek = snaps.FirstOrDefault(s => s.PostsPerWeek != null)?.PostsPerWeek
        };
    }

    public async Task<List<GrowthCompetitorDto>> ListAsync(Guid facebookAccountId, CancellationToken ct)
    {
        var list = await _db.GrowthCompetitors.AsNoTracking().Include(c => c.Snapshots).Where(c => c.FacebookAccountId == facebookAccountId)
            .OrderBy(c => c.CreatedAtUtc).ToListAsync(ct);
        return list.Select(Dto).ToList();
    }

    private async Task<GrowthCompetitor> LoadAsync(Guid id, Guid facebookAccountId, CancellationToken ct) =>
        await _db.GrowthCompetitors.Include(c => c.Snapshots).FirstOrDefaultAsync(c => c.Id == id && c.FacebookAccountId == facebookAccountId, ct)
        ?? throw new KeyNotFoundException("Không tìm thấy đối thủ của Page này.");

    /// <summary>Only an https facebook.com / fb.com / fb.watch address is accepted (it is stored, never fetched).</summary>
    public static string ValidateUrl(string? url)
    {
        var u = (url ?? "").Trim();
        if (u.Length == 0) throw new ArgumentException("Hãy nhập URL Page đối thủ.");
        if (!u.StartsWith("http", StringComparison.OrdinalIgnoreCase)) u = "https://" + u;
        if (u.Length > 500 || !GrowthContentService.IsFacebookUrl(u)) throw new ArgumentException("URL phải là địa chỉ Page trên facebook.com (https).");
        return u;
    }

    public async Task<GrowthCompetitorDto> CreateAsync(GrowthCompetitorInputDto input, CancellationToken ct)
    {
        if (input.FacebookAccountId is not { } pid || !await _db.FacebookAccounts.AnyAsync(a => a.Id == pid, ct))
            throw new ArgumentException("Hãy chọn Page của bạn.");
        if (await _db.GrowthCompetitors.CountAsync(c => c.FacebookAccountId == pid, ct) >= MaxPerPage)
            throw new InvalidOperationException($"Mỗi Page theo dõi tối đa {MaxPerPage} đối thủ.");
        var name = GrowthCampaignService.Clean(input.Name, 200);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Hãy nhập tên Page đối thủ.");
        var url = ValidateUrl(input.Url);
        if (await _db.GrowthCompetitors.AnyAsync(c => c.FacebookAccountId == pid && c.Url == url, ct)) throw new ArgumentException("Đối thủ này đã có trong danh sách.");
        var c = new GrowthCompetitor
        {
            FacebookAccountId = pid, Name = name, Url = url, Niche = GrowthCampaignService.Clean(input.Niche, 200), Notes = GrowthCampaignService.Clean(input.Notes, 4000)
        };
        _db.GrowthCompetitors.Add(c);
        await _db.SaveChangesAsync(ct);
        return Dto(c);
    }

    public async Task<GrowthCompetitorDto> UpdateAsync(Guid id, GrowthCompetitorInputDto input, CancellationToken ct)
    {
        if (input.FacebookAccountId is not { } pid) throw new ArgumentException("Thiếu Page.");
        var c = await LoadAsync(id, pid, ct);
        if (input.Name != null) c.Name = GrowthCampaignService.Clean(input.Name, 200) is { Length: > 0 } n ? n : throw new ArgumentException("Tên không được trống.");
        if (input.Url != null) c.Url = ValidateUrl(input.Url);
        if (input.Niche != null) c.Niche = GrowthCampaignService.Clean(input.Niche, 200);
        if (input.Notes != null) c.Notes = GrowthCampaignService.Clean(input.Notes, 4000);
        await _db.SaveChangesAsync(ct);
        return Dto(c);
    }

    public async Task DeleteAsync(Guid id, Guid facebookAccountId, CancellationToken ct)
    {
        var c = await LoadAsync(id, facebookAccountId, ct);
        _db.GrowthCompetitors.Remove(c);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<GrowthCompetitorDto> AddSnapshotAsync(Guid id, Guid facebookAccountId, GrowthCompetitorSnapshotInputDto input, CancellationToken ct)
    {
        var c = await LoadAsync(id, facebookAccountId, ct);
        foreach (var v in new[] { input.Followers, input.Reactions, input.Comments, input.Shares, input.Views })
            if (v is < 0) throw new ArgumentException("Số liệu không được âm.");
        if (input.PostsPerWeek is < 0 or > 500) throw new ArgumentException("Số bài/tuần không hợp lệ.");
        if (input.Format != null && !Formats.Contains(input.Format)) throw new ArgumentException("Kiểu nội dung không hợp lệ.");
        var date = input.Date ?? GrowthTime.Today(DateTime.UtcNow);
        if (date > GrowthTime.Today(DateTime.UtcNow)) throw new ArgumentException("Không nhập cho ngày chưa tới.");
        var s = new GrowthCompetitorSnapshot
        {
            CompetitorId = c.Id, Date = date, Followers = input.Followers, PostsPerWeek = input.PostsPerWeek,
            PostUrl = string.IsNullOrWhiteSpace(input.PostUrl) ? null : ValidateUrl(input.PostUrl), PostTitle = GrowthCampaignService.Clean(input.PostTitle, 500),
            Format = input.Format, Topic = GrowthCampaignService.Clean(input.Topic, 200), Reactions = input.Reactions, Comments = input.Comments,
            Shares = input.Shares, Views = input.Views, Note = GrowthCampaignService.Clean(input.Note, 2000)
        };
        if (s.Followers == null && s.PostsPerWeek == null && s.PostTitle == null && s.PostUrl == null && s.Note == null)
            throw new ArgumentException("Hãy nhập ít nhất một thông tin.");
        _db.GrowthCompetitorSnapshots.Add(s);
        c.Snapshots.Add(s);
        await _db.SaveChangesAsync(ct);
        return Dto(c);
    }

    public async Task DeleteSnapshotAsync(Guid id, Guid facebookAccountId, Guid snapshotId, CancellationToken ct)
    {
        var c = await LoadAsync(id, facebookAccountId, ct);
        var s = c.Snapshots.FirstOrDefault(x => x.Id == snapshotId) ?? throw new KeyNotFoundException("Không tìm thấy ghi chép.");
        _db.GrowthCompetitorSnapshots.Remove(s);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Counts from the owner's notes; with <paramref name="campaignId"/> and AI on, also an AI reading of them (campaign AI budget).</summary>
    public async Task<GrowthCompetitorAnalysisDto> AnalyzeAsync(Guid facebookAccountId, Guid? campaignId, bool useAi, CancellationToken ct)
    {
        var list = await _db.GrowthCompetitors.AsNoTracking().Include(c => c.Snapshots).Where(c => c.FacebookAccountId == facebookAccountId).ToListAsync(ct);
        var snaps = list.SelectMany(c => c.Snapshots.Select(s => (Competitor: c, Snap: s))).ToList();
        static long Eng(GrowthCompetitorSnapshot s) => (s.Reactions ?? 0) + (s.Comments ?? 0) + (s.Shares ?? 0);
        var dto = new GrowthCompetitorAnalysisDto
        {
            Observations = snaps.Count,
            Formats = snaps.Where(x => x.Snap.Format != null).GroupBy(x => x.Snap.Format!).Select(g => new GrowthCountDto
            {
                Label = g.Key, Value = g.Count(), Extra = g.Any(x => Eng(x.Snap) > 0) ? Math.Round(g.Average(x => (double)Eng(x.Snap)), 1) : null
            }).OrderByDescending(x => x.Value).ToList(),
            Topics = snaps.Where(x => !string.IsNullOrWhiteSpace(x.Snap.Topic)).GroupBy(x => x.Snap.Topic!.Trim().ToLowerInvariant())
                .Select(g => new GrowthCountDto { Label = g.First().Snap.Topic!.Trim(), Value = g.Count(), Extra = Math.Round(g.Average(x => (double)Eng(x.Snap)), 1) })
                .OrderByDescending(x => x.Value).Take(10).ToList(),
            TopPosts = snaps.Where(x => x.Snap.PostTitle != null || x.Snap.PostUrl != null).OrderByDescending(x => Eng(x.Snap)).ThenByDescending(x => x.Snap.Views ?? 0)
                .Take(8).Select(x => { var d = SnapDto(x.Snap); d.Note = x.Competitor.Name + (d.Note != null ? " · " + d.Note : ""); return d; }).ToList(),
            PostingFrequency = list.Select(c => new { c.Name, Ppw = c.Snapshots.OrderByDescending(s => s.Date).FirstOrDefault(s => s.PostsPerWeek != null)?.PostsPerWeek })
                .Where(x => x.Ppw != null).Select(x => new GrowthCountDto { Label = x.Name, Value = x.Ppw!.Value }).OrderByDescending(x => x.Value).ToList()
        };
        if (useAi)
        {
            if (campaignId is not { } cid) throw new ArgumentException("Chọn chiến dịch để dùng ngân sách AI.");
            var campaign = await _campaigns.LoadAsync(cid, ct);
            if (campaign.FacebookAccountId != facebookAccountId) throw new ArgumentException("Chiến dịch không thuộc Page này.");
            if (snaps.Count < 3) throw new InvalidOperationException("Cần ít nhất 3 ghi chép về đối thủ để AI phân tích.");
            var sb = new StringBuilder();
            foreach (var c in list)
            {
                sb.AppendLine($"Page {c.Name} ({c.Niche ?? "chưa rõ lĩnh vực"}){(c.Notes != null ? ": " + c.Notes : "")}");
                foreach (var s in c.Snapshots.OrderByDescending(s => s.Date).Take(15))
                    sb.AppendLine($"- {s.Date:dd/MM}: {s.Format ?? ""} {s.Topic ?? ""} \"{s.PostTitle ?? ""}\" tương tác {Eng(s)} view {s.Views?.ToString() ?? "?"} {s.PostsPerWeek?.ToString() ?? ""} bài/tuần {s.Note ?? ""}");
            }
            (dto.AiNotes, dto.CostUsd) = await _ai.CompetitorNotesAsync(campaign, sb.ToString(), ct);
        }
        return dto;
    }
}

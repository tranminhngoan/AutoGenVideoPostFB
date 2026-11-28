using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.FilmFactory;

namespace NewsStudio.Infrastructure.Outreach;

/// <summary>Input problems the owner can fix (shown as 400 with the message).</summary>
public class OutreachInputException(string message) : Exception(message);

/// <summary>
/// "Chiến dịch kéo tương tác": Group list, invitation texts (AI + library), campaigns with per Group × Page tracking
/// links, Group posts to answer by hand, and the /cheo-uy-tinh numbers. Manual-first by design: Meta gives no API to
/// search Groups, read Group posts or post into Groups, so nothing here posts, comments or joins on its own.
/// </summary>
public partial class OutreachService
{
    public static readonly string[] ExchangeTypeValues = { "follow", "like", "comment", "share", "view", "mixed" };
    public static readonly string[] CampaignStatuses = { "Draft", "PendingApproval", "Running", "Paused", "Completed" };
    private static readonly string[] Styles = { "friendly", "short", "funny" };
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private readonly IApplicationDbContext _db;
    private readonly IFacebookPublishingService _facebook;
    private readonly FilmGeminiClient _gemini;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<OutreachService> _logger;

    public OutreachService(IApplicationDbContext db, IFacebookPublishingService facebook, FilmGeminiClient gemini, IMemoryCache cache,
        IConfiguration config, IHttpClientFactory http, ILogger<OutreachService> logger)
    {
        _http = http;
        _db = db;
        _facebook = facebook;
        _gemini = gemini;
        _cache = cache;
        _config = config;
        _logger = logger;
    }

    private string LandingSlug => _config["Outreach:LandingSlug"] ?? "cheo-uy-tinh";
    private string MainPageId => _config["Outreach:MainPageId"] ?? "1358397297367678";
    /// <summary>Page ids usable as helpers (comma list). Empty = every connected Page except the main one.</summary>
    private HashSet<string> HelperPageIds => (_config["Outreach:HelperPageIds"] ?? "")
        .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
    private bool IsHelper(string pageId) => pageId != MainPageId && (HelperPageIds.Count == 0 || HelperPageIds.Contains(pageId));
    private string PublicBaseUrl => (_config["Outreach:PublicBaseUrl"] ?? "https://www.tuongtaccheo.xyz").TrimEnd('/');

    // ═════════════════════════ Pages ═════════════════════════

    public async Task<List<OutreachPageDto>> ListPagesAsync(bool refresh, CancellationToken ct)
    {
        var accounts = await _db.FacebookAccounts.AsNoTracking().Where(a => a.IsActive).OrderBy(a => a.CreatedAtUtc).ToListAsync(ct);
        var result = new List<OutreachPageDto>();
        foreach (var a in accounts)
        {
            var key = "outreach:page-health:" + a.Id;
            if (refresh) _cache.Remove(key);
            var health = await _cache.GetOrCreateAsync(key, async e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return await _facebook.CheckPageAccessAsync(a.Id, ct);
            });
            var (valid, permissions, expires, error) = health;
            var missing = new[] { "pages_show_list", "pages_read_engagement" }.Where(p => !permissions.Contains(p)).ToList();
            result.Add(new OutreachPageDto
            {
                Id = a.Id,
                PageId = a.PageId,
                Name = a.PageName,
                PictureUrl = a.PageId.All(char.IsDigit) ? $"https://graph.facebook.com/{a.PageId}/picture?type=large" : "",
                IsMain = a.PageId == MainPageId,
                IsHelper = IsHelper(a.PageId),
                Status = a.Status,
                Health = !valid ? "error" : missing.Count > 0 || (expires != null && expires < DateTime.UtcNow.AddDays(7)) ? "warning" : "ok",
                Problem = !valid ? (error ?? "Meta báo token không còn hiệu lực.")
                    : missing.Count > 0 ? "Thiếu quyền: " + string.Join(", ", missing)
                    : expires != null && expires < DateTime.UtcNow.AddDays(7) ? $"Token sắp hết hạn ({expires.Value.AddHours(7):dd/MM/yyyy})." : null,
                Permissions = permissions.ToList(),
                TokenExpiresAtUtc = expires,
            });
        }
        return result.OrderByDescending(p => p.IsMain).ThenByDescending(p => p.IsHelper).ThenBy(p => p.Name).ToList();
    }

    // ═════════════════════════ Groups ═════════════════════════

    /// <summary>facebook.com/groups/abc (m./web., any tail) → https://www.facebook.com/groups/abc. Null = not a group link.</summary>
    public static string? NormalizeGroupUrl(string? input)
    {
        var text = (input ?? "").Trim();
        if (text.Length is 0 or > 500) return null;
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        var host = uri.Host.ToLowerInvariant();
        if (host is not ("facebook.com" or "www.facebook.com" or "m.facebook.com" or "web.facebook.com" or "mbasic.facebook.com" or "fb.com" or "www.fb.com"))
            return null;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !segments[0].Equals("groups", StringComparison.OrdinalIgnoreCase)) return null;
        var id = segments[1];
        return GroupId().IsMatch(id) ? "https://www.facebook.com/groups/" + id.ToLowerInvariant() : null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]{2,100}$")]
    private static partial Regex GroupId();

    private static List<string> CleanTypes(IEnumerable<string>? types) =>
        (types ?? Array.Empty<string>()).Select(t => t.Trim().ToLowerInvariant()).Where(ExchangeTypeValues.Contains).Distinct().ToList();

    private static string? Clip(string? v, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var t = v.Trim();
        return t.Length > max ? t[..max] : t;
    }

    private async Task<string> NextGroupKeyAsync(CancellationToken ct)
    {
        var keys = await _db.OutreachGroups.Select(g => g.Key).ToListAsync(ct);
        var max = keys.Select(k => int.TryParse(k.TrimStart('g'), out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return "g" + (max + 1).ToString("00", CultureInfo.InvariantCulture);
    }

    private void Apply(OutreachGroup g, OutreachGroupInputDto input)
    {
        g.Name = Clip(input.Name, 300) ?? throw new OutreachInputException("Cần tên Group.");
        g.Topic = Clip(input.Topic, 100);
        g.ExchangeTypes = string.Join(',', CleanTypes(input.ExchangeTypes));
        g.Fit = input.Fit is "fit" or "unfit" ? input.Fit : "unknown";
        g.Privacy = input.Privacy is "public" or "private" ? input.Privacy : "unknown";
        g.Members = input.Members is > 0 ? input.Members : null;
        g.Rules = Clip(input.Rules, 3000);
        g.Note = Clip(input.Note, 2000);
    }

    public async Task<List<OutreachGroupDto>> ListGroupsAsync(CancellationToken ct)
    {
        var groups = await _db.OutreachGroups.AsNoTracking().OrderByDescending(g => g.CreatedAtUtc).ToListAsync(ct);
        var byContent = await CountsByContentAsync(DateTime.UtcNow.AddDays(-365), ct);
        var memberships = (await _db.OutreachGroupMemberships.AsNoTracking().ToListAsync(ct)).ToLookup(m => m.GroupId);
        return groups.Select(g =>
        {
            var (views, clicks) = SumForGroup(byContent, g.Key);
            var dto = ToDto(g, views, clicks);
            dto.Memberships = memberships[g.Id].Select(m => new OutreachMembershipDto
            {
                PageId = m.FacebookAccountId, Status = m.Status, UpdatedAtUtc = m.UpdatedAtUtc ?? m.CreatedAtUtc,
            }).ToList();
            return dto;
        }).ToList();
    }

    private static OutreachGroupDto ToDto(OutreachGroup g, int views = 0, int clicks = 0) => new()
    {
        Id = g.Id, Name = g.Name, Url = g.Url, Key = g.Key, Topic = g.Topic,
        ExchangeTypes = g.ExchangeTypes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
        Fit = g.Fit, Privacy = g.Privacy, Members = g.Members, Rules = g.Rules, Note = IsLoginWallText(g.Note) ? null : g.Note,
        CreatedAtUtc = g.CreatedAtUtc, Views = views, Clicks = clicks,
    };

    public async Task<OutreachGroupDto> CreateGroupAsync(OutreachGroupInputDto input, CancellationToken ct)
    {
        var url = NormalizeGroupUrl(input.Url) ?? throw new OutreachInputException("Link Group không hợp lệ (cần dạng facebook.com/groups/…).");
        if (await _db.OutreachGroups.AnyAsync(g => g.Url == url, ct)) throw new OutreachInputException("Group này đã có trong danh sách.");
        var group = new OutreachGroup { Url = url, Key = await NextGroupKeyAsync(ct) };
        if (string.IsNullOrWhiteSpace(input.Name)) input.Name = url["https://www.facebook.com/groups/".Length..];
        Apply(group, input);
        _db.OutreachGroups.Add(group);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Outreach group added: {Name} ({Url})", group.Name, group.Url);
        return ToDto(group);
    }

    public async Task<OutreachGroupDto?> UpdateGroupAsync(Guid id, OutreachGroupInputDto input, CancellationToken ct)
    {
        var group = await _db.OutreachGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group == null) return null;
        if (!string.IsNullOrWhiteSpace(input.Url))
        {
            var url = NormalizeGroupUrl(input.Url) ?? throw new OutreachInputException("Link Group không hợp lệ (cần dạng facebook.com/groups/…).");
            if (url != group.Url && await _db.OutreachGroups.AnyAsync(g => g.Url == url, ct)) throw new OutreachInputException("Link này đã thuộc một Group khác trong danh sách.");
            group.Url = url;
        }
        Apply(group, input);
        group.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDto(group);
    }

    public async Task<bool> DeleteGroupAsync(Guid id, CancellationToken ct)
    {
        var group = await _db.OutreachGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group == null) return false;
        _db.OutreachGroups.Remove(group);
        _db.OutreachActivities.RemoveRange(await _db.OutreachActivities.Where(a => a.GroupId == id).ToListAsync(ct));
        _db.OutreachGroupMemberships.RemoveRange(await _db.OutreachGroupMemberships.Where(m => m.GroupId == id).ToListAsync(ct));
        _db.OutreachShares.RemoveRange(await _db.OutreachShares.Where(s => s.GroupId == id).ToListAsync(ct));
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Records by hand whether a helper Page asked to join / is in / was refused by a Group ("none" forgets it).</summary>
    public async Task<OutreachMembershipDto?> SetMembershipAsync(Guid groupId, Guid pageAccountId, string? status, CancellationToken ct)
    {
        if (!await _db.OutreachGroups.AnyAsync(g => g.Id == groupId, ct)) return null;
        var page = await _db.FacebookAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == pageAccountId && a.IsActive, ct);
        if (page == null || !IsHelper(page.PageId)) throw new OutreachInputException("Chỉ ghi trạng thái cho Page phụ của chiến dịch.");
        var row = await _db.OutreachGroupMemberships.FirstOrDefaultAsync(m => m.GroupId == groupId && m.FacebookAccountId == pageAccountId, ct);
        if (status is not ("requested" or "joined" or "rejected"))
        {
            if (row != null) { _db.OutreachGroupMemberships.Remove(row); await _db.SaveChangesAsync(ct); }
            return new OutreachMembershipDto { PageId = pageAccountId, Status = "none", UpdatedAtUtc = DateTime.UtcNow };
        }
        if (row == null)
        {
            row = new OutreachGroupMembership { GroupId = groupId, FacebookAccountId = pageAccountId };
            _db.OutreachGroupMemberships.Add(row);
        }
        row.Status = status;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new OutreachMembershipDto { PageId = pageAccountId, Status = row.Status, UpdatedAtUtc = row.UpdatedAtUtc.Value };
    }

    // ───────── Group discovery through Brave Search (the owner joins by hand on Facebook) ─────────

    /// <summary>"12K thành viên" / "1,2 N thành viên" / "3.4K members" / "12.345 thành viên" → number.</summary>
    public static int? ParseMembers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = MembersPattern().Match(text);
        if (!m.Success) return null;
        var num = m.Groups[1].Value.Replace(" ", "");
        var unit = m.Groups[2].Value.ToLowerInvariant();
        double value;
        // with a unit "1,2" / "1.2" is a decimal; without one "12.345" / "12,345" groups thousands
        if (unit.Length > 0)
        {
            if (!double.TryParse(num.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return null;
        }
        else if (!double.TryParse(num.Replace(".", "").Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return null;
        var factor = unit switch { "k" or "n" or "nghìn" or "ngàn" => 1_000, "m" or "tr" or "triệu" => 1_000_000, _ => 1 };
        var total = value * factor;
        return total is > 0 and < int.MaxValue ? (int)Math.Round(total) : null;
    }

    [GeneratedRegex(@"(\d[\d.,]*)\s*(k|n|nghìn|ngàn|m|tr|triệu)?\s*(?:thành viên|members?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MembersPattern();

    private static string CleanTitle(string title)
    {
        var t = WebUtility.HtmlDecode(title ?? "").Trim();
        foreach (var tail in new[] { " | Facebook", " - Facebook", "| Facebook" })
            if (t.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) t = t[..^tail.Length].Trim();
        return t;
    }

    /// <summary>Facebook's logged-out boilerplate ("Popular groups · Find communities for you · Log in…") — not a description.</summary>
    public static bool IsLoginWallText(string? text) =>
        !string.IsNullOrWhiteSpace(text) && new[] { "Find communities for you", "Over 1 billion people", "Popular groups", "Log in · Categories", "Đăng nhập · Danh mục" }
            .Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Brave web results → Facebook Group links only (deduplicated; snippet read for members / privacy).</summary>
    public static List<OutreachDiscoveredGroupDto> ParseBraveResults(JsonElement body)
    {
        var list = new List<OutreachDiscoveredGroupDto>();
        if (!body.TryGetProperty("web", out var web) || !web.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return list;
        var seen = new HashSet<string>();
        foreach (var r in results.EnumerateArray())
        {
            var url = NormalizeGroupUrl(r.TryGetProperty("url", out var u) ? u.GetString() : null);
            if (url == null || !seen.Add(url)) continue;
            var title = CleanTitle(r.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "");
            var desc = WebUtility.HtmlDecode(r.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "");
            desc = Regex.Replace(desc, "<[^>]+>", "").Trim();
            if (IsLoginWallText(desc)) desc = "";
            var extra = r.TryGetProperty("extra_snippets", out var ex) && ex.ValueKind == JsonValueKind.Array
                ? string.Join(" ", ex.EnumerateArray().Select(x => x.GetString())) : "";
            var all = (title + " " + desc + " " + extra).ToLowerInvariant();
            list.Add(new OutreachDiscoveredGroupDto
            {
                Name = title.Length > 0 ? title : url["https://www.facebook.com/groups/".Length..],
                Url = url,
                Description = desc.Length > 300 ? desc[..300] + "…" : (desc.Length > 0 ? desc : null),
                Members = ParseMembers(desc + " " + extra),
                Privacy = all.Contains("nhóm riêng tư") || all.Contains("private group") ? "private"
                    : all.Contains("nhóm công khai") || all.Contains("public group") ? "public" : "unknown",
            });
        }
        return list;
    }

    /// <summary>Brave's own reason from an error body ({"error":{"detail":…,"meta":{…}}}), short.</summary>
    public static string BraveErrorDetail(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var detail = err.TryGetProperty("detail", out var d) ? d.GetString() : null;
                var meta = err.TryGetProperty("meta", out var m) ? m.GetRawText() : null;
                var text = string.Join(" ", new[] { detail, meta }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (text.Length > 0) return text.Length > 300 ? text[..300] : text;
            }
        }
        catch (JsonException)
        {
            // not JSON
        }
        return body.Length > 200 ? body[..200] : body;
    }

    public async Task<OutreachDiscoverResultDto> DiscoverGroupsAsync(string keyword, int page, CancellationToken ct)
    {
        var k = (keyword ?? "").Trim();
        if (k.Length is < 2 or > 100) throw new OutreachInputException("Nhập từ khoá 2–100 ký tự.");
        page = Math.Clamp(page, 1, 10);
        var apiKey = _config["Outreach:BraveSearch:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new OutreachInputException("Chưa cấu hình khoá Brave Search. Đăng ký tại api-dashboard.search.brave.com rồi thêm biến Outreach__BraveSearch__ApiKey trên Railway.");

        var cacheKey = $"outreach:discover:{k.ToLowerInvariant()}:{page}";
        if (!_cache.TryGetValue(cacheKey, out List<OutreachDiscoveredGroupDto>? found) || found == null)
        {
            var q = Uri.EscapeDataString($"site:facebook.com/groups {k}");
            // Brave answers 422 to options it doesn't accept (country list, plan-only extras): ask with Vietnamese
            // results first, then with the bare query.
            var urlsToTry = new[]
            {
                $"https://api.search.brave.com/res/v1/web/search?q={q}&count=20&offset={page - 1}&search_lang=vi&safesearch=moderate",
                $"https://api.search.brave.com/res/v1/web/search?q={q}&count=20&offset={page - 1}",
            };
            string? body = null;
            foreach (var url in urlsToTry)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("Accept", "application/json");
                req.Headers.Add("X-Subscription-Token", apiKey.Trim());
                using var res = await _http.CreateClient("brave-search").SendAsync(req, ct);
                var text = await res.Content.ReadAsStringAsync(ct);
                if (res.IsSuccessStatusCode) { body = text; break; }
                var status = (int)res.StatusCode;
                _logger.LogWarning("Brave search failed {Status} for {Url}: {Body}", status, url.Split('?')[1], text.Length > 400 ? text[..400] : text);
                if (status == 422 && url != urlsToTry[^1]) continue;
                throw new InvalidOperationException(status switch
                {
                    401 or 403 => "Khoá Brave Search không hợp lệ hoặc đã bị khoá.",
                    402 => "Hết tín dụng Brave Search tháng này (xem api-dashboard.search.brave.com).",
                    429 => "Tìm quá nhanh hoặc hết hạn mức Brave Search, thử lại sau ít phút.",
                    _ => $"Brave Search lỗi {status}: {BraveErrorDetail(text)}",
                });
            }
            using var doc = JsonDocument.Parse(body!);
            found = ParseBraveResults(doc.RootElement);
            _cache.Set(cacheKey, found, TimeSpan.FromHours(6));
            _logger.LogInformation("Outreach group search {Keyword} page {Page}: {Count} groups", k, page, found.Count);
        }

        var urls = found.Select(f => f.Url).ToList();
        var known = await _db.OutreachGroups.AsNoTracking().Where(g => urls.Contains(g.Url)).ToDictionaryAsync(g => g.Url, g => g.Id, ct);
        return new OutreachDiscoverResultDto
        {
            Keyword = k,
            Page = page,
            HasMore = found.Count >= 5 && page < 10,
            FacebookSearchUrl = "https://www.facebook.com/search/groups/?q=" + Uri.EscapeDataString(k),
            Results = found.Select(f => new OutreachDiscoveredGroupDto
            {
                Name = f.Name, Url = f.Url, Description = f.Description, Members = f.Members, Privacy = f.Privacy,
                GroupId = known.TryGetValue(f.Url, out var id) ? id : null,
            }).ToList(),
        };
    }

    /// <summary>
    /// CSV with a header row; columns (any order, Vietnamese or English names): url/link, name/ten, topic/chu_de,
    /// types/loai (separated by ; or |), note/ghi_chu. A file of bare URLs (one per line) also works.
    /// </summary>
    public async Task<OutreachImportResultDto> ImportGroupsCsvAsync(string csv, CancellationToken ct)
    {
        var result = new OutreachImportResultDto();
        var lines = (csv ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) throw new OutreachInputException("File CSV trống.");
        if (lines.Count > 2000) throw new OutreachInputException("Tối đa 2000 dòng mỗi lần nhập.");

        var header = SplitCsv(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(params string[] names) => header.FindIndex(names.Contains);
        var hasHeader = NormalizeGroupUrl(lines[0].Split(',')[0]) == null && header.Any(h => h is "url" or "link" or "group_url");
        int iUrl = hasHeader ? Col("url", "link", "group_url") : 0, iName = hasHeader ? Col("name", "ten", "tên", "group_name") : -1;
        int iTopic = hasHeader ? Col("topic", "chu_de", "chủ đề", "chude") : -1, iTypes = hasHeader ? Col("types", "loai", "loại", "exchange") : -1;
        int iNote = hasHeader ? Col("note", "ghi_chu", "ghi chú", "ghichu") : -1;

        var existing = (await _db.OutreachGroups.Select(g => g.Url).ToListAsync(ct)).ToHashSet();
        var nextKey = int.Parse((await NextGroupKeyAsync(ct))[1..], CultureInfo.InvariantCulture);
        foreach (var line in lines.Skip(hasHeader ? 1 : 0))
        {
            var cells = SplitCsv(line);
            string? Cell(int i) => i >= 0 && i < cells.Count ? cells[i].Trim() : null;
            var url = NormalizeGroupUrl(Cell(iUrl));
            if (url == null)
            {
                if (result.Invalid.Count < 50) result.Invalid.Add(line.Length > 120 ? line[..120] + "…" : line);
                continue;
            }
            if (!existing.Add(url))
            {
                result.Duplicates++;
                continue;
            }
            var group = new OutreachGroup { Url = url, Key = "g" + (nextKey++).ToString("00", CultureInfo.InvariantCulture) };
            Apply(group, new OutreachGroupInputDto
            {
                Name = Cell(iName) is { Length: > 0 } n ? n : url["https://www.facebook.com/groups/".Length..],
                Topic = Cell(iTopic),
                ExchangeTypes = Cell(iTypes)?.Split(new[] { ';', '|', '/' }, StringSplitOptions.RemoveEmptyEntries).ToList(),
                Note = Cell(iNote),
            });
            _db.OutreachGroups.Add(group);
            result.Added++;
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Outreach CSV import: {Added} added, {Dup} duplicates, {Bad} invalid", result.Added, result.Duplicates, result.Invalid.Count);
        return result;
    }

    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if ((c == ',' || c == ';' || c == '\t') && !quoted && !(c == ';' && line.Contains(','))) { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString());
        return cells;
    }

    /// <summary>
    /// Meta has no Group search API: returns Facebook's own search link for the keyword (opened by the owner, while
    /// logged in) plus related keywords worth trying. Nothing is fetched from Facebook.
    /// </summary>
    public static OutreachGroupSearchDto GroupSearch(string keyword)
    {
        var k = (keyword ?? "").Trim();
        if (k.Length is < 2 or > 100) throw new OutreachInputException("Nhập từ khoá 2–100 ký tự.");
        var related = new[]
        {
            $"{k} Việt Nam", $"hội {k}", $"cộng đồng {k}", $"{k} giao lưu",
            "tương tác chéo", "follow chéo uy tín", "like chéo fanpage", "trao đổi tương tác page", "kéo view video",
        }.Where(x => !x.Equals(k, StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
        return new OutreachGroupSearchDto
        {
            Keyword = k,
            FacebookSearchUrl = "https://www.facebook.com/search/groups/?q=" + Uri.EscapeDataString(k),
            RelatedKeywords = related,
        };
    }

    // ═════════════════════════ Content library + AI ═════════════════════════

    private static string Fold(string text)
    {
        var d = text.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return sb.ToString();
    }

    /// <summary>0–100 overlap of word pairs (links and emoji ignored): ≥ 70 reads as the same text.</summary>
    public static int Similarity(string a, string b)
    {
        static HashSet<string> Shingles(string s)
        {
            var words = Fold(LinkPattern().Replace(s, " ")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var set = new HashSet<string>();
            if (words.Length == 1) set.Add(words[0]);
            for (var i = 0; i + 1 < words.Length; i++) set.Add(words[i] + " " + words[i + 1]);
            return set;
        }
        var x = Shingles(a);
        var y = Shingles(b);
        if (x.Count == 0 || y.Count == 0) return 0;
        var inter = x.Count(y.Contains);
        return (int)Math.Round(100.0 * inter / (x.Count + y.Count - inter));
    }

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex LinkPattern();

    private async Task<(int Score, string? Text)> MostSimilarAsync(string text, Guid? exceptId, CancellationToken ct)
    {
        var library = await _db.OutreachContents.AsNoTracking().Where(c => exceptId == null || c.Id != exceptId)
            .OrderByDescending(c => c.CreatedAtUtc).Take(500).Select(c => c.Text).ToListAsync(ct);
        var best = library.Select(t => (Score: Similarity(text, t), Text: t)).OrderByDescending(x => x.Score).FirstOrDefault();
        return best.Text == null ? (0, null) : best;
    }

    public async Task<OutreachDraftDto> CheckTextAsync(string text, CancellationToken ct)
    {
        var (score, similar) = await MostSimilarAsync(text ?? "", null, ct);
        return new OutreachDraftDto { Text = text ?? "", Similarity = score, SimilarTo = similar };
    }

    public string LandingUrl(string? utmQuery = null) => PublicBaseUrl + "/" + LandingSlug + (string.IsNullOrEmpty(utmQuery) ? "" : "?" + utmQuery);

    /// <summary>AI writes N invitation texts (Vietnamese, natural, honest) for the main Page; the link is added at the end.</summary>
    public async Task<List<OutreachDraftDto>> WriteAsync(OutreachWriteRequestDto input, CancellationToken ct)
    {
        var style = Styles.Contains(input.Style) ? input.Style : "friendly";
        var count = Math.Clamp(input.Count, 1, 5);
        OutreachGroup? group = input.GroupId is { } gid ? await _db.OutreachGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gid, ct) : null;
        var main = await _db.FacebookAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.PageId == MainPageId, ct);
        var pageName = main?.PageName ?? "Page chính";
        var topic = Clip(input.Topic, 100) ?? group?.Topic;

        var styleText = style switch
        {
            "short" => "rất ngắn gọn: 1–2 câu",
            "funny" => "hài hước, dí dỏm nhẹ nhàng, không lố, 2–3 câu",
            _ => "thân thiện, ấm áp, chân thành, 2–4 câu",
        };
        var prompt = new StringBuilder()
            .AppendLine($"Page cần mời mọi người ghé: \"{pageName}\" — Page video hoài niệm ký ức tuổi thơ Việt Nam.")
            .AppendLine($"Phong cách: {styleText}.")
            .AppendLine(topic != null ? $"Chủ đề Group sẽ đăng: {topic}." : "")
            .AppendLine(group != null ? $"Tên Group: {group.Name}. Loại trao đổi của Group: {(group.ExchangeTypes.Length > 0 ? group.ExchangeTypes : "chưa rõ")}." : "")
            .AppendLine(!string.IsNullOrWhiteSpace(group?.Rules) ? $"Quy định của Group (phải tuân thủ): {group!.Rules}" : "")
            .AppendLine(!string.IsNullOrWhiteSpace(input.Extra) ? $"Yêu cầu thêm của chủ Page: {Clip(input.Extra, 500)}" : "")
            .AppendLine($@"
Viết {count} phiên bản KHÁC NHAU (khác cách mở đầu, khác ý), tiếng Việt tự nhiên như người thật đăng trong Group:
- Mời mọi người ghé thăm Page, Follow, xem video; Like / bình luận NẾU thấy hay.
- Có thể dùng 1–3 emoji hợp lý.
- KHÔNG chèn link (hệ thống tự thêm link ở cuối), KHÔNG hashtag dồn dập, KHÔNG viết hoa toàn bộ.
- KHÔNG hứa hẹn quà, tiền, số lượng follow, ""follow lại 100%"" hay bất cứ điều gì không chắc chắn; KHÔNG giả làm người khác; KHÔNG gây hiểu nhầm.
- Nếu Group là nhóm trao đổi tương tác thì có thể nói nhẹ nhàng là mình sẽ ghé tương tác lại bạn nào để lại bình luận.
Trả về JSON: {{ ""texts"": [""..."", ""...""] }}");

        var system = "Bạn là người viết nội dung mạng xã hội tiếng Việt, trung thực, không spam. Chỉ trả về JSON hợp lệ.";
        var result = await _gemini.GenerateJsonAsync(system, prompt.ToString(), 2048, 0.95, ct, null, true);
        _logger.LogInformation("Outreach AI wrote invitations (style {Style}, cost ${Cost})", style, result.CostUsd);
        var texts = ParseStrings(result.Text, "texts");
        if (texts.Count == 0) throw new InvalidOperationException("AI không trả về nội dung. Thử lại.");

        var drafts = new List<OutreachDraftDto>();
        foreach (var t in texts.Take(count))
        {
            // the site as plain text (many Groups refuse links), on its own line so readers can copy it
            var text = t.Trim() + "\n\n👉 " + new Uri(PublicBaseUrl).Host.Replace("www.", "");
            var (score, similar) = await MostSimilarAsync(text, null, ct);
            // also compare with the other drafts of this batch
            foreach (var d in drafts)
            {
                var s = Similarity(text, d.Text);
                if (s > score) { score = s; similar = d.Text; }
            }
            drafts.Add(new OutreachDraftDto { Text = text, Similarity = score, SimilarTo = similar });
        }
        return drafts;
    }

    private static List<string> ParseStrings(string raw, string property)
    {
        var text = raw.Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        var list = new List<string>();
        if (start < 0 || end <= start) return list;
        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            if (doc.RootElement.TryGetProperty(property, out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var s in arr.EnumerateArray())
                    if (s.ValueKind == JsonValueKind.String && s.GetString() is { Length: > 0 } v) list.Add(v.Trim());
        }
        catch (JsonException)
        {
            // fall through: empty list → the caller says the AI gave nothing usable
        }
        return list;
    }


    /// <summary>
    /// The owner's standard Group post, rewritten N times with different words and the same meaning: visit the site
    /// (written as plain text, since many Groups refuse links), follow / like / comment, comment "done", and the Page
    /// interacts back. Never promises fake accounts, multiplied returns, gifts or money.
    /// </summary>
    public async Task<List<OutreachDraftDto>> WriteVariantsAsync(OutreachVariantsRequestDto input, CancellationToken ct)
    {
        var count = Math.Clamp(input.Count, 1, 10);
        var domain = new Uri(PublicBaseUrl).Host.Replace("www.", "");
        var recent = await _db.OutreachContents.AsNoTracking().OrderByDescending(c => c.CreatedAtUtc).Take(15).Select(c => c.Text).ToListAsync(ct);

        var prompt = new StringBuilder()
            .AppendLine($@"Viết {count} bài đăng NGẮN (2–4 câu) để đăng vào các Group Facebook trao đổi tương tác. Mỗi bài viết KHÁC HẲN nhau về câu chữ, cách mở đầu, thứ tự ý, nhưng CÙNG MỘT Ý:")
            .AppendLine($"1. Mời mọi người vào trang {domain} (viết đúng chữ \"{domain}\", dạng chữ thường, KHÔNG có https, KHÔNG phải link).")
            .AppendLine("2. Ở đó bấm follow / like / comment ủng hộ Page.")
            .AppendLine("3. Comment \"done\" (hoặc \"xong\") dưới bài để mình biết.")
            .AppendLine("4. Mình nhận được thông báo sẽ ghé tương tác lại đầy đủ, uy tín.")
            .AppendLine(@"
Giọng: thân thiện, tự nhiên như người thật trong nhóm; có thể dùng từ quen thuộc của dân chéo tương tác (follow, like, cmt, chéo, uy tín); 0–2 emoji; không viết hoa toàn bộ.
TUYỆT ĐỐI KHÔNG: nhắc tới tài khoản nuôi / clone / nick phụ / tài khoản ảo; hứa trả gấp nhiều lần (""1 trả 5"", ""trả x2""…); hứa quà, tiền, số lượng; giả danh người khác; hashtag.")
            .AppendLine(!string.IsNullOrWhiteSpace(input.Extra) ? $"Yêu cầu thêm của chủ Page: {Clip(input.Extra, 300)}" : "")
            .AppendLine(recent.Count > 0 ? "Tránh lặp lại câu chữ của các bài đã dùng gần đây:\n- " + string.Join("\n- ", recent.Select(r => r.Length > 160 ? r[..160] : r)) : "")
            .AppendLine(@"Trả về JSON: { ""texts"": [""..."", ""...""] }");

        var system = "Bạn viết bài đăng mạng xã hội tiếng Việt ngắn, tự nhiên, trung thực, không spam. Chỉ trả về JSON hợp lệ.";
        var result = await _gemini.GenerateJsonAsync(system, prompt.ToString(), 3072, 1.0, ct, null, true);
        _logger.LogInformation("Outreach AI wrote {Count} post variants (cost ${Cost})", count, result.CostUsd);
        var banned = new[] { "tài khoản nuôi", "nick nuôi", "clone", "nick phụ", "tài khoản ảo", "trả gấp", "1 trả", "x2", "x3", "x5" };
        var texts = ParseStrings(result.Text, "texts")
            .Select(t => t.Trim())
            .Where(t => t.Length > 0 && !banned.Any(b => t.Contains(b, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (texts.Count == 0) throw new InvalidOperationException("AI không trả về bài viết dùng được. Thử lại.");

        var drafts = new List<OutreachDraftDto>();
        foreach (var text in texts.Take(count))
        {
            var (score, similar) = await MostSimilarAsync(text, null, ct);
            foreach (var d in drafts)
            {
                var s = Similarity(text, d.Text);
                if (s > score) { score = s; similar = d.Text; }
            }
            drafts.Add(new OutreachDraftDto { Text = text, Similarity = score, SimilarTo = similar });
        }
        return drafts;
    }

    public async Task<List<OutreachContentDto>> ListContentsAsync(CancellationToken ct) =>
        await _db.OutreachContents.AsNoTracking().OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new OutreachContentDto { Id = c.Id, Text = c.Text, Style = c.Style, Topic = c.Topic, Origin = c.Origin, CopiedCount = c.CopiedCount, CreatedAtUtc = c.CreatedAtUtc })
            .ToListAsync(ct);

    public async Task<OutreachContentDto> SaveContentAsync(OutreachContentInputDto input, CancellationToken ct)
    {
        var text = Clip(input.Text, 3000) ?? throw new OutreachInputException("Nội dung đang trống.");
        var (score, _) = await MostSimilarAsync(text, null, ct);
        if (score >= 90) throw new OutreachInputException("Nội dung gần như trùng với một mẫu đã lưu trong thư viện.");
        var c = new OutreachContent
        {
            Text = text,
            Style = input.Style is "friendly" or "short" or "funny" or "post" ? input.Style : "custom",
            Topic = Clip(input.Topic, 100),
            Origin = input.Origin == "ai" ? "ai" : "manual",
        };
        _db.OutreachContents.Add(c);
        await _db.SaveChangesAsync(ct);
        return new OutreachContentDto { Id = c.Id, Text = c.Text, Style = c.Style, Topic = c.Topic, Origin = c.Origin, CreatedAtUtc = c.CreatedAtUtc };
    }

    public async Task<bool> DeleteContentAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.OutreachContents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return false;
        _db.OutreachContents.Remove(c);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> MarkCopiedAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.OutreachContents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return false;
        c.CopiedCount++;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ═════════════════════════ Campaigns ═════════════════════════

    private static List<Guid> Ids(string json)
    {
        try { return JsonSerializer.Deserialize<List<Guid>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    [GeneratedRegex("^[a-z0-9_-]{2,60}$")]
    private static partial Regex SlugPattern();

    private static string PageKey(string pageName)
    {
        var folded = Fold(pageName).Trim();
        var words = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w is not ("tram" or "tin" or "trang" or "page")).ToList();
        var key = string.Join("", words.Count > 0 ? words : folded.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return key.Length == 0 ? "page" : key[..Math.Min(key.Length, 20)];
    }

    private async Task Validate(OutreachCampaign c, OutreachCampaignInputDto input, CancellationToken ct)
    {
        c.Name = Clip(input.Name, 200) ?? throw new OutreachInputException("Cần tên chiến dịch.");
        var pageIds = (input.PageIds ?? new()).Distinct().ToList();
        if (pageIds.Count > 0)
        {
            var known = await _db.FacebookAccounts.Where(a => pageIds.Contains(a.Id) && a.IsActive).Select(a => new { a.Id, a.PageId }).ToListAsync(ct);
            if (known.Count != pageIds.Count) throw new OutreachInputException("Có Page không còn kết nối.");
            if (known.Any(k => !IsHelper(k.PageId))) throw new OutreachInputException("Chỉ chọn Page phụ của chiến dịch (Trạm Tin Showbiz / Trạm Tin Công Nghệ).");
        }
        var groupIds = (input.GroupIds ?? new()).Distinct().ToList();
        if (groupIds.Count > 0 && await _db.OutreachGroups.CountAsync(g => groupIds.Contains(g.Id), ct) != groupIds.Count)
            throw new OutreachInputException("Có Group không còn trong danh sách.");
        if (input.StartDate != null && input.EndDate != null && input.EndDate < input.StartDate)
            throw new OutreachInputException("Ngày kết thúc phải sau ngày bắt đầu.");

        c.PageIdsJson = JsonSerializer.Serialize(pageIds);
        c.GroupIdsJson = JsonSerializer.Serialize(groupIds);
        c.ContentId = input.ContentId;
        c.ContentText = Clip(input.ContentText, 3000);
        // Date pickers send "2026-11-28" (no time zone): store as a UTC calendar day — PostgreSQL refuses Kind=Unspecified
        c.StartDate = input.StartDate is { } start ? DateTime.SpecifyKind(start.Date, DateTimeKind.Utc) : null;
        c.EndDate = input.EndDate is { } end ? DateTime.SpecifyKind(end.Date, DateTimeKind.Utc) : null;
        c.Status = CampaignStatuses.Contains(input.Status) ? input.Status! : c.Status;
        c.Owner = Clip(input.Owner, 100);
        c.ResultNote = Clip(input.ResultNote, 3000);
    }

    public async Task<OutreachCampaignDto> CreateCampaignAsync(OutreachCampaignInputDto input, CancellationToken ct)
    {
        var c = new OutreachCampaign { Status = "Draft" };
        await Validate(c, input, ct);
        var slug = Clip(input.Slug, 60)?.ToLowerInvariant();
        if (slug == null)
        {
            var n = await _db.OutreachCampaigns.CountAsync(ct) + 1;
            do slug = "campaign_" + (n++).ToString("00", CultureInfo.InvariantCulture);
            while (await _db.OutreachCampaigns.AnyAsync(x => x.Slug == slug, ct));
        }
        else if (!SlugPattern().IsMatch(slug)) throw new OutreachInputException("Mã chiến dịch chỉ gồm chữ thường không dấu, số, - và _.");
        else if (await _db.OutreachCampaigns.AnyAsync(x => x.Slug == slug, ct)) throw new OutreachInputException("Mã chiến dịch đã được dùng.");
        c.Slug = slug;
        _db.OutreachCampaigns.Add(c);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Outreach campaign created: {Name} ({Slug})", c.Name, c.Slug);
        return (await ListCampaignsAsync(ct)).First(x => x.Id == c.Id);
    }

    public async Task<OutreachCampaignDto?> UpdateCampaignAsync(Guid id, OutreachCampaignInputDto input, CancellationToken ct)
    {
        var c = await _db.OutreachCampaigns.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return null;
        await Validate(c, input, ct);
        c.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return (await ListCampaignsAsync(ct)).First(x => x.Id == c.Id);
    }

    public async Task<bool> DeleteCampaignAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.OutreachCampaigns.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return false;
        _db.OutreachActivities.RemoveRange(await _db.OutreachActivities.Where(a => a.CampaignId == id).ToListAsync(ct));
        foreach (var s in await _db.OutreachShares.Where(x => x.CampaignId == id).ToListAsync(ct)) s.CampaignId = null;
        foreach (var w in await _db.OutreachWallPosts.Where(x => x.CampaignId == id).ToListAsync(ct)) w.CampaignId = null;
        _db.OutreachCampaigns.Remove(c);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<OutreachCampaignDto>> ListCampaignsAsync(CancellationToken ct)
    {
        var campaigns = await _db.OutreachCampaigns.AsNoTracking().OrderByDescending(c => c.CreatedAtUtc).ToListAsync(ct);
        var done = await _db.OutreachActivities.AsNoTracking().GroupBy(a => a.CampaignId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var sharedPairs = (await _db.OutreachShares.AsNoTracking().Where(s => s.CampaignId != null)
                .Select(s => new { s.CampaignId, s.GroupId, s.FacebookAccountId }).Distinct().ToListAsync(ct))
            .GroupBy(s => s.CampaignId!.Value).ToDictionary(g => g.Key, g => g.Count());

        // Posts carry no link any more (visitors type the site), so a visit can't be tied to one campaign:
        // count every visit / "Mở Facebook" click on the landing page during the campaign's dates.
        var nowUtc = DateTime.UtcNow;
        var todayStartUtc = (nowUtc + VietnamOffset).Date - VietnamOffset;
        DateTime FromUtc(OutreachCampaign c) => c.StartDate is { } s ? s.Date - VietnamOffset : c.CreatedAtUtc;
        DateTime ToUtc(OutreachCampaign c) => c.EndDate is { } e ? e.Date.AddDays(1) - VietnamOffset : nowUtc.AddMinutes(1);
        var earliest = campaigns.Count == 0 ? nowUtc : campaigns.Min(FromUtc);
        if (earliest < nowUtc.AddDays(-400)) earliest = nowUtc.AddDays(-400);
        var visits = campaigns.Count == 0 ? new() : await _db.LandingEvents.AsNoTracking()
            .Where(e => e.Slug == LandingSlug && e.CreatedAtUtc >= earliest && (e.Type == "view" || e.Type == "open_page"))
            .Select(e => new { e.Type, e.CreatedAtUtc, e.VisitorId })
            .ToListAsync(ct);

        return campaigns.Select(c =>
        {
            var pages = Ids(c.PageIdsJson);
            var groups = Ids(c.GroupIdsJson);
            var from = FromUtc(c);
            var to = ToUtc(c);
            var inPeriod = visits.Where(v => v.CreatedAtUtc >= from && v.CreatedAtUtc < to).ToList();
            return new OutreachCampaignDto
            {
                Id = c.Id, Name = c.Name, Slug = c.Slug, PageIds = pages, GroupIds = groups, TargetPath = c.TargetPath,
                ContentId = c.ContentId, ContentText = c.ContentText, StartDate = c.StartDate, EndDate = c.EndDate,
                Status = c.Status, Owner = c.Owner, ResultNote = c.ResultNote, CreatedAtUtc = c.CreatedAtUtc,
                Views = inPeriod.Count(v => v.Type == "view"),
                Clicks = inPeriod.Count(v => v.Type == "open_page"),
                Visitors = inPeriod.Where(v => v.Type == "view" && v.VisitorId != null).Select(v => v.VisitorId).Distinct().Count(),
                TodayViews = inPeriod.Count(v => v.Type == "view" && v.CreatedAtUtc >= todayStartUtc),
                TodayClicks = inPeriod.Count(v => v.Type == "open_page" && v.CreatedAtUtc >= todayStartUtc),
                ActivitiesDone = Math.Min(groups.Count * Math.Max(1, pages.Count), sharedPairs.GetValueOrDefault(c.Id) + done.GetValueOrDefault(c.Id)),
                ActivitiesPlanned = groups.Count * Math.Max(1, pages.Count),
            };
        }).ToList();
    }

    /// <summary>Every Group × helper Page of the campaign with its own tracking link, numbers and "done by hand" marks.</summary>
    public async Task<List<OutreachCampaignLinkDto>?> CampaignLinksAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.OutreachCampaigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return null;
        var groupIds = Ids(c.GroupIdsJson);
        var pageIds = Ids(c.PageIdsJson);
        var groups = await _db.OutreachGroups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToListAsync(ct);
        var pages = await _db.FacebookAccounts.AsNoTracking().Where(a => pageIds.Contains(a.Id)).ToListAsync(ct);
        var activities = await _db.OutreachActivities.AsNoTracking().Where(a => a.CampaignId == id).OrderByDescending(a => a.DoneAtUtc).ToListAsync(ct);
        var memberships = await _db.OutreachGroupMemberships.AsNoTracking().Where(m => groupIds.Contains(m.GroupId)).ToListAsync(ct);
        var shares = await _db.OutreachShares.AsNoTracking().Where(s => s.CampaignId == id).ToListAsync(ct);
        var counts = await _db.LandingEvents.AsNoTracking()
            .Where(e => e.Slug == LandingSlug && e.UtmCampaign == c.Slug && e.UtmContent != null && (e.Type == "view" || e.Type == "open_page"))
            .GroupBy(e => new { e.UtmContent, e.Type }).Select(g => new { g.Key.UtmContent, g.Key.Type, Count = g.Count() }).ToListAsync(ct);

        var rows = new List<OutreachCampaignLinkDto>();
        foreach (var g in groups.OrderBy(g => g.Key))
        {
            var pageList = pages.Count > 0 ? pages.Select(p => (FacebookAccount?)p).ToList() : new List<FacebookAccount?> { null };
            foreach (var p in pageList)
            {
                var content = p == null ? g.Key : $"{PageKey(p.PageName)}_{g.Key}";
                var query = $"utm_source=facebook&utm_medium=group&utm_campaign={Uri.EscapeDataString(c.Slug)}&utm_content={Uri.EscapeDataString(content)}";
                rows.Add(new OutreachCampaignLinkDto
                {
                    GroupId = g.Id, GroupName = g.Name, GroupUrl = g.Url, PageId = p?.Id, PageName = p?.PageName, UtmContent = content,
                    Membership = p == null ? null : memberships.FirstOrDefault(m => m.GroupId == g.Id && m.FacebookAccountId == p.Id)?.Status,
                    Shares = shares.Count(s => s.GroupId == g.Id && (p == null || s.FacebookAccountId == p.Id)),
                    LastSharedAtUtc = shares.Where(s => s.GroupId == g.Id && (p == null || s.FacebookAccountId == p.Id)).Select(s => (DateTime?)s.CreatedAtUtc).Max(),
                    Link = LandingUrl(query),
                    Views = counts.Where(x => x.UtmContent == content && x.Type == "view").Sum(x => x.Count),
                    Clicks = counts.Where(x => x.UtmContent == content && x.Type == "open_page").Sum(x => x.Count),
                    Activities = activities.Where(a => a.GroupId == g.Id && a.FacebookAccountId == p?.Id).Select(ToDto).ToList(),
                });
            }
        }
        return rows;
    }

    private static OutreachActivityDto ToDto(OutreachActivity a) => new()
    {
        Id = a.Id, GroupId = a.GroupId, PageId = a.FacebookAccountId, Kind = a.Kind, PostUrl = a.PostUrl, Note = a.Note, DoneAtUtc = a.DoneAtUtc,
    };

    public async Task<OutreachActivityDto?> AddActivityAsync(Guid campaignId, OutreachActivityInputDto input, CancellationToken ct)
    {
        var c = await _db.OutreachCampaigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == campaignId, ct);
        if (c == null) return null;
        if (!Ids(c.GroupIdsJson).Contains(input.GroupId)) throw new OutreachInputException("Group không thuộc chiến dịch này.");
        if (input.PageId is { } pid && !Ids(c.PageIdsJson).Contains(pid)) throw new OutreachInputException("Page không thuộc chiến dịch này.");
        string? postUrl = null;
        if (!string.IsNullOrWhiteSpace(input.PostUrl))
        {
            if (!Uri.TryCreate(input.PostUrl.Trim(), UriKind.Absolute, out var u) || !u.Host.EndsWith("facebook.com", StringComparison.OrdinalIgnoreCase))
                throw new OutreachInputException("Link bài đăng phải là link Facebook.");
            postUrl = Clip(u.ToString(), 500);
        }
        var a = new OutreachActivity
        {
            CampaignId = campaignId, GroupId = input.GroupId, FacebookAccountId = input.PageId,
            Kind = input.Kind is "join" or "comment" ? input.Kind : "post", PostUrl = postUrl, Note = Clip(input.Note, 1000),
        };
        _db.OutreachActivities.Add(a);
        await _db.SaveChangesAsync(ct);
        return ToDto(a);
    }

    public async Task<bool> DeleteActivityAsync(Guid id, CancellationToken ct)
    {
        var a = await _db.OutreachActivities.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a == null) return false;
        _db.OutreachActivities.Remove(a);
        await _db.SaveChangesAsync(ct);
        return true;
    }


    // ═════════════════════════ Wall posts + shares into Groups ═════════════════════════

    /// <summary>
    /// Publishes a picture + text on a helper Page's wall through the Graph API (allowed). Sharing it into Groups is
    /// then done by hand on Facebook — Meta has no API for Groups.
    /// </summary>
    public async Task<OutreachWallPostDto> PublishWallPostAsync(Guid pageAccountId, Guid? campaignId, string? message, Stream image, string fileName, CancellationToken ct)
    {
        var page = await _db.FacebookAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == pageAccountId && a.IsActive, ct);
        if (page == null || !IsHelper(page.PageId)) throw new OutreachInputException("Chỉ đăng lên tường Page phụ của chiến dịch.");
        if (campaignId is { } cid && !await _db.OutreachCampaigns.AnyAsync(c => c.Id == cid, ct)) throw new OutreachInputException("Chiến dịch không còn tồn tại.");
        var text = Clip(message, 3000) ?? throw new OutreachInputException("Bài đăng cần có chữ.");
        var ext = Path.GetExtension(fileName).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" ? Path.GetExtension(fileName).ToLowerInvariant() : ".jpg";

        var dir = Path.Combine(Path.GetTempPath(), "outreach-wall");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ext);
        string postId;
        try
        {
            await using (var file = File.Create(path)) await image.CopyToAsync(file, ct);
            if (new FileInfo(path).Length is 0 or > 8 * 1024 * 1024) throw new OutreachInputException("Ảnh trống hoặc quá 8MB.");
            postId = await _facebook.PublishPhotoPostAsync(pageAccountId, new[] { path }, text, ct);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { /* temp file, ignore */ }
        }

        var post = new OutreachWallPost
        {
            FacebookAccountId = pageAccountId, CampaignId = campaignId, ExternalPostId = postId,
            PermalinkUrl = "https://www.facebook.com/" + postId, Text = text,
        };
        _db.OutreachWallPosts.Add(post);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Outreach wall post published on {Page}: {PostId}", page.PageName, postId);
        return new OutreachWallPostDto
        {
            Id = post.Id, PageId = pageAccountId, PageName = page.PageName, CampaignId = campaignId, PostId = postId,
            PermalinkUrl = post.PermalinkUrl, Text = text, CreatedAtUtc = post.CreatedAtUtc,
        };
    }

    public async Task<List<OutreachWallPostDto>> ListWallPostsAsync(Guid? pageAccountId, CancellationToken ct)
    {
        var q = _db.OutreachWallPosts.AsNoTracking();
        if (pageAccountId is { } pid) q = q.Where(p => p.FacebookAccountId == pid);
        var posts = await q.OrderByDescending(p => p.CreatedAtUtc).Take(50).ToListAsync(ct);
        var ids = posts.Select(p => p.Id).ToList();
        var shares = (await _db.OutreachShares.AsNoTracking().Where(s => ids.Contains(s.WallPostId)).ToListAsync(ct)).ToLookup(s => s.WallPostId);
        var names = await _db.FacebookAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.PageName, ct);
        return posts.Select(p => new OutreachWallPostDto
        {
            Id = p.Id, PageId = p.FacebookAccountId, PageName = names.GetValueOrDefault(p.FacebookAccountId, "Page"), CampaignId = p.CampaignId,
            PostId = p.ExternalPostId, PermalinkUrl = p.PermalinkUrl, Text = p.Text, CreatedAtUtc = p.CreatedAtUtc,
            Shares = shares[p.Id].Select(s => new OutreachShareDto { GroupId = s.GroupId, SharedAtUtc = s.CreatedAtUtc }).ToList(),
        }).ToList();
    }

    /// <summary>Ticks / unticks "shared this wall post into that Group" (done by hand on Facebook).</summary>
    public async Task<OutreachShareDto?> SetShareAsync(Guid wallPostId, Guid groupId, bool shared, CancellationToken ct)
    {
        var post = await _db.OutreachWallPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == wallPostId, ct);
        if (post == null || !await _db.OutreachGroups.AnyAsync(g => g.Id == groupId, ct)) return null;
        var row = await _db.OutreachShares.FirstOrDefaultAsync(s => s.WallPostId == wallPostId && s.GroupId == groupId, ct);
        if (!shared)
        {
            if (row != null) { _db.OutreachShares.Remove(row); await _db.SaveChangesAsync(ct); }
            return new OutreachShareDto { GroupId = groupId, SharedAtUtc = DateTime.MinValue };
        }
        if (row == null)
        {
            row = new OutreachShare { WallPostId = wallPostId, GroupId = groupId, FacebookAccountId = post.FacebookAccountId, CampaignId = post.CampaignId };
            _db.OutreachShares.Add(row);
            await _db.SaveChangesAsync(ct);
        }
        return new OutreachShareDto { GroupId = groupId, SharedAtUtc = row.CreatedAtUtc };
    }

    // ═════════════════════════ Group posts to answer by hand ═════════════════════════

    public async Task<List<OutreachPostTaskDto>> ListPostTasksAsync(string? status, CancellationToken ct)
    {
        var q = _db.OutreachPostTasks.AsNoTracking();
        if (status is "todo" or "done" or "skipped") q = q.Where(t => t.Status == status);
        var tasks = await q.OrderByDescending(t => t.CreatedAtUtc).Take(500).ToListAsync(ct);
        var names = await _db.OutreachGroups.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        return tasks.Select(t => new OutreachPostTaskDto
        {
            Id = t.Id, GroupId = t.GroupId, GroupName = t.GroupId is { } g && names.TryGetValue(g, out var n) ? n : null, CampaignId = t.CampaignId,
            PostUrl = t.PostUrl, PostText = t.PostText, SuggestedComment = t.SuggestedComment, Status = t.Status, DoneAtUtc = t.DoneAtUtc,
            Note = t.Note, CreatedAtUtc = t.CreatedAtUtc,
        }).ToList();
    }

    public async Task<OutreachPostTaskDto> SavePostTaskAsync(Guid? id, OutreachPostTaskInputDto input, CancellationToken ct)
    {
        OutreachPostTask? t = id is { } existingId ? await _db.OutreachPostTasks.FirstOrDefaultAsync(x => x.Id == existingId, ct) : null;
        if (id != null && t == null) throw new KeyNotFoundException();
        if (!Uri.TryCreate((input.PostUrl ?? "").Trim(), UriKind.Absolute, out var u) || !u.Host.EndsWith("facebook.com", StringComparison.OrdinalIgnoreCase))
            throw new OutreachInputException("Cần link bài viết Facebook.");
        if (input.GroupId is { } gid && !await _db.OutreachGroups.AnyAsync(g => g.Id == gid, ct)) throw new OutreachInputException("Group không còn trong danh sách.");
        if (t == null)
        {
            t = new OutreachPostTask();
            _db.OutreachPostTasks.Add(t);
        }
        t.PostUrl = Clip(u.ToString(), 500)!;
        t.GroupId = input.GroupId;
        t.CampaignId = input.CampaignId;
        t.PostText = Clip(input.PostText, 5000);
        t.SuggestedComment = Clip(input.SuggestedComment, 2000) ?? t.SuggestedComment;
        t.Note = Clip(input.Note, 1000);
        var status = input.Status is "todo" or "done" or "skipped" ? input.Status : t.Status;
        if (status != t.Status) t.DoneAtUtc = status == "todo" ? null : DateTime.UtcNow;
        t.Status = status;
        t.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return (await ListPostTasksAsync(null, ct)).First(x => x.Id == t.Id);
    }

    public async Task<bool> DeletePostTaskAsync(Guid id, CancellationToken ct)
    {
        var t = await _db.OutreachPostTasks.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t == null) return false;
        _db.OutreachPostTasks.Remove(t);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>AI drafts 3 helpful comments for a Group post the owner pasted (they post the chosen one themselves).</summary>
    public async Task<List<string>?> SuggestCommentsAsync(Guid id, CancellationToken ct)
    {
        var t = await _db.OutreachPostTasks.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t == null) return null;
        if (string.IsNullOrWhiteSpace(t.PostText)) throw new OutreachInputException("Dán nội dung bài viết vào trước để AI đọc (Facebook không cho đọc bài trong Group qua API).");
        var group = t.GroupId is { } gid ? await _db.OutreachGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gid, ct) : null;
        var prompt = $@"Bài viết trong Group Facebook{(group != null ? $" \"{group.Name}\" (chủ đề: {group.Topic ?? "chưa rõ"})" : "")}:
---
{t.PostText}
---
{(!string.IsNullOrWhiteSpace(group?.Rules) ? $"Quy định của Group: {group!.Rules}\n" : "")}
Viết 3 bình luận KHÁC NHAU, tiếng Việt tự nhiên, 1–2 câu, có giá trị với cuộc thảo luận (đáp đúng nội dung bài, chia sẻ cảm nghĩ / kinh nghiệm / hỏi lại).
- Không spam, không chèn link, không quảng cáo lộ liễu, không hứa hẹn.
- Nếu bài là bài trao đổi tương tác (xin follow/like) thì có thể nói nhẹ nhàng đã ghé ủng hộ và mời ghé lại Page của mình.
Trả về JSON: {{ ""comments"": [""..."", ""..."", ""...""] }}";
        var result = await _gemini.GenerateJsonAsync("Bạn là người dùng Facebook Việt Nam lịch sự, chân thành. Chỉ trả về JSON hợp lệ.", prompt, 1024, 0.9, ct, null, true);
        _logger.LogInformation("Outreach AI suggested comments for post task {Id} (cost ${Cost})", id, result.CostUsd);
        var comments = ParseStrings(result.Text, "comments").Take(3).ToList();
        if (comments.Count == 0) throw new InvalidOperationException("AI không trả về bình luận. Thử lại.");
        t.SuggestedComment = Clip(comments[0], 2000);
        await _db.SaveChangesAsync(ct);
        return comments;
    }

    // ═════════════════════════ Stats ═════════════════════════

    private async Task<Dictionary<string, (int Views, int Clicks)>> CountsByContentAsync(DateTime since, CancellationToken ct)
    {
        var rows = await _db.LandingEvents.AsNoTracking()
            .Where(e => e.Slug == LandingSlug && e.UtmContent != null && e.CreatedAtUtc >= since && (e.Type == "view" || e.Type == "open_page"))
            .GroupBy(e => new { e.UtmContent, e.Type }).Select(g => new { g.Key.UtmContent, g.Key.Type, Count = g.Count() }).ToListAsync(ct);
        return rows.GroupBy(r => r.UtmContent!).ToDictionary(g => g.Key,
            g => (g.Where(x => x.Type == "view").Sum(x => x.Count), g.Where(x => x.Type == "open_page").Sum(x => x.Count)));
    }

    private static (int Views, int Clicks) SumForGroup(Dictionary<string, (int Views, int Clicks)> byContent, string key)
    {
        int v = 0, c = 0;
        foreach (var (content, n) in byContent)
            if (content == key || content.EndsWith("_" + key, StringComparison.Ordinal)) { v += n.Views; c += n.Clicks; }
        return (v, c);
    }

    public async Task<OutreachStatsDto> StatsAsync(int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, 180);
        var todayVn = (DateTime.UtcNow + VietnamOffset).Date;
        var since = todayVn.AddDays(-(days - 1)) - VietnamOffset;
        var events = await _db.LandingEvents.AsNoTracking()
            .Where(e => e.Slug == LandingSlug && e.CreatedAtUtc >= since && (e.Type == "view" || e.Type == "open_page"))
            .Select(e => new Ev(e.Type, e.CreatedAtUtc, e.Device, e.VisitorId, e.UtmSource, e.UtmCampaign, e.UtmContent, e.ReferrerHost))
            .ToListAsync(ct);
        var views = events.Where(e => e.Type == "view").ToList();
        var clicks = events.Where(e => e.Type == "open_page").ToList();

        var campaignNames = await _db.OutreachCampaigns.AsNoTracking().ToDictionaryAsync(c => c.Slug, c => c.Name, ct);
        var groupNames = await _db.OutreachGroups.AsNoTracking().ToDictionaryAsync(g => g.Key, g => g.Name, ct);

        List<OutreachBreakdownDto> Break(Func<Ev, string?> key, Func<string, string?> label) =>
            events.Select(e => new { K = key(e), e.Type }).Where(x => x.K != null)
                .GroupBy(x => x.K!).Select(g => new OutreachBreakdownDto
                {
                    Key = g.Key, Label = label(g.Key),
                    Views = g.Count(x => x.Type == "view"), Clicks = g.Count(x => x.Type == "open_page"),
                })
                .OrderByDescending(x => x.Views + x.Clicks).Take(30).ToList();

        return new OutreachStatsDto
        {
            Days = days,
            Views = views.Count,
            Clicks = clicks.Count,
            UniqueVisitors = views.Where(v => v.VisitorId != null).Select(v => v.VisitorId).Distinct().Count(),
            MobileViews = views.Count(v => v.Device == "mobile"),
            DesktopViews = views.Count(v => v.Device != "mobile"),
            PerDay = Enumerable.Range(0, days).Select(i => todayVn.AddDays(-(days - 1) + i)).Select(d => new OutreachDayDto
            {
                Date = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Views = views.Count(v => (v.CreatedAtUtc + VietnamOffset).Date == d),
                Clicks = clicks.Count(v => (v.CreatedAtUtc + VietnamOffset).Date == d),
            }).ToList(),
            Sources = Break(e => e.UtmSource ?? e.ReferrerHost ?? "(trực tiếp)", _ => null),
            Campaigns = Break(e => e.UtmCampaign, k => campaignNames.GetValueOrDefault(k)),
            Contents = Break(e => e.UtmContent, k =>
            {
                var gk = k.Contains('_') ? k[(k.LastIndexOf('_') + 1)..] : k;
                var page = k.Contains('_') ? k[..k.LastIndexOf('_')] : null;
                return groupNames.TryGetValue(gk, out var gn) ? (page != null ? $"{page} × {gn}" : gn) : null;
            }),
        };
    }

    private sealed record Ev(string Type, DateTime CreatedAtUtc, string Device, string? VisitorId, string? UtmSource, string? UtmCampaign, string? UtmContent, string? ReferrerHost);

    public async Task<OutreachOverviewDto> OverviewAsync(CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-7);
        var recent = await _db.LandingEvents.AsNoTracking()
            .Where(e => e.Slug == LandingSlug && e.CreatedAtUtc >= since && (e.Type == "view" || e.Type == "open_page"))
            .Select(e => new { e.Type, Outreach = e.UtmCampaign != null && e.UtmMedium == "group" }).ToListAsync(ct);
        return new OutreachOverviewDto
        {
            Groups = await _db.OutreachGroups.CountAsync(ct),
            GroupsFit = await _db.OutreachGroups.CountAsync(g => g.Fit == "fit", ct),
            Contents = await _db.OutreachContents.CountAsync(ct),
            Campaigns = await _db.OutreachCampaigns.CountAsync(ct),
            CampaignsRunning = await _db.OutreachCampaigns.CountAsync(c => c.Status == "Running", ct),
            PostTasksTodo = await _db.OutreachPostTasks.CountAsync(t => t.Status == "todo", ct),
            Views7d = recent.Count(e => e.Type == "view"),
            Clicks7d = recent.Count(e => e.Type == "open_page"),
            OutreachViews7d = recent.Count(e => e.Type == "view" && e.Outreach),
            OutreachClicks7d = recent.Count(e => e.Type == "open_page" && e.Outreach),
            Pages = await ListPagesAsync(false, ct),
        };
    }
}

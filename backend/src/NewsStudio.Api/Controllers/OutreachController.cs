using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NewsStudio.Application.DTOs;
using NewsStudio.Infrastructure.Outreach;

namespace NewsStudio.Api.Controllers;

internal static class OutreachHttp
{
    public static string ClientIp(HttpContext http)
    {
        var forwarded = http.Request.Headers["X-Forwarded-For"].ToString();
        return !string.IsNullOrWhiteSpace(forwarded) ? forwarded.Split(',')[0].Trim() : http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

/// <summary>Every "Chiến dịch kéo tương tác" call needs a valid campaign session (header X-Outreach-Session).</summary>
public class OutreachSessionFilter : IAsyncAuthorizationFilter
{
    private readonly OutreachAuth _auth;
    public OutreachSessionFilter(OutreachAuth auth) => _auth = auth;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var token = context.HttpContext.Request.Headers[OutreachAuth.SessionHeader].ToString();
        if (!await _auth.ValidateAsync(token, context.HttpContext.RequestAborted))
            context.Result = new ObjectResult(new { error = "Phiên chiến dịch đã hết hạn — nhập lại mật khẩu chiến dịch.", needsOutreachUnlock = true })
            {
                StatusCode = StatusCodes.Status401Unauthorized,
            };
    }
}

/// <summary>Unlocking the campaign menu (the only outreach calls that work without a session).</summary>
[ApiController]
[Route("api/outreach/auth")]
[Produces("application/json")]
public class OutreachAuthController : ControllerBase
{
    private readonly OutreachAuth _auth;
    public OutreachAuthController(OutreachAuth auth) => _auth = auth;

    [HttpGet("status")]
    public async Task<ActionResult<object>> Status(CancellationToken ct)
    {
        var s = await _auth.StatusAsync(false, ct);
        var valid = await _auth.ValidateAsync(Request.Headers[OutreachAuth.SessionHeader].ToString(), ct);
        return Ok(new { s.PasswordConfigured, s.SessionHours, sessionValid = valid });
    }

    [HttpPost("unlock")]
    public async Task<ActionResult<OutreachUnlockResultDto>> Unlock([FromBody] OutreachUnlockRequestDto body, CancellationToken ct)
    {
        var result = await _auth.UnlockAsync(body?.Password, OutreachHttp.ClientIp(HttpContext), ct);
        if (result.Ok) return Ok(result);
        return StatusCode(result.LockedUntilUtc != null ? StatusCodes.Status429TooManyRequests : StatusCodes.Status401Unauthorized, result);
    }
}

/// <summary>
/// "Chiến dịch kéo tương tác": Groups, invitation texts, campaigns, Group posts, helper Pages, numbers, security.
/// Manual-first: nothing here posts, comments or joins on Facebook.
/// </summary>
[ApiController]
[Route("api/outreach")]
[Produces("application/json")]
[TypeFilter(typeof(OutreachSessionFilter))]
public class OutreachController : ControllerBase
{
    private readonly OutreachService _svc;
    private readonly OutreachAuth _auth;
    private readonly OutreachAiJobs _ai;
    private readonly ILogger<OutreachController> _logger;

    public OutreachController(OutreachService svc, OutreachAuth auth, OutreachAiJobs ai, ILogger<OutreachController> logger)
    {
        _svc = svc;
        _auth = auth;
        _ai = ai;
        _logger = logger;
    }

    private async Task<ActionResult> Run<T>(Func<Task<T?>> action)
    {
        try
        {
            var result = await action();
            return result == null ? NotFound(new { error = "Không tìm thấy." }) : Ok(result);
        }
        catch (OutreachInputException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Không tìm thấy." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Outreach AI / Meta call failed: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
    }

    private async Task<ActionResult> Done(Func<Task<bool>> action) => await action() ? NoContent() : NotFound(new { error = "Không tìm thấy." });

    // ── overview / pages ──
    [HttpGet("overview")] public Task<ActionResult> Overview(CancellationToken ct) => Run(() => _svc.OverviewAsync(ct))!;
    [HttpGet("pages")] public Task<ActionResult> Pages([FromQuery] bool refresh = false, CancellationToken ct = default) => Run(() => _svc.ListPagesAsync(refresh, ct))!;

    // ── groups ──
    [HttpGet("groups")] public Task<ActionResult> Groups(CancellationToken ct) => Run(() => _svc.ListGroupsAsync(ct))!;
    [HttpPost("groups")] public Task<ActionResult> AddGroup([FromBody] OutreachGroupInputDto input, CancellationToken ct) => Run(() => _svc.CreateGroupAsync(input, ct))!;
    [HttpPut("groups/{id:guid}")] public Task<ActionResult> EditGroup(Guid id, [FromBody] OutreachGroupInputDto input, CancellationToken ct) => Run(() => _svc.UpdateGroupAsync(id, input, ct));
    [HttpDelete("groups/{id:guid}")] public Task<ActionResult> DeleteGroup(Guid id, CancellationToken ct) => Done(() => _svc.DeleteGroupAsync(id, ct));
    [HttpPost("groups/import")] public Task<ActionResult> Import([FromBody] OutreachCsvDto body, CancellationToken ct) => Run(() => _svc.ImportGroupsCsvAsync(body?.Csv ?? "", ct))!;
    [HttpGet("groups/discover")] public Task<ActionResult> Discover([FromQuery] string keyword, [FromQuery] int page = 1, CancellationToken ct = default) => Run(() => _svc.DiscoverGroupsAsync(keyword, page, ct))!;
    [HttpPut("groups/{id:guid}/membership/{pageId:guid}")] public Task<ActionResult> Membership(Guid id, Guid pageId, [FromBody] OutreachMembershipInputDto body, CancellationToken ct) => Run(() => _svc.SetMembershipAsync(id, pageId, body?.Status, ct));
    [HttpGet("groups/search")] public Task<ActionResult> Search([FromQuery] string keyword) => Run(() => Task.FromResult<OutreachGroupSearchDto?>(OutreachService.GroupSearch(keyword)));

    // ── contents / AI ──
    [HttpGet("contents")] public Task<ActionResult> Contents(CancellationToken ct) => Run(() => _svc.ListContentsAsync(ct))!;
    [HttpPost("contents")] public Task<ActionResult> SaveContent([FromBody] OutreachContentInputDto input, CancellationToken ct) => Run(() => _svc.SaveContentAsync(input, ct))!;
    [HttpDelete("contents/{id:guid}")] public Task<ActionResult> DeleteContent(Guid id, CancellationToken ct) => Done(() => _svc.DeleteContentAsync(id, ct));
    [HttpPost("contents/{id:guid}/copied")] public Task<ActionResult> Copied(Guid id, CancellationToken ct) => Done(() => _svc.MarkCopiedAsync(id, ct));
    // AI writers run in the background (a Gemini answer can outlast the proxy's request time): start, then poll.
    [HttpPost("contents/variants")]
    public ActionResult Variants([FromBody] OutreachVariantsRequestDto input)
    {
        var request = input ?? new();
        return Ok(new { jobId = _ai.Start("variants", (svc, token) => svc.WriteVariantsAsync(request, token)) });
    }
    [HttpPost("contents/write")]
    public ActionResult Write([FromBody] OutreachWriteRequestDto input)
    {
        var request = input ?? new();
        return Ok(new { jobId = _ai.Start("write", (svc, token) => svc.WriteAsync(request, token)) });
    }
    [HttpGet("ai-jobs/{id:guid}")]
    public ActionResult AiJob(Guid id) => _ai.Get(id) is { } job ? Ok(job) : NotFound(new { error = "Không thấy yêu cầu AI (máy chủ vừa khởi động lại?) — bấm viết lại nhé." });
    [HttpPost("contents/check")] public Task<ActionResult> Check([FromBody] OutreachTextCheckDto input, CancellationToken ct) => Run(() => _svc.CheckTextAsync(input?.Text ?? "", ct))!;

    // ── campaigns ──
    [HttpGet("campaigns")] public Task<ActionResult> Campaigns(CancellationToken ct) => Run(() => _svc.ListCampaignsAsync(ct))!;
    [HttpPost("campaigns")] public Task<ActionResult> AddCampaign([FromBody] OutreachCampaignInputDto input, CancellationToken ct) => Run(() => _svc.CreateCampaignAsync(input, ct))!;
    [HttpPut("campaigns/{id:guid}")] public Task<ActionResult> EditCampaign(Guid id, [FromBody] OutreachCampaignInputDto input, CancellationToken ct) => Run(() => _svc.UpdateCampaignAsync(id, input, ct));
    [HttpDelete("campaigns/{id:guid}")] public Task<ActionResult> DeleteCampaign(Guid id, CancellationToken ct) => Done(() => _svc.DeleteCampaignAsync(id, ct));
    [HttpGet("campaigns/{id:guid}/links")] public Task<ActionResult> Links(Guid id, CancellationToken ct) => Run(() => _svc.CampaignLinksAsync(id, ct));
    [HttpPost("campaigns/{id:guid}/activities")] public Task<ActionResult> AddActivity(Guid id, [FromBody] OutreachActivityInputDto input, CancellationToken ct) => Run(() => _svc.AddActivityAsync(id, input, ct));
    [HttpDelete("activities/{id:guid}")] public Task<ActionResult> DeleteActivity(Guid id, CancellationToken ct) => Done(() => _svc.DeleteActivityAsync(id, ct));

    // ── wall posts on helper Pages + shares into Groups ──
    [HttpPost("wall-posts")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult> PublishWallPost([FromForm] Guid pageId, [FromForm] Guid? campaignId, [FromForm] string? message, IFormFile? image, CancellationToken ct)
    {
        // picture post when an image comes with it, text-only post otherwise
        if (image == null || image.Length == 0)
            return await Run(() => _svc.PublishWallPostAsync(pageId, campaignId, message, null, null, ct))!;
        await using var stream = image.OpenReadStream();
        return await Run(() => _svc.PublishWallPostAsync(pageId, campaignId, message, stream, image.FileName, ct))!;
    }
    [HttpGet("wall-posts")] public Task<ActionResult> WallPosts([FromQuery] Guid? pageId, CancellationToken ct) => Run(() => _svc.ListWallPostsAsync(pageId, ct))!;
    [HttpPut("wall-posts/{id:guid}/shares/{groupId:guid}")] public Task<ActionResult> Share(Guid id, Guid groupId, [FromBody] OutreachShareInputDto body, CancellationToken ct) => Run(() => _svc.SetShareAsync(id, groupId, body?.Shared ?? true, ct));

    // ── Group posts ──
    [HttpGet("posts")] public Task<ActionResult> Posts([FromQuery] string? status, CancellationToken ct) => Run(() => _svc.ListPostTasksAsync(status, ct))!;
    [HttpPost("posts")] public Task<ActionResult> AddPost([FromBody] OutreachPostTaskInputDto input, CancellationToken ct) => Run(() => _svc.SavePostTaskAsync(null, input, ct))!;
    [HttpPut("posts/{id:guid}")] public Task<ActionResult> EditPost(Guid id, [FromBody] OutreachPostTaskInputDto input, CancellationToken ct) => Run(() => _svc.SavePostTaskAsync(id, input, ct))!;
    [HttpDelete("posts/{id:guid}")] public Task<ActionResult> DeletePost(Guid id, CancellationToken ct) => Done(() => _svc.DeletePostTaskAsync(id, ct));
    [HttpPost("posts/{id:guid}/suggest")] public Task<ActionResult> Suggest(Guid id, CancellationToken ct) => Run(() => _svc.SuggestCommentsAsync(id, ct));

    // ── numbers ──
    [HttpGet("stats")] public Task<ActionResult> Stats([FromQuery] int days = 14, CancellationToken ct = default) => Run(() => _svc.StatsAsync(days, ct))!;

    /// <summary>Deletes every visit / click of the public page (start counting again). Needs body { "confirm": "XOA" }.</summary>
    [HttpPost("stats/reset")]
    public async Task<ActionResult> ResetStats([FromBody] OutreachResetDto body, CancellationToken ct)
    {
        if (body?.Confirm != "XOA") return BadRequest(new { error = "Thiếu xác nhận xoá." });
        var (views, clicks) = await _svc.ResetLandingStatsAsync(ct);
        return Ok(new { deletedViews = views, deletedClicks = clicks });
    }

    // ── security ──
    [HttpGet("security")] public Task<ActionResult> Security(CancellationToken ct) => Run(() => _auth.StatusAsync(true, ct))!;

    [HttpPut("security")]
    public async Task<ActionResult> ChangeSecurity([FromBody] OutreachChangePasswordDto input, CancellationToken ct)
    {
        var error = await _auth.ChangePasswordAsync(input ?? new(), ct);
        return error == null ? NoContent() : BadRequest(new { error });
    }

    [HttpPost("security/lock-all")]
    public async Task<ActionResult> LockAll(CancellationToken ct)
    {
        await _auth.LockEverywhereAsync(ct);
        return NoContent();
    }
}

public class OutreachCsvDto { public string Csv { get; set; } = string.Empty; }

public class OutreachResetDto { public string? Confirm { get; set; } }

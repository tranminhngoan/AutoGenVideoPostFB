using System.Text;
using Microsoft.AspNetCore.Mvc;
using NewsStudio.Application.DTOs;
using NewsStudio.Infrastructure.Growth;

namespace NewsStudio.Api.Controllers;

/// <summary>
/// Chiến dịch tăng trưởng (Meta Growth Campaign). Every data call needs the studio key (<see cref="StudioAccess"/>),
/// reads included. Pages come from the existing connection; nothing here returns a token. Child resources are always
/// looked up through the campaign / Page in the URL (an id from elsewhere is "not found"). Only the campaign's own
/// uploaded pictures are served without the key (random ids, for &lt;img&gt; tags), like the studio's media.
/// </summary>
[ApiController]
[Route("api/growth")]
public class GrowthController : ControllerBase
{
    private const long MaxUploadBytes = GrowthContentService.MaxVideoBytes + 1024 * 1024;

    private readonly GrowthCampaignService _campaigns;
    private readonly GrowthContentService _content;
    private readonly GrowthInsightsService _insights;
    private readonly GrowthAiService _ai;
    private readonly GrowthReportService _reports;
    private readonly GrowthCompetitorService _competitors;
    private readonly GrowthCapabilityService _capabilities;
    private readonly IConfiguration _configuration;

    public GrowthController(GrowthCampaignService campaigns, GrowthContentService content, GrowthInsightsService insights, GrowthAiService ai,
        GrowthReportService reports, GrowthCompetitorService competitors, GrowthCapabilityService capabilities, IConfiguration configuration)
    {
        _campaigns = campaigns;
        _content = content;
        _insights = insights;
        _ai = ai;
        _reports = reports;
        _competitors = competitors;
        _capabilities = capabilities;
        _configuration = configuration;
    }

    private async Task<IActionResult> Run(Func<Task<object?>> action)
    {
        if (StudioAccess.Denied(Request, _configuration) is { } denied) return denied;
        try
        {
            var result = await action();
            return result == null ? NoContent() : Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    // ───────────── pages ─────────────

    [HttpGet("pages")]
    public Task<IActionResult> Pages(CancellationToken ct) => Run(async () => await _campaigns.PagesAsync(ct));

    [HttpGet("pages/{pageId:guid}/capabilities")]
    public Task<IActionResult> Capabilities(Guid pageId, CancellationToken ct) => Run(async () => await _capabilities.CheckAsync(pageId, ct));

    // ───────────── campaigns ─────────────

    [HttpGet("campaigns")]
    public Task<IActionResult> Campaigns([FromQuery] Guid? pageId, CancellationToken ct) => Run(async () => await _campaigns.ListAsync(pageId, ct));

    [HttpPost("campaigns")]
    public Task<IActionResult> CreateCampaign([FromBody] GrowthCampaignInputDto input, CancellationToken ct) => Run(async () => await _campaigns.CreateAsync(input, ct));

    [HttpGet("campaigns/{id:guid}")]
    public Task<IActionResult> Campaign(Guid id, CancellationToken ct) => Run(async () => await _campaigns.GetAsync(id, ct));

    [HttpPut("campaigns/{id:guid}")]
    public Task<IActionResult> UpdateCampaign(Guid id, [FromBody] GrowthCampaignInputDto input, CancellationToken ct) => Run(async () => await _campaigns.UpdateAsync(id, input, ct));

    [HttpDelete("campaigns/{id:guid}")]
    public Task<IActionResult> DeleteCampaign(Guid id, CancellationToken ct) => Run(async () => { await _campaigns.DeleteAsync(id, ct); return null; });

    [HttpPost("campaigns/{id:guid}/start")]
    public Task<IActionResult> Start(Guid id, CancellationToken ct) => Run(async () => await _campaigns.StartAsync(id, ct));

    [HttpPost("campaigns/{id:guid}/pause")]
    public Task<IActionResult> Pause(Guid id, CancellationToken ct) => Run(async () => await _campaigns.PauseAsync(id, ct));

    [HttpPost("campaigns/{id:guid}/resume")]
    public Task<IActionResult> Resume(Guid id, CancellationToken ct) => Run(async () => await _campaigns.ResumeAsync(id, ct));

    [HttpPost("campaigns/{id:guid}/complete")]
    public Task<IActionResult> Complete(Guid id, CancellationToken ct) => Run(async () => await _campaigns.CompleteAsync(id, "user", ct));

    [HttpPost("campaigns/{id:guid}/cancel")]
    public Task<IActionResult> Cancel(Guid id, CancellationToken ct) => Run(async () => await _campaigns.CancelAsync(id, ct));

    [HttpPut("campaigns/{id:guid}/checklist")]
    public Task<IActionResult> Checklist(Guid id, [FromBody] GrowthChecklistUpdateDto input, CancellationToken ct) => Run(async () => await _campaigns.SetChecklistAsync(id, input, ct));

    [HttpPut("campaigns/{id:guid}/manual-metrics")]
    public Task<IActionResult> ManualMetrics(Guid id, [FromBody] GrowthManualMetricsDto input, CancellationToken ct) => Run(async () => await _campaigns.SetManualMetricsAsync(id, input, ct));

    [HttpGet("campaigns/{id:guid}/logs")]
    public Task<IActionResult> Logs(Guid id, CancellationToken ct) => Run(async () => await _campaigns.LogsAsync(id, ct));

    // ───────────── overview & analytics ─────────────

    [HttpGet("campaigns/{id:guid}/overview")]
    public Task<IActionResult> Overview(Guid id, CancellationToken ct) => Run(async () => await _insights.OverviewAsync(id, ct));

    [HttpPost("campaigns/{id:guid}/sync")]
    public Task<IActionResult> Sync(Guid id, CancellationToken ct) => Run(async () => await _insights.SyncAsync(id, ct));

    [HttpGet("campaigns/{id:guid}/analytics/posts")]
    public Task<IActionResult> AnalyticsPosts(Guid id, CancellationToken ct) => Run(async () => await _insights.PostsAsync(id, ct));

    [HttpGet("campaigns/{id:guid}/analytics/ranking")]
    public Task<IActionResult> Ranking(Guid id, CancellationToken ct) => Run(async () => await _insights.RankingAsync(id, ct));

    [HttpGet("campaigns/{id:guid}/analytics/hours")]
    public Task<IActionResult> Hours(Guid id, CancellationToken ct) => Run(async () => await _insights.HoursAsync(id, ct));

    [HttpGet("campaigns/{id:guid}/analytics/followers")]
    public Task<IActionResult> Followers(Guid id, CancellationToken ct) => Run(async () => await _insights.FollowersAsync(id, ct));

    // ───────────── content calendar ─────────────

    [HttpGet("campaigns/{id:guid}/contents")]
    public Task<IActionResult> Contents(Guid id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? kind, [FromQuery] string? status, CancellationToken ct) =>
        Run(async () => await _content.ListAsync(id, from?.ToUniversalTime(), to?.ToUniversalTime(), kind, status, ct));

    [HttpPost("campaigns/{id:guid}/contents")]
    public Task<IActionResult> CreateContent(Guid id, [FromBody] GrowthContentInputDto input, CancellationToken ct) => Run(async () => await _content.CreateAsync(id, input, ct));

    [HttpGet("campaigns/{id:guid}/contents/{cid:guid}")]
    public Task<IActionResult> Content(Guid id, Guid cid, CancellationToken ct) => Run(async () => await _content.GetAsync(id, cid, ct));

    [HttpPut("campaigns/{id:guid}/contents/{cid:guid}")]
    public Task<IActionResult> UpdateContent(Guid id, Guid cid, [FromBody] GrowthContentInputDto input, CancellationToken ct) => Run(async () => await _content.UpdateAsync(id, cid, input, ct));

    [HttpDelete("campaigns/{id:guid}/contents/{cid:guid}")]
    public Task<IActionResult> DeleteContent(Guid id, Guid cid, CancellationToken ct) => Run(async () => { await _content.DeleteAsync(id, cid, ct); return null; });

    [HttpPost("campaigns/{id:guid}/contents/{cid:guid}/submit")]
    public Task<IActionResult> Submit(Guid id, Guid cid, CancellationToken ct) => Run(async () => await _content.SubmitAsync(id, cid, ct));

    [HttpPost("campaigns/{id:guid}/contents/{cid:guid}/approve")]
    public Task<IActionResult> Approve(Guid id, Guid cid, CancellationToken ct) => Run(async () => await _content.ApproveAsync(id, cid, ct));

    [HttpPost("campaigns/{id:guid}/contents/{cid:guid}/cancel")]
    public Task<IActionResult> CancelContent(Guid id, Guid cid, CancellationToken ct) => Run(async () => await _content.CancelAsync(id, cid, ct));

    [HttpPost("campaigns/{id:guid}/contents/{cid:guid}/publish-now")]
    public Task<IActionResult> PublishNow(Guid id, Guid cid, CancellationToken ct) => Run(async () => await _content.PublishNowAsync(id, cid, ct));

    [HttpPost("campaigns/{id:guid}/contents/{cid:guid}/retry")]
    public Task<IActionResult> Retry(Guid id, Guid cid, CancellationToken ct) => Run(async () => await _content.RetryAsync(id, cid, ct));

    [HttpPost("campaigns/{id:guid}/contents/{cid:guid}/mark-manual")]
    public Task<IActionResult> MarkManual(Guid id, Guid cid, [FromBody] GrowthMarkManualDto input, CancellationToken ct) => Run(async () => await _content.MarkManualAsync(id, cid, input, ct));

    [HttpGet("campaigns/{id:guid}/sources")]
    public Task<IActionResult> Sources(Guid id, CancellationToken ct) => Run(async () => await _content.SourcesAsync(id, ct));

    // ───────────── assets ─────────────

    [HttpGet("campaigns/{id:guid}/assets")]
    public Task<IActionResult> Assets(Guid id, CancellationToken ct) => Run(async () => await _content.AssetsAsync(id, ct));

    [HttpPost("campaigns/{id:guid}/assets")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Upload(Guid id, [FromForm] UploadForm form, CancellationToken ct)
    {
        var file = form.File;
        var source = form.Source;
        if (StudioAccess.Denied(Request, _configuration) is { } denied) return denied;
        if (file == null || file.Length == 0) return BadRequest(new { message = "Chưa chọn tệp." });
        if (file.Length > GrowthContentService.MaxVideoBytes) return BadRequest(new { message = $"Tệp tối đa {GrowthContentService.MaxVideoBytes / 1048576} MB." });
        var temp = Path.Combine(Path.GetTempPath(), "growth-upload-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var fs = System.IO.File.Create(temp)) await file.CopyToAsync(fs, ct);
            return await Run(async () => await _content.UploadAsync(id, temp, file.FileName, source ?? "Upload", ct));
        }
        finally
        {
            try { System.IO.File.Delete(temp); } catch { /* best effort */ }
        }
    }

    public class UploadForm
    {
        public IFormFile? File { get; set; }
        /// <summary>Upload or Template.</summary>
        public string? Source { get; set; }
    }

    [HttpDelete("campaigns/{id:guid}/assets/{assetId:guid}")]
    public Task<IActionResult> DeleteAsset(Guid id, Guid assetId, CancellationToken ct) => Run(async () => { await _content.DeleteAssetAsync(id, assetId, ct); return null; });

    /// <summary>The picture / video of a campaign asset (random id, for &lt;img&gt; / &lt;video&gt;); only growth assets are served here.</summary>
    [HttpGet("assets/{assetId:guid}/file")]
    public async Task<IActionResult> AssetFile(Guid assetId, CancellationToken ct)
    {
        if (await _content.AssetFileAsync(assetId, ct) is not { } f) return NotFound();
        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(f.Path, f.Mime, enableRangeProcessing: true);
    }

    // ───────────── AI ─────────────

    [HttpPost("campaigns/{id:guid}/ai/captions")]
    public Task<IActionResult> Captions(Guid id, [FromBody] GrowthCaptionRequestDto input, CancellationToken ct) => Run(async () => await _ai.CaptionsAsync(id, input, ct));

    [HttpPost("campaigns/{id:guid}/ai/repurpose")]
    public Task<IActionResult> Repurpose(Guid id, [FromBody] GrowthRepurposeRequestDto input, CancellationToken ct) => Run(async () => await _ai.RepurposeAsync(id, input, ct));

    [HttpPost("campaigns/{id:guid}/ai/suggestions")]
    public Task<IActionResult> Suggestions(Guid id, [FromQuery] bool ai = false, CancellationToken ct = default) => Run(async () => await _ai.SuggestionsAsync(id, ai, ct));

    // ───────────── report ─────────────

    [HttpGet("campaigns/{id:guid}/report")]
    public Task<IActionResult> Report(Guid id, CancellationToken ct) => Run(async () => await _reports.LatestAsync(id, ct));

    [HttpPost("campaigns/{id:guid}/report")]
    public Task<IActionResult> GenerateReport(Guid id, [FromQuery] bool ai = false, CancellationToken ct = default) => Run(async () => await _reports.GenerateAsync(id, ai, ct));

    [HttpGet("campaigns/{id:guid}/report.csv")]
    public Task<IActionResult> ReportCsv(Guid id, CancellationToken ct) => Export(id, "csv", ct);

    [HttpGet("campaigns/{id:guid}/report.xls")]
    public Task<IActionResult> ReportXls(Guid id, CancellationToken ct) => Export(id, "xls", ct);

    private async Task<IActionResult> Export(Guid id, string format, CancellationToken ct)
    {
        if (StudioAccess.Denied(Request, _configuration) is { } denied) return denied;
        try
        {
            var (report, posts) = await _reports.ExportDataAsync(id, ct);
            var name = $"bao-cao-chien-dich-{report.Data.StartDate:yyyyMMdd}";
            if (format == "csv")
            {
                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(GrowthReportService.Csv(report, posts))).ToArray(); // BOM: Excel reads UTF-8
                return File(bytes, "text/csv; charset=utf-8", name + ".csv");
            }
            return File(Encoding.UTF8.GetBytes(GrowthReportService.SpreadsheetXml(report, posts)), "application/vnd.ms-excel", name + ".xls");
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
    }

    // ───────────── competitors ─────────────

    [HttpGet("competitors")]
    public Task<IActionResult> Competitors([FromQuery] Guid pageId, CancellationToken ct) => Run(async () => await _competitors.ListAsync(pageId, ct));

    [HttpPost("competitors")]
    public Task<IActionResult> CreateCompetitor([FromBody] GrowthCompetitorInputDto input, CancellationToken ct) => Run(async () => await _competitors.CreateAsync(input, ct));

    [HttpPut("competitors/{id:guid}")]
    public Task<IActionResult> UpdateCompetitor(Guid id, [FromBody] GrowthCompetitorInputDto input, CancellationToken ct) => Run(async () => await _competitors.UpdateAsync(id, input, ct));

    [HttpDelete("competitors/{id:guid}")]
    public Task<IActionResult> DeleteCompetitor(Guid id, [FromQuery] Guid pageId, CancellationToken ct) => Run(async () => { await _competitors.DeleteAsync(id, pageId, ct); return null; });

    [HttpPost("competitors/{id:guid}/snapshots")]
    public Task<IActionResult> AddSnapshot(Guid id, [FromQuery] Guid pageId, [FromBody] GrowthCompetitorSnapshotInputDto input, CancellationToken ct) =>
        Run(async () => await _competitors.AddSnapshotAsync(id, pageId, input, ct));

    [HttpDelete("competitors/{id:guid}/snapshots/{snapshotId:guid}")]
    public Task<IActionResult> DeleteSnapshot(Guid id, Guid snapshotId, [FromQuery] Guid pageId, CancellationToken ct) =>
        Run(async () => { await _competitors.DeleteSnapshotAsync(id, pageId, snapshotId, ct); return null; });

    [HttpPost("competitors/analyze")]
    public Task<IActionResult> AnalyzeCompetitors([FromQuery] Guid pageId, [FromQuery] Guid? campaignId, [FromQuery] bool ai = false, CancellationToken ct = default) =>
        Run(async () => await _competitors.AnalyzeAsync(pageId, campaignId, ai, ct));
}

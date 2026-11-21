using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Api.Controllers;

[ApiController]
[Route("api/film-factory")]
public class FilmFactoryController : ControllerBase
{
    private readonly IFilmFactoryService _filmFactoryService;
    private readonly IFilmProductionCoordinator _productionCoordinator;
    private readonly IFilmRendererService _rendererService;
    private readonly IConfiguration _configuration;

    public FilmFactoryController(
        IFilmFactoryService filmFactoryService,
        IFilmProductionCoordinator productionCoordinator,
        IFilmRendererService rendererService,
        IConfiguration configuration)
    {
        _filmFactoryService = filmFactoryService;
        _productionCoordinator = productionCoordinator;
        _rendererService = rendererService;
        _configuration = configuration;
    }

    [HttpGet("pages")]
    public async Task<ActionResult<List<FilmPageDto>>> GetPages(CancellationToken ct)
    {
        var pages = await _filmFactoryService.GetFilmPagesAsync(ct);
        return Ok(pages);
    }

    [HttpGet("pages/{id:guid}")]
    public async Task<ActionResult<FilmPageDto>> GetPageById(Guid id, CancellationToken ct)
    {
        var page = await _filmFactoryService.GetFilmPageByIdAsync(id, ct);
        if (page == null) return NotFound(new { message = "Không tìm thấy Film Page." });
        return Ok(page);
    }

    [HttpPut("pages/{id:guid}")]
    public async Task<ActionResult<FilmPageDto>> UpdatePage(Guid id, [FromBody] UpdateFilmPageRequestDto request, CancellationToken ct)
    {
        try
        {
            var updated = await _filmFactoryService.UpdateFilmPageAsync(id, request, ct);
            return Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Kết nối trực tiếp một Facebook Page thật cho Kênh Phim (tương tự như cài đặt Page cho tin tức).
    /// </summary>
    [HttpPost("pages/connect-facebook")]
    public async Task<ActionResult<FilmPageDto>> ConnectFacebookPage(
        [FromBody] ConnectFacebookPageRequestDto request,
        [FromServices] IFacebookPublishingService facebookService,
        CancellationToken ct)
    {
        try
        {
            var fbAccount = await facebookService.ConnectPageAsync(request, ct);
            var pages = await _filmFactoryService.GetFilmPagesAsync(ct);
            var page = pages.FirstOrDefault(p => p.LinkedFacebookAccountId == fbAccount.Id)
                       ?? pages.FirstOrDefault();
            return Ok(page);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("projects")]
    public async Task<ActionResult<List<FilmProjectDto>>> GetProjects([FromQuery] Guid? pageId, CancellationToken ct)
    {
        var projects = await _filmFactoryService.GetProjectsAsync(pageId, ct);
        return Ok(projects);
    }

    [HttpGet("projects/{id:guid}")]
    public async Task<ActionResult<FilmProjectDetailDto>> GetProjectById(Guid id, CancellationToken ct)
    {
        var project = await _filmFactoryService.GetProjectByIdAsync(id, ct);
        if (project == null) return NotFound(new { message = "Không tìm thấy dự án phim." });
        return Ok(project);
    }

    [HttpPost("projects")]
    public async Task<ActionResult<FilmProjectDto>> CreateProject([FromBody] CreateFilmProjectRequestDto request, CancellationToken ct)
    {
        try
        {
            var project = await _filmFactoryService.CreateProjectAsync(request, ct);

            // Tự động kích hoạt quy trình sản xuất phim nếu bật AutoProduceAll
            if (request.AutoProduceAll)
            {
                await _productionCoordinator.TriggerFullAutoProductionAsync(project.Id, ct);
            }

            return CreatedAtAction(nameof(GetProjectById), new { id = project.Id }, project);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("projects/{id:guid}")]
    public async Task<IActionResult> DeleteProject(Guid id, CancellationToken ct)
    {
        var success = await _filmFactoryService.DeleteProjectAsync(id, ct);
        if (!success) return NotFound(new { message = "Không tìm thấy dự án phim." });
        return NoContent();
    }

    [HttpPost("projects/{id:guid}/produce")]
    public async Task<IActionResult> ProduceProject(Guid id, CancellationToken ct)
    {
        var project = await _filmFactoryService.GetProjectByIdAsync(id, ct);
        if (project == null) return NotFound(new { message = "Không tìm thấy dự án phim." });

        await _productionCoordinator.TriggerFullAutoProductionAsync(id, ct);
        return Accepted(new { message = "Đã kích hoạt quy trình sản xuất tự động cho series 10 tập.", projectId = id });
    }

    [HttpGet("projects/{id:guid}/cost")]
    public async Task<ActionResult<FilmCostSummaryDto>> GetCostSummary(Guid id, CancellationToken ct)
    {
        var cost = await _filmFactoryService.GetCostSummaryAsync(id, ct);
        return Ok(cost);
    }

    [HttpGet("episodes/{id:guid}")]
    public async Task<ActionResult<FilmEpisodeDetailDto>> GetEpisodeDetail(Guid id, CancellationToken ct)
    {
        var ep = await _filmFactoryService.GetEpisodeDetailAsync(id, ct);
        if (ep == null) return NotFound(new { message = "Không tìm thấy tập phim." });
        return Ok(ep);
    }

    /// <summary>
    /// Queues the episode for rendering (script if missing → AI frames → voices → MP4). Takes several
    /// minutes, so it runs in the background; poll the episode status.
    /// </summary>
    [HttpPost("episodes/{id:guid}/render")]
    public async Task<IActionResult> RenderEpisode(Guid id, [FromQuery] bool force = false, CancellationToken ct = default)
    {
        var ep = await _filmFactoryService.GetEpisodeDetailAsync(id, ct);
        if (ep == null) return NotFound(new { message = "Không tìm thấy tập phim." });
        try
        {
            await _productionCoordinator.TriggerEpisodeRenderAsync(id, force, ct);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        return Accepted(new { message = $"Đã đưa tập {ep.EpisodeNumber} vào hàng đợi dựng phim.", episodeId = id });
    }

    // Serving never starts a render: rendering costs money and minutes, a <video> request must not trigger it.
    [HttpGet("episodes/{id:guid}/video")]
    [HttpHead("episodes/{id:guid}/video")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StreamEpisodeVideo(Guid id, CancellationToken ct)
    {
        var ep = await _filmFactoryService.GetEpisodeDetailAsync(id, ct);
        if (ep == null) return NotFound(new { message = "Không tìm thấy tập phim." });

        var filePath = ResolveEpisodeVideoPath(ep);
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
        {
            return NotFound(new { message = "Chưa có file video cho tập này (hoặc máy chủ đã khởi động lại và xoá file tạm) — bấm \"Dựng tập\" để dựng." });
        }

        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, "video/mp4", enableRangeProcessing: true);
    }

    [HttpGet("episodes/{id:guid}/download")]
    public async Task<IActionResult> DownloadEpisodeVideo(Guid id, CancellationToken ct)
    {
        var ep = await _filmFactoryService.GetEpisodeDetailAsync(id, ct);
        if (ep == null) return NotFound(new { message = "Không tìm thấy tập phim." });

        var filePath = ResolveEpisodeVideoPath(ep);
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
        {
            return NotFound(new { message = "Chưa có file video hoàn chỉnh để tải về." });
        }

        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var cleanTitle = string.Join("_", (ep.Title ?? $"Tap_{ep.EpisodeNumber}").Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
        var fileName = $"Tap_{ep.EpisodeNumber}_{cleanTitle}.mp4";
        return File(stream, "video/mp4", fileName, enableRangeProcessing: true);
    }

    private string? ResolveEpisodeVideoPath(FilmEpisodeDto ep)
    {
        if (!string.IsNullOrWhiteSpace(ep.OutputVideoPath) && System.IO.File.Exists(ep.OutputVideoPath))
        {
            return ep.OutputVideoPath;
        }

        var tempRoot = NewsStudio.Infrastructure.Common.TempStorage.GetRoot(_configuration);
        var candidate = Path.Combine(tempRoot, "film_factory", "episodes", $"episode_{ep.EpisodeNumber}_{ep.Id}.mp4");
        if (System.IO.File.Exists(candidate))
        {
            return candidate;
        }

        return null;
    }

    [HttpGet("gpu/status")]
    public async Task<IActionResult> GetGpuStatus(CancellationToken ct)
    {
        var runpod = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.RunPodGpuBackend>();
        if (runpod == null) return Ok(new { configured = false, message = "RunPod backend chưa được đăng ký." });

        var baseUrl = await runpod.GetEffectiveBaseUrlAsync(ct);
        var podId = NewsStudio.Infrastructure.FilmFactory.RunPodGpuBackend.GetRuntimePodId() ?? runpod.PodId;
        var hasApiKey = !string.IsNullOrWhiteSpace(runpod.ApiKey);

        bool online = false;
        string? pingDetail = null;
        string? gpu = null;
        double? vramGb = null;
        double? diskFreeGb = null;
        double? diskTotalGb = null;
        string? currentBatch = null;
        string? batchDetail = null;
        int queuedBatches = 0;

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var res = await client.GetAsync($"{baseUrl}/", ct);
                online = res.IsSuccessStatusCode;
                if (online)
                {
                    pingDetail = await res.Content.ReadAsStringAsync(ct);
                    using var doc = System.Text.Json.JsonDocument.Parse(pingDetail);
                    var r = doc.RootElement;
                    if (r.TryGetProperty("gpu", out var g)) gpu = g.GetString();
                    if (r.TryGetProperty("vram_gb", out var v)) vramGb = v.GetDouble();
                    if (r.TryGetProperty("disk_free_gb", out var df)) diskFreeGb = df.GetDouble();
                    if (r.TryGetProperty("disk_total_gb", out var dt)) diskTotalGb = dt.GetDouble();
                    if (r.TryGetProperty("current_batch", out var cb)) currentBatch = cb.GetString();
                    if (r.TryGetProperty("queued_batches", out var qb)) queuedBatches = qb.GetInt32();

                    if (!string.IsNullOrWhiteSpace(currentBatch))
                    {
                        var bRes = await client.GetAsync($"{baseUrl}/status?batch_id={Uri.EscapeDataString(currentBatch)}", ct);
                        if (bRes.IsSuccessStatusCode)
                        {
                            var bJson = await bRes.Content.ReadAsStringAsync(ct);
                            using var bDoc = System.Text.Json.JsonDocument.Parse(bJson);
                            if (bDoc.RootElement.TryGetProperty("detail", out var d))
                            {
                                batchDetail = d.GetString();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                pingDetail = ex.Message;
            }
        }

        var db = HttpContext.RequestServices.GetService<NewsStudio.Application.Common.Interfaces.IApplicationDbContext>();
        Guid? activeEpisodeId = null;
        int? activeEpisodeNumber = null;
        string? activeEpisodeTitle = null;
        string? activeEpisodeStatus = null;
        bool isStalled = false;

        if (db != null)
        {
            var activeEp = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                db.FilmEpisodes.Where(e => e.Status == "GpuRendering" || e.Status == "GpuQueued" || e.Status == "VoiceGenerating"),
                ct);

            if (activeEp != null)
            {
                activeEpisodeId = activeEp.Id;
                activeEpisodeNumber = activeEp.EpisodeNumber;
                activeEpisodeTitle = activeEp.Title;
                activeEpisodeStatus = activeEp.Status;
                if (online && activeEp.Status == "GpuRendering" && string.IsNullOrWhiteSpace(currentBatch))
                {
                    isStalled = true;
                }
            }
        }

        return Ok(new
        {
            configured = runpod.IsConfigured || !string.IsNullOrWhiteSpace(baseUrl),
            baseUrl,
            podId,
            hasApiKey,
            online,
            pingDetail,
            gpu,
            vramGb,
            diskFreeGb,
            diskTotalGb,
            state = string.IsNullOrWhiteSpace(currentBatch) ? "idle" : "busy",
            currentBatch,
            batchDetail,
            queuedBatches,
            activeEpisodeId,
            activeEpisodeNumber,
            activeEpisodeTitle,
            activeEpisodeStatus,
            isStalled
        });
    }

    public class SetPodIdRequest
    {
        public string? PodId { get; set; }
    }

    [HttpPost("gpu/runpod/pod")]
    public IActionResult SetRuntimePod([FromBody] SetPodIdRequest req)
    {
        NewsStudio.Infrastructure.FilmFactory.RunPodGpuBackend.SetRuntimePodId(req.PodId);
        return Ok(new { success = true, podId = req.PodId });
    }
}

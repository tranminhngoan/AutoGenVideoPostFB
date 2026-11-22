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
    private readonly IFilmShowrunnerService _showrunnerService;
    private readonly IConfiguration _configuration;

    public FilmFactoryController(
        IFilmFactoryService filmFactoryService,
        IFilmProductionCoordinator productionCoordinator,
        IFilmRendererService rendererService,
        IFilmShowrunnerService showrunnerService,
        IConfiguration configuration)
    {
        _filmFactoryService = filmFactoryService;
        _productionCoordinator = productionCoordinator;
        _rendererService = rendererService;
        _showrunnerService = showrunnerService;
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

    [HttpPost("projects/{id:guid}/story-bible/generate")]
    public async Task<ActionResult<FilmProjectDetailDto>> GenerateStoryBibleOnly(Guid id, CancellationToken ct)
    {
        var project = await _filmFactoryService.GetProjectByIdAsync(id, ct);
        if (project == null) return NotFound(new { message = "Không tìm thấy dự án phim." });

        await _showrunnerService.DevelopStoryBibleAsync(id, ct);
        await _showrunnerService.GenerateCharactersAndLocationsAsync(id, ct);

        var updated = await _filmFactoryService.GetProjectByIdAsync(id, ct);
        return Ok(updated);
    }

    [HttpGet("characters/{id:guid}/portrait")]
    public async Task<IActionResult> GetCharacterPortrait(Guid id, CancellationToken ct)
    {
        var c = await _filmFactoryService.GetCharacterByIdAsync(id, ct);
        if (c == null || string.IsNullOrWhiteSpace(c.FrontReferenceImageUrl) || !System.IO.File.Exists(c.FrontReferenceImageUrl))
        {
            return NotFound(new { message = "Chưa có ảnh chân dung cho nhân vật này." });
        }

        var ext = Path.GetExtension(c.FrontReferenceImageUrl).ToLowerInvariant();
        var mime = ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        return PhysicalFile(c.FrontReferenceImageUrl, mime);
    }

    [HttpPost("characters/{id:guid}/portrait/generate")]
    public async Task<ActionResult<FilmCharacterDto>> GenerateCharacterPortrait(Guid id, CancellationToken ct)
    {
        var c = await _filmFactoryService.GetCharacterByIdAsync(id, ct);
        if (c == null) return NotFound(new { message = "Không tìm thấy nhân vật." });

        var updated = await _filmFactoryService.GenerateCharacterPortraitAsync(id, ct);
        return Ok(updated);
    }

    [HttpPost("characters/{id:guid}/portrait/upload")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<FilmCharacterDto>> UploadCharacterPortrait(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Vui lòng chọn file ảnh để tải lên." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".jpg" && ext != ".jpeg" && ext != ".png" && ext != ".webp")
            return BadRequest(new { message = "Định dạng file không hỗ trợ. Vui lòng tải file ảnh JPG, PNG hoặc WebP." });

        var c = await _filmFactoryService.GetCharacterByIdAsync(id, ct);
        if (c == null) return NotFound(new { message = "Không tìm thấy nhân vật." });

        var root = Path.Combine(NewsStudio.Infrastructure.Common.TempStorage.GetRoot(_configuration), "film_factory", "characters");
        Directory.CreateDirectory(root);
        var targetPath = Path.Combine(root, $"{id}_uploaded_{DateTime.UtcNow.Ticks}{ext}");

        using (var stream = new FileStream(targetPath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }

        var updated = await _filmFactoryService.SetCharacterPortraitPathAsync(id, targetPath, ct);
        return Ok(updated);
    }

    [HttpPut("characters/{id:guid}")]
    public async Task<ActionResult<FilmCharacterDto>> UpdateCharacter(Guid id, [FromBody] UpdateFilmCharacterRequestDto request, CancellationToken ct)
    {
        var c = await _filmFactoryService.GetCharacterByIdAsync(id, ct);
        if (c == null) return NotFound(new { message = "Không tìm thấy nhân vật." });

        var updated = await _filmFactoryService.UpdateCharacterAsync(id, request, ct);
        return Ok(updated);
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
        var stored = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.ActiveFilmGpuBackend>();
        if (stored != null) await stored.RefreshAsync(ct);
        var vast = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend>();
        var runpod = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.RunPodGpuBackend>();
        var activeBackend = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.IFilmGpuBackend>();

        var isVast = (activeBackend is NewsStudio.Infrastructure.FilmFactory.ActiveFilmGpuBackend { IsVast: true }) || (activeBackend is NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend) ||
                     (vast != null && vast.IsConfigured && (runpod == null || !runpod.IsConfigured));

        string provider = isVast ? "VastAi" : "RunPod";
        string? baseUrl = null;
        string? podId = null;
        string? podName = null;
        string? podDesiredStatus = null;
        bool hasApiKey = false;
        double? hourlyCost = null;

        if (isVast && vast != null)
        {
            baseUrl = await vast.GetEffectiveBaseUrlAsync(ct);
            hasApiKey = !string.IsNullOrWhiteSpace(vast.ApiKey);
            if (hasApiKey)
            {
                var lifecycle = await vast.GetInstanceLifecycleStatusAsync(null, ct);
                podId = lifecycle.Id ?? NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend.GetRuntimeInstance() ?? vast.InstanceId;
                podDesiredStatus = lifecycle.Status;
                podName = lifecycle.GpuName;
                hourlyCost = lifecycle.DphTotal;
            }
            else
            {
                podId = NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend.GetRuntimeInstance() ?? vast.InstanceId;
            }
        }
        else if (runpod != null)
        {
            baseUrl = await runpod.GetEffectiveBaseUrlAsync(ct);
            podId = NewsStudio.Infrastructure.FilmFactory.RunPodGpuBackend.GetRuntimePodId() ?? runpod.PodId;
            hasApiKey = !string.IsNullOrWhiteSpace(runpod.ApiKey);
            hourlyCost = 2.22;
            if (hasApiKey)
            {
                var lifecycle = await runpod.GetPodLifecycleStatusAsync(podId, ct);
                podDesiredStatus = lifecycle.Status;
                podName = lifecycle.Name;
            }
        }

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
                if (res.IsSuccessStatusCode)
                {
                    pingDetail = await res.Content.ReadAsStringAsync(ct);
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(pingDetail);
                        var r = doc.RootElement;
                        if (r.TryGetProperty("gpu", out var g)) gpu = g.GetString();
                        if (r.TryGetProperty("vram_gb", out var v)) vramGb = v.GetDouble();
                        if (r.TryGetProperty("disk_free_gb", out var df)) diskFreeGb = df.GetDouble();
                        if (r.TryGetProperty("disk_total_gb", out var dt)) diskTotalGb = dt.GetDouble();
                        if (r.TryGetProperty("current_batch", out var cb)) currentBatch = cb.GetString();
                        if (r.TryGetProperty("queued_batches", out var qb)) queuedBatches = qb.GetInt32();

                        // Chỉ coi là online khi đúng là Film Worker JSON hợp lệ (có trường gpu hoặc status ok)
                        online = !string.IsNullOrWhiteSpace(gpu) || (r.TryGetProperty("status", out var s) && s.GetString() == "ok");
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        online = false;
                        pingDetail = "Máy GPU đang mở nhưng cổng 8888 đang chạy ứng dụng mặc định (Jupyter Lab). Cần chạy lệnh start_vast.sh hoặc start_a100.sh trong Terminal để kích hoạt Worker.";
                    }

                    if (online && !string.IsNullOrWhiteSpace(currentBatch))
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
                else
                {
                    online = false;
                    pingDetail = $"Cổng 8888 trả về mã HTTP {(int)res.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                online = false;
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
            int? batchEpNum = null;
            if (!string.IsNullOrWhiteSpace(currentBatch) && currentBatch.StartsWith("ep", StringComparison.OrdinalIgnoreCase))
            {
                var numPart = currentBatch[2..].Split('-')[0];
                if (int.TryParse(numPart, out var n)) batchEpNum = n;
            }

            var activeEp = batchEpNum.HasValue
                ? await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                    db.FilmEpisodes.Where(e => e.EpisodeNumber == batchEpNum.Value), ct)
                : await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                    db.FilmEpisodes
                        .Where(e => e.Status == "GpuRendering" || e.Status == "GpuQueued" || e.Status == "VoiceGenerating" || e.Status == "Queued")
                        .OrderBy(e => e.Status == "GpuRendering" ? 0 : e.Status == "GpuQueued" ? 1 : e.Status == "VoiceGenerating" ? 2 : 3)
                        .ThenBy(e => e.EpisodeNumber),
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
            provider,
            configured = (isVast && vast?.IsConfigured == true) || (!isVast && runpod?.IsConfigured == true) || !string.IsNullOrWhiteSpace(baseUrl),
            baseUrl,
            podId,
            instanceId = podId,
            podName,
            podDesiredStatus,
            hourlyCost,
            dphTotal = hourlyCost,
            hasApiKey,
            vastHasApiKey = !string.IsNullOrWhiteSpace(vast?.ApiKey),
            canManageLifecycle = hasApiKey,
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

    /// <summary>
    /// Master control to STOP the active GPU (Vast.ai or RunPod) directly from the UI, stopping per-hour billing.
    /// </summary>
    [HttpPost("gpu/stop")]
    public async Task<IActionResult> StopGpu([FromQuery] string? podId, CancellationToken ct)
    {
        if (await GpuControlDeniedAsync() is { } denied) return denied;
        var vast = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend>();
        var runpod = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.RunPodGpuBackend>();
        var activeBackend = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.IFilmGpuBackend>();

        var isVast = (activeBackend is NewsStudio.Infrastructure.FilmFactory.ActiveFilmGpuBackend { IsVast: true }) || (activeBackend is NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend) ||
                     (vast != null && vast.IsConfigured && (runpod == null || !runpod.IsConfigured));

        if (isVast && vast != null)
        {
            var (success, msg, desiredStatus) = await vast.StopInstanceAsync(podId, ct);
            if (!success) return BadRequest(new { success = false, message = msg });
            return Ok(new { success = true, message = msg, desiredStatus, provider = "VastAi" });
        }

        if (runpod != null)
        {
            var (success, msg, desiredStatus) = await runpod.StopPodAsync(podId, ct);
            if (!success) return BadRequest(new { success = false, message = msg });
            return Ok(new { success = true, message = msg, desiredStatus, provider = "RunPod" });
        }

        return BadRequest(new { success = false, message = "Chưa cấu hình GPU backend nào." });
    }

    /// <summary>
    /// Master control to START / RESUME the active GPU (Vast.ai or RunPod) directly from the UI.
    /// </summary>
    [HttpPost("gpu/start")]
    public async Task<IActionResult> StartGpu([FromQuery] string? podId, CancellationToken ct)
    {
        if (await GpuControlDeniedAsync() is { } denied) return denied;
        var vast = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend>();
        var runpod = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.RunPodGpuBackend>();
        var activeBackend = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.IFilmGpuBackend>();

        var isVast = (activeBackend is NewsStudio.Infrastructure.FilmFactory.ActiveFilmGpuBackend { IsVast: true }) || (activeBackend is NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend) ||
                     (vast != null && vast.IsConfigured && (runpod == null || !runpod.IsConfigured));

        if (isVast && vast != null)
        {
            var (success, msg, desiredStatus) = await vast.StartInstanceAsync(podId, ct);
            if (!success) return BadRequest(new { success = false, message = msg });
            return Ok(new { success = true, message = msg, desiredStatus, provider = "VastAi" });
        }

        if (runpod != null)
        {
            var (success, msg, desiredStatus) = await runpod.ResumePodAsync(podId, ct);
            if (!success) return BadRequest(new { success = false, message = msg });
            return Ok(new { success = true, message = msg, desiredStatus, provider = "RunPod" });
        }

        return BadRequest(new { success = false, message = "Chưa cấu hình GPU backend nào." });
    }

    public class SetInstanceRequest
    {
        public string? PodId { get; set; }
        public string? InstanceId { get; set; }
        public string? Target { get; set; }
    }

    [HttpPost("gpu/runpod/pod")]
    [HttpPost("gpu/instance")]
    public async Task<IActionResult> SetRuntimeInstance([FromBody] SetInstanceRequest req, CancellationToken ct)
    {
        if (await GpuControlDeniedAsync() is { } denied) return denied;
        var val = req.Target ?? req.InstanceId ?? req.PodId;
        NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend.SetRuntimeInstance(val);
        var active = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.ActiveFilmGpuBackend>();
        if (active != null) await active.SetVastInstanceAsync(val, ct); // kept across restarts and seen by the Worker
        NewsStudio.Infrastructure.FilmFactory.RunPodGpuBackend.SetRuntimePodId(val);
        return Ok(new { success = true, podId = val, instanceId = val });
    }

    // ─────────────────────────────── Vast.ai machines: find / rent / list / destroy ───────────────────────────────

    /// <summary>Rentable Vast.ai machines matching the film worker's needs, cheapest first (prices for the chosen disk).</summary>
    [HttpGet("gpu/vast/offers")]
    public async Task<IActionResult> SearchVastOffers(
        [FromQuery] string? gpus, [FromQuery] int diskGb = 200, [FromQuery] double? maxPrice = null,
        [FromQuery] double? maxDownloadCostPerGb = null, [FromQuery] double minReliability = 0.98,
        [FromQuery] bool verifiedOnly = true, CancellationToken ct = default)
    {
        var vast = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend>();
        if (vast == null) return BadRequest(new { message = "Chưa đăng ký Vast.ai backend." });
        if (diskGb is < 50 or > 2000) return BadRequest(new { message = "Dung lượng ổ phải từ 50 đến 2000 GB." });

        var names = string.IsNullOrWhiteSpace(gpus)
            ? NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend.DefaultGpuNames
            : gpus.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            var offers = await vast.SearchOffersAsync(new NewsStudio.Infrastructure.FilmFactory.VastOfferQuery(
                names, diskGb, maxPrice, maxDownloadCostPerGb, minReliability, VerifiedOnly: verifiedOnly), ct);
            return Ok(new
            {
                diskGb,
                modelDownloadGb = NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend.ModelDownloadGb,
                requirements = NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend.Requirements,
                offers
            });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { message = ex.Message });
        }
    }

    public class RentVastRequest
    {
        public long OfferId { get; set; }
        public int DiskGb { get; set; } = 200;
    }

    /// <summary>Rents one offer; the new machine installs and starts the film worker by itself (start_vast.sh).</summary>
    [HttpPost("gpu/vast/rent")]
    public async Task<IActionResult> RentVastOffer([FromBody] RentVastRequest req, CancellationToken ct)
    {
        if (await GpuControlDeniedAsync() is { } denied) return denied;
        var vast = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend>();
        if (vast == null) return BadRequest(new { message = "Chưa đăng ký Vast.ai backend." });
        if (req.OfferId <= 0) return BadRequest(new { message = "Thiếu mã máy (offer) cần thuê." });
        if (req.DiskGb is < 50 or > 2000) return BadRequest(new { message = "Dung lượng ổ phải từ 50 đến 2000 GB." });
        try
        {
            var id = await vast.RentOfferAsync(req.OfferId, req.DiskGb, ct);
            // New batches now go to this Vast machine (batches already sent elsewhere still finish where they are).
            var active = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.ActiveFilmGpuBackend>();
            if (active != null) await active.SelectVastAsync(id, ct); // stored: survives restarts, no Railway variable needed
            return Ok(new
            {
                success = true,
                instanceId = id,
                message = $"Đã thuê máy #{id} và chuyển Film Factory sang dùng máy này. Máy đang khởi động, tự tải model và bật worker (thường 10–20 phút) — tiền GPU bắt đầu tính từ bây giờ."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>Remaining Vast.ai credit, plus Vast's own billing page where the user tops up.</summary>
    [HttpGet("gpu/vast/account")]
    public async Task<IActionResult> GetVastAccount(CancellationToken ct)
    {
        if (await GpuControlDeniedAsync() is { } denied) return denied;
        var vast = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend>();
        if (vast == null) return BadRequest(new { message = "Chưa đăng ký Vast.ai backend." });
        try
        {
            var a = await vast.GetAccountAsync(ct);
            return Ok(new
            {
                credit = a.Credit,
                balance = a.Balance,
                autoBillingEnabled = a.AutoBillingEnabled,
                autoBillingThreshold = a.AutoBillingThreshold,
                billingUrl = NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend.BillingUrl
            });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { message = ex.Message });
        }
    }

    /// <summary>All instances on the Vast.ai account (running, stopped, loading) with their storage cost.</summary>
    [HttpGet("gpu/vast/instances")]
    public async Task<IActionResult> ListVastInstances(CancellationToken ct)
    {
        if (await GpuControlDeniedAsync() is { } denied) return denied;
        var vast = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend>();
        if (vast == null) return BadRequest(new { message = "Chưa đăng ký Vast.ai backend." });
        try
        {
            var instances = await vast.ListInstancesAsync(ct);
            return Ok(new { activeInstanceId = NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend.GetRuntimeInstance() ?? vast.InstanceId, instances });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Destroys a Vast.ai machine (deletes its disk, stops all billing). Refused while that machine is rendering an
    /// episode unless force=true, because the batch would be lost.
    /// </summary>
    [HttpDelete("gpu/vast/instances/{instanceId}")]
    public async Task<IActionResult> DestroyVastInstance(string instanceId, [FromQuery] bool force = false, CancellationToken ct = default)
    {
        if (await GpuControlDeniedAsync() is { } denied) return denied;
        var vast = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend>();
        if (vast == null) return BadRequest(new { message = "Chưa đăng ký Vast.ai backend." });

        if (!force)
        {
            var active = NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend.GetRuntimeInstance() ?? vast.InstanceId;
            var db = HttpContext.RequestServices.GetService<NewsStudio.Application.Common.Interfaces.IApplicationDbContext>();
            var rendering = db != null && await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
                db.FilmEpisodes.Where(e => e.Status == NewsStudio.Infrastructure.FilmFactory.FilmGpuService.StatusRendering), ct);
            if (rendering && (active == null || string.Equals(active, instanceId, StringComparison.OrdinalIgnoreCase)))
                return Conflict(new
                {
                    success = false,
                    needsForce = true,
                    message = $"Máy #{instanceId} có thể đang dựng một tập phim (trạng thái GpuRendering). Xoá bây giờ sẽ mất batch đang chạy."
                });
        }

        try
        {
            var storedActive = NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend.GetRuntimeInstance();
            await vast.DestroyInstanceAsync(instanceId, ct);
            var activeBackend = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.ActiveFilmGpuBackend>();
            if (activeBackend != null && string.Equals(storedActive, instanceId, StringComparison.OrdinalIgnoreCase))
                await activeBackend.SetVastInstanceAsync(null, ct); // next running machine is picked up automatically
            return Ok(new { success = true, message = $"Đã xoá máy #{instanceId}. Không còn tính tiền GPU lẫn phí giữ ổ." });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Voice line n (1-based) of the fixed 30 s test scene (FilmTestScene), synthesised with the film's Gemini voices.
    /// Only these fixed lines can be produced, and each is cached on disk, so the endpoint cannot spend the Gemini key
    /// on arbitrary text.
    /// </summary>
    [HttpGet("gpu/test-scene/voice/{n:int}")]
    public async Task<IActionResult> GetTestSceneVoice(int n, [FromServices] ITtsFactory ttsFactory, CancellationToken ct)
    {
        var lines = NewsStudio.Infrastructure.FilmFactory.FilmTestScene.Lines;
        if (n < 1 || n > lines.Count) return NotFound(new { message = $"Cảnh test chỉ có {lines.Count} câu thoại." });
        var (voice, text) = lines[n - 1];

        var dir = Path.Combine(NewsStudio.Infrastructure.Common.TempStorage.GetRoot(_configuration), "film_factory", "test_scene");
        Directory.CreateDirectory(dir);
        // keyed by voice + text, so an edited line (or a bad take replaced by a new deploy) is never served stale
        var textHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(voice + "|" + text)))[..12];
        var cached = Path.Combine(dir, $"line_{n}_{textHash}.wav");
        if (!System.IO.File.Exists(cached))
        {
            try
            {
                var result = await ttsFactory.GetTtsService("Gemini").SynthesizeSpeechWithSubtitlesAsync(new TtsSynthesisRequest
                {
                    ProjectId = Guid.Empty,
                    Text = text,
                    Voice = voice,
                    Speed = "+0%"
                }, ct);
                System.IO.File.Copy(result.AudioFilePath, cached, overwrite: true);
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
            }
        }
        return PhysicalFile(Path.GetFullPath(cached), "audio/wav", $"line_{n}.wav");
    }

    /// <summary>
    /// Phục vụ mã nguồn Python film_gpu_worker.py để các máy GPU (Vast.ai, RunPod) tải về bằng 1 lệnh curl tiện lợi.
    /// </summary>
    [HttpGet("gpu/worker-script")]
    public IActionResult GetWorkerScript()
    {
        var path = FindLocalScriptPath("film_gpu_worker.py");
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            return NotFound(new { message = "Không tìm thấy file film_gpu_worker.py trên server." });
        }
        return PhysicalFile(path, "text/x-python", "film_gpu_worker.py");
    }

    /// <summary>
    /// Phục vụ kịch bản khởi động start_vast.sh để chạy trên Vast.ai.
    /// </summary>
    [HttpGet("gpu/start-vast")]
    public IActionResult GetStartVastScript()
    {
        var path = FindLocalScriptPath("start_vast.sh");
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            return NotFound(new { message = "Không tìm thấy file start_vast.sh trên server." });
        }
        return PhysicalFile(path, "text/x-sh", "start_vast.sh");
    }

    /// <summary>
    /// The API has no login (Security:AdminApiKey is optional and unset in production), so anything that spends money
    /// or touches the Vast account — rent, destroy, start/stop, retarget, balance, machine list — needs the account's
    /// own Vast API key in X-Vast-Key. The browser asks for it once and keeps it locally; it is compared in constant
    /// time with VAST_API_KEY on the server, so no extra Railway variable is needed. Without a Vast key configured
    /// (RunPod/Kaggle-only setups) the previous behaviour is kept.
    /// </summary>
    private async Task<IActionResult?> GpuControlDeniedAsync()
    {
        var vast = HttpContext.RequestServices.GetService<NewsStudio.Infrastructure.FilmFactory.VastAiGpuBackend>();
        if (vast == null || string.IsNullOrWhiteSpace(vast.ApiKey)) return null;
        var given = Request.Headers.TryGetValue("X-Vast-Key", out var h) ? h.ToString().Trim() : "";
        if (given.Length > 0 && await vast.IsSameAccountKeyAsync(given, HttpContext.RequestAborted)) return null;
        return StatusCode(StatusCodes.Status401Unauthorized, new
        {
            needsVastKey = true,
            message = given.Length == 0
                ? "Cần mở khoá bằng Vast API key (chỉ lưu trên trình duyệt này) để quản lý máy GPU."
                : "Khoá này không thuộc tài khoản Vast.ai đang dùng trên server (hoặc khoá đã bị xoá) — dán lại một API key của đúng tài khoản."
        });
    }

    private string? FindLocalScriptPath(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "GpuWorker", fileName),
            Path.Combine(AppContext.BaseDirectory, "FilmFactory", "GpuWorker", fileName), // where the publish output puts them
            Path.Combine(Directory.GetCurrentDirectory(), fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "backend", "src", "NewsStudio.Infrastructure", "FilmFactory", "GpuWorker", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "backend", "src", "NewsStudio.Infrastructure", "FilmFactory", "GpuWorker", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "NewsStudio.Infrastructure", "FilmFactory", "GpuWorker", fileName)
        };
        return candidates.FirstOrDefault(System.IO.File.Exists);
    }
}

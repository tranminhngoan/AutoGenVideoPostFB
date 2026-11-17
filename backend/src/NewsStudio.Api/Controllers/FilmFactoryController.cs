using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
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

    public FilmFactoryController(
        IFilmFactoryService filmFactoryService,
        IFilmProductionCoordinator productionCoordinator,
        IFilmRendererService rendererService)
    {
        _filmFactoryService = filmFactoryService;
        _productionCoordinator = productionCoordinator;
        _rendererService = rendererService;
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

    [HttpPost("episodes/{id:guid}/render")]
    public async Task<IActionResult> RenderEpisode(Guid id, CancellationToken ct)
    {
        try
        {
            var videoPath = await _rendererService.AssembleEpisodeAsync(id, ct);
            return Ok(new { message = "Dựng tập phim thành công.", videoPath });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Dựng tập phim thất bại: {ex.Message}" });
        }
    }
}

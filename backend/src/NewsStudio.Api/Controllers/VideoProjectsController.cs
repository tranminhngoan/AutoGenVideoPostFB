using Microsoft.AspNetCore.Mvc;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Api.Controllers;

[ApiController]
[Route("api/video-projects")]
[Produces("application/json")]
public class VideoProjectsController : ControllerBase
{
    private readonly IVideoProjectService _projectService;
    private readonly ILogger<VideoProjectsController> _logger;

    public VideoProjectsController(
        IVideoProjectService projectService,
        ILogger<VideoProjectsController> logger)
    {
        _projectService = projectService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new video project from an article URL.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(VideoProjectResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateProject([FromBody] CreateVideoProjectDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.SourceUrl))
        {
            return BadRequest(new { error = "SourceUrl is required." });
        }

        var project = await _projectService.CreateProjectAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(GetProjectById), new { id = project.Id }, project);
    }

    /// <summary>
    /// Retrieves a paginated list of video projects.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<VideoProjectResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProjects([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var projects = await _projectService.GetProjectsAsync(page, pageSize, cancellationToken);
        return Ok(projects);
    }

    /// <summary>
    /// Retrieves detailed information for a specific video project.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(VideoProjectResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProjectById([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var project = await _projectService.GetProjectByIdAsync(id, cancellationToken);
        if (project == null)
        {
            return NotFound(new { error = $"VideoProject {id} not found." });
        }
        return Ok(project);
    }

    /// <summary>
    /// Enqueues an asynchronous processing pipeline job for the video project via Hangfire.
    /// </summary>
    [HttpPost("{id:guid}/process")]
    [ProducesResponseType(typeof(ProcessingJobResponseDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ProcessProject([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var job = await _projectService.EnqueueProjectProcessingAsync(id, cancellationToken);
            return Accepted($"/api/jobs/{job.Id}", job);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"VideoProject {id} not found." });
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Api.Controllers;

[ApiController]
[Route("api/jobs")]
[Produces("application/json")]
public class JobsController : ControllerBase
{
    private readonly IProcessingJobService _jobService;
    private readonly ILogger<JobsController> _logger;

    public JobsController(
        IProcessingJobService jobService,
        ILogger<JobsController> logger)
    {
        _jobService = jobService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves current status, step progress, and error details for a processing job.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProcessingJobResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJobById([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var job = await _jobService.GetJobByIdAsync(id, cancellationToken);
        if (job == null)
        {
            return NotFound(new { error = $"Job {id} not found." });
        }
        return Ok(job);
    }

    /// <summary>
    /// Requests cancellation of an in-progress or queued job.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelJob([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var success = await _jobService.CancelJobAsync(id, cancellationToken);
        if (!success)
        {
            return NotFound(new { error = $"Job {id} not found or could not be cancelled." });
        }
        return Ok(new { message = $"Job {id} cancelled successfully." });
    }
}

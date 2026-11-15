using Microsoft.AspNetCore.Mvc;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
    private readonly ISystemHealthService _healthService;
    private readonly ILogger<HealthController> _logger;

    public HealthController(ISystemHealthService healthService, ILogger<HealthController> logger)
    {
        _healthService = healthService;
        _logger = logger;
    }

    /// <summary>
    /// Returns comprehensive health status and component metrics.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SystemHealthDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Health check requested");
        var health = await _healthService.CheckHealthAsync(cancellationToken);
        return Ok(health);
    }

    /// <summary>
    /// Readiness probe for container orchestration.
    /// </summary>
    [HttpGet("ready")]
    public async Task<IActionResult> GetReady(CancellationToken cancellationToken)
    {
        var ready = await _healthService.IsReadyAsync(cancellationToken);
        return ready ? Ok(new { status = "Ready" }) : StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "Not Ready" });
    }

    /// <summary>
    /// Liveness probe for container orchestration.
    /// </summary>
    [HttpGet("live")]
    public async Task<IActionResult> GetLive(CancellationToken cancellationToken)
    {
        var live = await _healthService.IsLiveAsync(cancellationToken);
        return live ? Ok(new { status = "Live" }) : StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "Unhealthy" });
    }
}

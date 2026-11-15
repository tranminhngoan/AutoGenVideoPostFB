using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Api.Controllers;

[ApiController]
[Route("api/system")]
[Produces("application/json")]
public class SystemController : ControllerBase
{
    private readonly ISystemMetricsService _metricsService;
    private readonly IStorageCleanupService _storageCleanupService;
    private readonly IJobRecoveryService _jobRecoveryService;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public SystemController(
        ISystemMetricsService metricsService,
        IStorageCleanupService storageCleanupService,
        IJobRecoveryService jobRecoveryService,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _metricsService = metricsService;
        _storageCleanupService = storageCleanupService;
        _jobRecoveryService = jobRecoveryService;
        _configuration = configuration;
        _environment = environment;
    }

    /// <summary>
    /// Gets sanitized system settings and service readiness (no secrets exposed).
    /// </summary>
    [HttpGet("config-summary")]
    public IActionResult GetConfigSummary()
    {
        var aiProvider = _configuration["Ai:Provider"] ?? "Gemini";
        var hasAiApiKey = !string.IsNullOrWhiteSpace(_configuration["Ai:ApiKey"]);
        var ttsProvider = _configuration["Tts:Provider"] ?? "EdgeTts";
        var ttsVoice = _configuration["Tts:Voice"] ?? "vi-VN-HoaiMyNeural";
        var hasFacebookConfig = !string.IsNullOrWhiteSpace(_configuration["Facebook:PageAccessToken"]);
        var tempStorage = _configuration["Storage:TempStoragePath"] ?? "/tmp/newsstudio";

        return Ok(new
        {
            environment = _environment.EnvironmentName,
            ai = new
            {
                provider = aiProvider,
                isConfigured = hasAiApiKey,
                model = _configuration["Ai:Model"] ?? "gemini-1.5-flash"
            },
            tts = new
            {
                provider = ttsProvider,
                voice = ttsVoice,
                isConfigured = true
            },
            facebook = new
            {
                isConfigured = hasFacebookConfig,
                pageId = _configuration["Facebook:PageId"] ?? string.Empty
            },
            storage = new
            {
                tempPath = tempStorage,
                maxConcurrentRenders = _configuration.GetValue<int>("Storage:MaxConcurrentRenderJobs", 2),
                maxStorageMb = _configuration.GetValue<int>("Storage:MaxTempStorageMb", 5120),
                ttlMinutes = _configuration.GetValue<int>("Storage:TempFileTtlMinutes", 120)
            }
        });
    }

    /// <summary>
    /// Returns basic system information.
    /// </summary>
    [HttpGet("info")]
    public IActionResult GetInfo()
    {
        return Ok(new
        {
            appName = "AI News Studio",
            version = "1.0.0",
            dotnetVersion = Environment.Version.ToString(),
            osPlatform = Environment.OSVersion.Platform.ToString(),
            serverTimeUtc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Gets real-time system metrics: CPU, RAM, Temp Storage disk usage, Job counts, and DB latency.
    /// </summary>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(SystemMetricsDto), 200)]
    public async Task<IActionResult> GetMetrics(CancellationToken cancellationToken)
    {
        var metrics = await _metricsService.GetSystemMetricsAsync(cancellationToken);
        return Ok(metrics);
    }

    /// <summary>
    /// Gets cost and token tracking across AI and TTS providers.
    /// </summary>
    [HttpGet("costs")]
    [ProducesResponseType(typeof(SystemCostSummaryDto), 200)]
    public async Task<IActionResult> GetCosts(CancellationToken cancellationToken)
    {
        var costs = await _metricsService.GetCostSummaryAsync(cancellationToken);
        return Ok(costs);
    }

    /// <summary>
    /// Triggers an immediate storage cleanup of expired assets and enforces storage quotas.
    /// </summary>
    [HttpPost("storage/cleanup")]
    [ProducesResponseType(typeof(StorageCleanupReportDto), 200)]
    public async Task<IActionResult> TriggerStorageCleanup(CancellationToken cancellationToken)
    {
        var report = await _storageCleanupService.RunStorageCleanupAsync(cancellationToken);
        return Ok(report);
    }

    /// <summary>
    /// Triggers a job recovery audit for any interrupted or dangling jobs.
    /// </summary>
    [HttpPost("jobs/recover")]
    [ProducesResponseType(typeof(JobRecoveryReportDto), 200)]
    public async Task<IActionResult> TriggerJobRecovery(CancellationToken cancellationToken)
    {
        var report = await _jobRecoveryService.RecoverDanglingJobsAsync(cancellationToken);
        return Ok(report);
    }
}

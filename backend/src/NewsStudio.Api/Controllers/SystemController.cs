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
        // Read the same keys the AI editor services actually use
        var geminiFreeKeyCount = NewsStudio.Infrastructure.Common.GeminiKeys.FreeKeys(_configuration).Count;
        var hasGeminiFreeKey = geminiFreeKeyCount > 0;
        var hasGeminiPaidKey = NewsStudio.Infrastructure.Common.GeminiKeys.Paid(_configuration) != null;
        var claudeKey = _configuration["Ai:Claude:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var hasGeminiKey = hasGeminiFreeKey || hasGeminiPaidKey;
        var hasClaudeKey = !string.IsNullOrWhiteSpace(claudeKey);
        var isClaude = aiProvider.Equals("Claude", StringComparison.OrdinalIgnoreCase);
        var ttsProvider = _configuration["Tts:Provider"] ?? "EdgeTts";
        var ttsVoice = _configuration["Tts:Voice"] ?? "vi-VN-HoaiMyNeural";
        var hasFacebookConfig = !string.IsNullOrWhiteSpace(_configuration["Facebook:PageAccessToken"]);
        var tempStorage = NewsStudio.Infrastructure.Common.TempStorage.GetRoot(_configuration);

        return Ok(new
        {
            environment = _environment.EnvironmentName,
            simulation = NewsStudio.Infrastructure.Common.SimulationMode.IsEnabled(_configuration),
            ai = new
            {
                provider = aiProvider,
                isConfigured = isClaude ? hasClaudeKey : hasGeminiKey,
                model = isClaude
                    ? _configuration["Ai:Claude:Model"] ?? "claude-3-5-haiku-20241022"
                    : _configuration["Ai:Gemini:Model"] ?? "gemini-flash-latest",
                gemini = new { isConfigured = hasGeminiKey, hasFreeKey = hasGeminiFreeKey, freeKeyCount = geminiFreeKeyCount, hasPaidKey = hasGeminiPaidKey, model = _configuration["Ai:Gemini:Model"] ?? "gemini-flash-latest" },
                claude = new { isConfigured = hasClaudeKey, model = _configuration["Ai:Claude:Model"] ?? "claude-3-5-haiku-20241022" }
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

    private const string VoicePreviewSentence =
        "Xin chào, đây là Trạm Tin 24/7. Hôm nay, thành phố ghi nhận nhiều chuyển biến tích cực, và chúng tôi sẽ cập nhật ngay cho bạn.";

    /// <summary>
    /// Short spoken sample of a voice ("gemini:Kore", "vi-VN-HoaiMyNeural"…). Synthesised once per voice
    /// and cached on disk, so listening again costs nothing.
    /// </summary>
    [HttpGet("voice-preview")]
    public async Task<IActionResult> GetVoicePreview(
        [FromQuery] string voice,
        [FromServices] ITtsFactory ttsFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(voice) || voice.Length > 80
            || !System.Text.RegularExpressions.Regex.IsMatch(voice, @"^[A-Za-z0-9:\-]+$"))
        {
            return BadRequest(new { message = "Tên giọng không hợp lệ." });
        }

        var dir = Path.Combine(NewsStudio.Infrastructure.Common.TempStorage.GetRoot(_configuration), "voice-previews");
        Directory.CreateDirectory(dir);
        var cached = Path.Combine(dir, voice.Replace(':', '_') + ".wav");

        if (!System.IO.File.Exists(cached))
        {
            try
            {
                var tts = NewsStudio.Infrastructure.Rendering.GeminiTtsService.IsGeminiVoice(voice)
                    ? ttsFactory.GetTtsService("Gemini")
                    : ttsFactory.GetTtsService("EdgeTts");
                var result = await tts.SynthesizeSpeechWithSubtitlesAsync(new TtsSynthesisRequest
                {
                    ProjectId = Guid.Empty,
                    Text = VoicePreviewSentence,
                    Voice = voice,
                    Speed = "+0%"
                }, cancellationToken);
                System.IO.File.Copy(result.AudioFilePath, cached, overwrite: true);
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
            }
        }

        return PhysicalFile(Path.GetFullPath(cached), "audio/wav");
    }

    /// <summary>
    /// Lists the Gemini models the configured API key can use, live from Google — so the right model
    /// name can be picked instead of guessing (a wrong/retired name makes script generation fail with 404).
    /// </summary>
    [HttpGet("ai-models")]
    public async Task<IActionResult> GetAiModels(
        [FromServices] NewsStudio.Infrastructure.Ai.GeminiAiEditorService gemini,
        CancellationToken cancellationToken)
    {
        var current = _configuration["Ai:Gemini:Model"] ?? "gemini-flash-latest";
        try
        {
            var models = await gemini.ListAvailableModelsAsync(cancellationToken);
            if (models == null)
            {
                return Ok(new { configured = false, current, currentAvailable = false, models = Array.Empty<AiModelInfoDto>() });
            }
            return Ok(new
            {
                configured = true,
                current,
                currentAvailable = models.Any(m => m.Id == current),
                models = models.OrderByDescending(m => m.Id)
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message, current });
        }
    }

    /// <summary>
    /// Returns basic system information.
    /// </summary>
    [HttpGet("info")]
    public IActionResult GetInfo()
    {
        return Ok(new
        {
            appName = "Trạm Tin 24/7",
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

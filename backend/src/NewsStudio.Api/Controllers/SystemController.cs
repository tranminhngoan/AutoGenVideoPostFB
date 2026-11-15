using Microsoft.AspNetCore.Mvc;

namespace NewsStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SystemController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public SystemController(IConfiguration configuration, IHostEnvironment environment)
    {
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
                isConfigured = true // EdgeTts is built-in/free
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
}

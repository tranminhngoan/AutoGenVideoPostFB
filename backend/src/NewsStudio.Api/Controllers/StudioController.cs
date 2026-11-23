using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Infrastructure.Common;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Api.Controllers;

/// <summary>
/// AI video studio API (Seedance text-to-video + Gemini TTS). The app has no login, so every call that spends money or
/// writes data needs X-Studio-Key equal to the server's Vercel AI Gateway key, its paid Gemini key or Security:AdminApiKey. Media downloads (images, music, clips, final video) stay
/// open so the page can show them in &lt;img&gt;/&lt;video&gt; tags.
/// </summary>
[ApiController]
[Route("api/studio")]
public class StudioController : ControllerBase
{
    private const long MaxMusicBytes = 25 * 1024 * 1024;

    private readonly StudioService _studio;
    private readonly IFacebookPublishingService _facebook;
    private readonly IConfiguration _configuration;

    public StudioController(StudioService studio, IFacebookPublishingService facebook, IConfiguration configuration)
    {
        _studio = studio;
        _facebook = facebook;
        _configuration = configuration;
    }

    // ───────────── access ─────────────

    /// <summary>Null when allowed; otherwise a 401 the page turns into its unlock box.</summary>
    private IActionResult? Denied()
    {
        var keys = new[] { SeedanceVideoClient.ApiKey(_configuration), GeminiKeys.Paid(_configuration), _configuration["Security:AdminApiKey"] }
            .Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!.Trim()).ToList();
        if (keys.Count == 0) return null; // nothing configured to compare with (and nothing paid can run either)
        var given = Request.Headers.TryGetValue("X-Studio-Key", out var h) ? h.ToString().Trim() : "";
        if (given.Length > 0)
        {
            var g = SHA256.HashData(Encoding.UTF8.GetBytes(given));
            if (keys.Any(k => CryptographicOperations.FixedTimeEquals(g, SHA256.HashData(Encoding.UTF8.GetBytes(k))))) return null;
        }
        return StatusCode(StatusCodes.Status401Unauthorized, new
        {
            needsStudioKey = true,
            message = given.Length == 0
                ? "Cần mở khoá Studio bằng khoá Vercel AI Gateway (AI_GATEWAY_API_KEY) hoặc khoá Gemini đang đặt trên Railway (chỉ lưu trên trình duyệt này)."
                : "Khoá không khớp với khoá Vercel / Gemini đang cấu hình trên server."
        });
    }

    private async Task<IActionResult> Run(Func<Task<object?>> action)
    {
        try
        {
            var result = await action();
            return result == null ? NoContent() : Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("access")]
    public IActionResult CheckAccess() => Denied() ?? Ok(new { ok = true });

    [HttpGet("settings")]
    public Task<IActionResult> GetSettings(CancellationToken ct) => Run(async () => await _studio.GetSettingsAsync(ct));

    public class BudgetRequest { public decimal MonthlyBudgetUsd { get; set; } }

    [HttpPut("settings/budget")]
    public async Task<IActionResult> SetBudget([FromBody] BudgetRequest req, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _studio.SetMonthlyBudgetAsync(req.MonthlyBudgetUsd, ct));

    [HttpGet("pages")]
    public async Task<IActionResult> Pages(CancellationToken ct) =>
        Ok((await _facebook.GetConnectedPagesAsync(ct)).Select(p => new { p.Id, p.PageName, p.IsActive }));

    // ───────────── assets ─────────────

    public class MusicUploadForm
    {
        public IFormFile? File { get; set; }
        public string? Name { get; set; }
        public string? Mood { get; set; }
    }

    // ───────────── music ─────────────

    [HttpGet("music")]
    public Task<IActionResult> Music(CancellationToken ct) => Run(async () => await _studio.ListMusicAsync(ct));

    [HttpPost("music")]
    [RequestSizeLimit(MaxMusicBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxMusicBytes + 1024 * 1024)]
    public async Task<IActionResult> AddMusic([FromForm] MusicUploadForm form, CancellationToken ct)
    {
        if (Denied() is { } d) return d;
        var file = form.File;
        if (file == null || file.Length == 0) return BadRequest(new { message = "Chưa chọn file nhạc." });
        if (file.Length > MaxMusicBytes) return BadRequest(new { message = "File nhạc quá lớn (tối đa 25 MB)." });
        var bytes = await ReadAsync(file, ct);
        return await Run(async () => await _studio.AddMusicAsync(form.Name ?? "", form.Mood, bytes, file.ContentType, Path.GetExtension(file.FileName), ct));
    }

    public class MusicUpdate { public string? Name { get; set; } public string? Mood { get; set; } }

    [HttpPut("music/{id:guid}")]
    public async Task<IActionResult> UpdateMusic(Guid id, [FromBody] MusicUpdate req, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _studio.UpdateMusicAsync(id, req.Name, req.Mood, ct));

    [HttpDelete("music/{id:guid}")]
    public async Task<IActionResult> DeleteMusic(Guid id, CancellationToken ct) =>
        Denied() ?? await Run(async () => { await _studio.DeleteMusicAsync(id, ct); return null; });

    [HttpGet("music/{id:guid}/audio")]
    public async Task<IActionResult> MusicAudio(Guid id, CancellationToken ct) =>
        await BlobResult(await _studio.MusicBlobIdAsync(id, ct), null, ct);

    // ───────────── videos ─────────────

    [HttpGet("videos")]
    public Task<IActionResult> Videos(CancellationToken ct) => Run(async () => await _studio.ListVideosAsync(ct));

    [HttpGet("videos/{id:guid}")]
    public Task<IActionResult> Video(Guid id, CancellationToken ct) => Run(async () => await _studio.GetVideoAsync(id, ct));

    [HttpPost("videos")]
    public async Task<IActionResult> CreateVideo([FromBody] StudioVideoInputDto input, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _studio.CreateVideoAsync(input, ct));

    [HttpPut("videos/{id:guid}")]
    public async Task<IActionResult> UpdateVideo(Guid id, [FromBody] StudioVideoInputDto input, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _studio.UpdateVideoAsync(id, input, ct));

    [HttpDelete("videos/{id:guid}")]
    public async Task<IActionResult> DeleteVideo(Guid id, CancellationToken ct) =>
        Denied() ?? await Run(async () => { await _studio.DeleteVideoAsync(id, ct); return null; });

    [HttpPost("videos/{id:guid}/plan")]
    public async Task<IActionResult> Plan(Guid id, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _studio.StartPlanAsync(id, ct));

    [HttpPut("videos/{id:guid}/shots")]
    public async Task<IActionResult> Shots(Guid id, [FromBody] List<StudioShotInputDto> shots, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _studio.ReplaceShotsAsync(id, shots, ct));

    [HttpPost("videos/{id:guid}/generate")]
    public async Task<IActionResult> Generate(Guid id, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _studio.StartGenerateAsync(id, null, ct));

    [HttpPost("videos/{id:guid}/shots/{shotId:guid}/generate")]
    public async Task<IActionResult> GenerateShot(Guid id, Guid shotId, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _studio.StartGenerateAsync(id, shotId, ct));

    [HttpPost("videos/{id:guid}/voices")]
    public async Task<IActionResult> Voices(Guid id, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _studio.StartVoicesAsync(id, null, ct));

    [HttpPost("videos/{id:guid}/shots/{shotId:guid}/voice")]
    public async Task<IActionResult> ShotVoice(Guid id, Guid shotId, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _studio.StartVoicesAsync(id, shotId, ct));

    [HttpGet("shots/{shotId:guid}/voice")]
    public async Task<IActionResult> VoiceAudio(Guid shotId, CancellationToken ct) =>
        await BlobResult(await _studio.ShotVoiceBlobIdAsync(shotId, ct), null, ct);

    [HttpPost("videos/{id:guid}/recover-clips")]
    public async Task<IActionResult> RecoverClips(Guid id, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _studio.StartRecoverAsync(id, ct));

    [HttpPost("videos/{id:guid}/compose")]
    public async Task<IActionResult> Compose(Guid id, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _studio.StartComposeAsync(id, ct));

    public class PublishRequest { public string? Caption { get; set; } }

    [HttpPost("videos/{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, [FromBody] PublishRequest req, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _studio.StartPublishAsync(id, req.Caption, ct));

    [HttpPost("videos/{id:guid}/purge-clips")]
    public async Task<IActionResult> PurgeClips(Guid id, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _studio.PurgeClipsAsync(id, ct));

    [HttpGet("videos/{id:guid}/final")]
    public async Task<IActionResult> Final(Guid id, [FromQuery] bool download = false, CancellationToken ct = default) =>
        await BlobResult(await _studio.FinalBlobIdAsync(id, ct), download ? $"video_{id:N}.mp4" : null, ct);

    [HttpGet("shots/{shotId:guid}/clip")]
    public async Task<IActionResult> Clip(Guid shotId, CancellationToken ct) =>
        await BlobResult(await _studio.ShotClipBlobIdAsync(shotId, ct), null, ct);

    // ───────────── costs ─────────────

    [HttpGet("costs")]
    public Task<IActionResult> Costs(CancellationToken ct) => Run(async () => await _studio.GetCostsAsync(ct));

    // ───────────── helpers ─────────────

    private async Task<IActionResult> BlobResult(Guid? blobId, string? downloadName, CancellationToken ct)
    {
        if (blobId == null) return NotFound();
        Response.Headers.CacheControl = "private, max-age=3600";
        if (await _studio.DiskFileAsync(blobId.Value, ct) is { } disk) // streamed from disk, never loaded whole
            return downloadName == null
                ? PhysicalFile(disk.Path, disk.MimeType, enableRangeProcessing: true)
                : PhysicalFile(disk.Path, disk.MimeType, downloadName, enableRangeProcessing: true);
        var blob = await _studio.GetBlobAsync(blobId.Value, ct);
        if (blob == null) return NotFound();
        Response.Headers.CacheControl = "private, max-age=3600";
        return downloadName == null
            ? File(blob.Data, blob.MimeType, enableRangeProcessing: true)
            : File(blob.Data, blob.MimeType, downloadName, enableRangeProcessing: true);
    }

    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return ms.ToArray();
    }
}

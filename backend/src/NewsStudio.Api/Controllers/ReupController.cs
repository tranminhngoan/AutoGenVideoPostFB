using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NewsStudio.Api.Workers;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Infrastructure.Reup;

namespace NewsStudio.Api.Controllers;

/// <summary>
/// "Reup Video" — free processing on the owner's PC (no paid API, no cloud GPU). Writes need the studio key
/// (<see cref="StudioAccess"/>); file downloads stay open so the page can show them in &lt;img&gt; / &lt;video&gt; tags.
/// Uploads are streamed to disk, never held whole in memory.
/// </summary>
[ApiController]
[Route("api/reup")]
public class ReupController : ControllerBase
{
    private const long MaxImageBytes = 15L * 1024 * 1024;
    private const long MaxAudioBytes = 60L * 1024 * 1024;
    private const long MaxBackdropBytes = 400L * 1024 * 1024;
    private const long MaxSourceUpload = ReupService.MaxSourceBytes + 4L * 1024 * 1024;

    private readonly ReupService _reup;
    private readonly IApplicationDbContext _db;
    private readonly IConfiguration _configuration;

    public ReupController(ReupService reup, IApplicationDbContext db, IConfiguration configuration)
    {
        _reup = reup;
        _db = db;
        _configuration = configuration;
    }

    private IActionResult? Denied() => StudioAccess.Denied(Request, _configuration);

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
        catch (HttpRequestException ex) { return BadRequest(new { message = "Không kết nối được: " + ex.Message }); }
    }

    /// <summary>Saves an upload to a temporary file (streamed), runs <paramref name="use"/>, then deletes the file.</summary>
    private async Task<IActionResult> WithUpload(IFormFile? file, long maxBytes, string tooBig, Func<string, Task<object?>> use, CancellationToken ct)
    {
        if (Denied() is { } d) return d;
        if (file == null || file.Length == 0) return BadRequest(new { message = "Chưa chọn file." });
        if (file.Length > maxBytes) return BadRequest(new { message = tooBig });
        var dir = Path.Combine(Path.GetTempPath(), "reup-upload", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var ext = Path.GetExtension(file.FileName);
        var path = Path.Combine(dir, "upload" + (ext.Length is > 1 and <= 6 ? ext.ToLowerInvariant() : ".bin"));
        try
        {
            await using (var dst = System.IO.File.Create(path))
                await file.CopyToAsync(dst, ct);
            return await Run(() => use(path));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    private async Task<IActionResult> FileResult(Guid? blobId, string? downloadName, CancellationToken ct)
    {
        if (await _reup.FileAsync(blobId, ct) is not { } f) return NotFound();
        Response.Headers.CacheControl = "private, max-age=3600";
        return downloadName == null
            ? PhysicalFile(f.Path, f.MimeType, enableRangeProcessing: true)
            : PhysicalFile(f.Path, f.MimeType, downloadName, enableRangeProcessing: true);
    }

    // ───────────── overview / settings / engine ─────────────

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        var pc = await WorkerSwitchService.ReadBeatAsync(_db, WorkerSwitchService.PcKey, ct);
        var online = pc is { Working: true } && WorkerSwitchService.Fresh(pc.HeartbeatUtc, DateTime.UtcNow);
        return await Run(async () => await _reup.OverviewAsync(online, pc?.Machine, ct));
    }

    [HttpGet("settings")]
    public Task<IActionResult> Settings(CancellationToken ct) => Run(async () => await _reup.GetSettingsAsync(ct));

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] ReupSettingsDto input, CancellationToken ct) =>
        Denied() ?? await Run(async () =>
        {
            var saved = await _reup.SaveSettingsAsync(input, ct);
            _reup.RequestProbe(false); // the PC reports the new threads / provider / folder soon
            return saved;
        });

    public sealed record EngineCheckRequest(bool Bench);

    [HttpPost("engine/check")]
    public IActionResult CheckEngine([FromBody] EngineCheckRequest req)
    {
        if (Denied() is { } d) return d;
        _reup.RequestProbe(req.Bench);
        return Ok(new { queued = true });
    }

    // ───────────── characters ─────────────

    public class CharacterForm
    {
        public IFormFile? File { get; set; }
        public string? Name { get; set; }
        public string? Notes { get; set; }
        public bool Consent { get; set; }
    }

    [HttpGet("characters")]
    public Task<IActionResult> Characters(CancellationToken ct) => Run(async () => await _reup.ListCharactersAsync(ct));

    [HttpPost("characters")]
    [RequestSizeLimit(MaxImageBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImageBytes + 1024 * 1024)]
    public Task<IActionResult> AddCharacter([FromForm] CharacterForm form, CancellationToken ct) =>
        WithUpload(form.File, MaxImageBytes, "Ảnh quá lớn (tối đa 15 MB).",
            async path => await _reup.AddCharacterAsync(form.Name ?? "", form.Notes, form.Consent, path, form.File!.ContentType?.ToLowerInvariant() ?? "", ct), ct);

    [HttpPut("characters/{id:guid}")]
    public async Task<IActionResult> UpdateCharacter(Guid id, [FromBody] ReupCharacterUpdateDto input, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _reup.UpdateCharacterAsync(id, input, ct));

    [HttpPost("characters/{id:guid}/recheck")]
    public async Task<IActionResult> RecheckCharacter(Guid id, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _reup.RecheckCharacterAsync(id, ct));

    [HttpDelete("characters/{id:guid}")]
    public async Task<IActionResult> DeleteCharacter(Guid id, CancellationToken ct) =>
        Denied() ?? await Run(async () => { await _reup.DeleteCharacterAsync(id, ct); return null; });

    [HttpGet("characters/{id:guid}/image")]
    public async Task<IActionResult> CharacterImage(Guid id, CancellationToken ct) => await FileResult(await _reup.CharacterBlobAsync(id, false, ct), null, ct);

    [HttpGet("characters/{id:guid}/thumb")]
    public async Task<IActionResult> CharacterThumb(Guid id, CancellationToken ct) => await FileResult(await _reup.CharacterBlobAsync(id, true, ct), null, ct);

    // ───────────── backdrops ─────────────

    public class BackdropForm : ReupBackdropInfoDto
    {
        public IFormFile? File { get; set; }
    }

    [HttpGet("backdrops")]
    public Task<IActionResult> Backdrops([FromQuery] string? category, [FromQuery] string? kind, [FromQuery] string? q, CancellationToken ct) =>
        Run(async () => await _reup.ListBackdropsAsync(category, kind, q, ct));

    [HttpPost("backdrops")]
    [RequestSizeLimit(MaxBackdropBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxBackdropBytes + 1024 * 1024)]
    public Task<IActionResult> AddBackdrop([FromForm] BackdropForm form, CancellationToken ct) =>
        WithUpload(form.File, MaxBackdropBytes, "File quá lớn (tối đa 400 MB).", async path => await _reup.AddBackdropAsync(path, form.File!.FileName, form, ct), ct);

    [HttpPost("backdrops/import")]
    public async Task<IActionResult> ImportBackdrop([FromBody] ReupBackdropImportDto input, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _reup.ImportBackdropAsync(input, ct));

    [HttpGet("stock/search")]
    public async Task<IActionResult> SearchStock([FromQuery] string provider, [FromQuery] string kind, [FromQuery] string q, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _reup.SearchStockAsync(provider, kind, q, ct));

    [HttpPost("stock/import")]
    public async Task<IActionResult> ImportStock([FromBody] ReupStockImportDto input, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _reup.ImportStockAsync(input, ct));

    [HttpPut("backdrops/{id:guid}")]
    public async Task<IActionResult> UpdateBackdrop(Guid id, [FromBody] ReupBackdropInfoDto input, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _reup.UpdateBackdropAsync(id, input, ct));

    [HttpDelete("backdrops/{id:guid}")]
    public async Task<IActionResult> DeleteBackdrop(Guid id, CancellationToken ct) =>
        Denied() ?? await Run(async () => { await _reup.DeleteBackdropAsync(id, ct); return null; });

    [HttpGet("backdrops/{id:guid}/file")]
    public async Task<IActionResult> BackdropFile(Guid id, CancellationToken ct) => await FileResult(await _reup.BackdropBlobAsync(id, false, ct), null, ct);

    [HttpGet("backdrops/{id:guid}/thumb")]
    public async Task<IActionResult> BackdropThumb(Guid id, CancellationToken ct) => await FileResult(await _reup.BackdropBlobAsync(id, true, ct), null, ct);

    // ───────────── jobs ─────────────

    public class SourceForm
    {
        public IFormFile? File { get; set; }
        public string? Title { get; set; }
    }

    public class AudioForm
    {
        public IFormFile? File { get; set; }
    }

    [HttpGet("jobs")]
    public Task<IActionResult> Jobs(CancellationToken ct) => Run(async () => await _reup.ListJobsAsync(ct));

    [HttpGet("jobs/{id:guid}")]
    public Task<IActionResult> Job(Guid id, CancellationToken ct) => Run(async () => await _reup.GetJobAsync(id, ct));

    [HttpPost("jobs")]
    [RequestSizeLimit(MaxSourceUpload)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxSourceUpload)]
    public Task<IActionResult> CreateJob([FromForm] SourceForm form, CancellationToken ct) =>
        WithUpload(form.File, ReupService.MaxSourceBytes, "Video quá lớn (tối đa 600 MB).",
            async path => await _reup.CreateJobAsync(path, form.File!.FileName, form.Title, ct), ct);

    [HttpPut("jobs/{id:guid}")]
    public async Task<IActionResult> UpdateJob(Guid id, [FromBody] ReupJobInputDto input, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _reup.UpdateJobAsync(id, input, ct));

    [HttpPost("jobs/{id:guid}/audio")]
    [RequestSizeLimit(MaxAudioBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAudioBytes + 1024 * 1024)]
    public Task<IActionResult> SetAudio(Guid id, [FromForm] AudioForm form, CancellationToken ct) =>
        WithUpload(form.File, MaxAudioBytes, "File âm thanh quá lớn (tối đa 60 MB).", async path => await _reup.SetAudioAsync(id, path, form.File!.FileName, ct), ct);

    [HttpDelete("jobs/{id:guid}/audio")]
    public async Task<IActionResult> RemoveAudio(Guid id, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _reup.RemoveAudioAsync(id, ct));

    [HttpPost("jobs/{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _reup.StartAsync(id, true, ct));

    [HttpPost("jobs/{id:guid}/start")]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _reup.StartAsync(id, false, ct));

    [HttpPost("jobs/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _reup.CancelAsync(id, ct));

    [HttpDelete("jobs/{id:guid}")]
    public async Task<IActionResult> DeleteJob(Guid id, CancellationToken ct) =>
        Denied() ?? await Run(async () => { await _reup.DeleteJobAsync(id, ct); return null; });

    [HttpGet("jobs/{id:guid}/thumb")]
    public async Task<IActionResult> JobThumb(Guid id, CancellationToken ct) => await FileResult(await _reup.JobBlobAsync(id, "thumb", ct), null, ct);

    [HttpGet("jobs/{id:guid}/source")]
    public async Task<IActionResult> JobSource(Guid id, CancellationToken ct) => await FileResult(await _reup.JobBlobAsync(id, "source", ct), null, ct);

    [HttpGet("jobs/{id:guid}/preview")]
    public async Task<IActionResult> JobPreview(Guid id, CancellationToken ct) => await FileResult(await _reup.JobBlobAsync(id, "preview", ct), null, ct);

    [HttpGet("jobs/{id:guid}/audio")]
    public async Task<IActionResult> JobAudio(Guid id, CancellationToken ct) => await FileResult(await _reup.JobBlobAsync(id, "audio", ct), null, ct);

    [HttpGet("jobs/{id:guid}/output")]
    public async Task<IActionResult> JobOutput(Guid id, [FromQuery] bool download = false, CancellationToken ct = default) =>
        await FileResult(await _reup.JobBlobAsync(id, "output", ct), download ? $"reup_{id.ToString("N")[..8]}.mp4" : null, ct);
}

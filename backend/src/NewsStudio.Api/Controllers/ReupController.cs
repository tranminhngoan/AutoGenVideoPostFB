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

    /// <summary>
    /// A file sent either whole (multipart "file") or beforehand in small pieces (<see cref="UploadChunk"/>) and named
    /// here by "uploadId": big videos go in 4 MB pieces because the Vercel proxy in front of the API refuses long / big requests.
    /// </summary>
    public class UploadFields
    {
        public IFormFile? File { get; set; }
        public string? UploadId { get; set; }
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
    }

    /// <summary>Puts the upload in a temporary file, runs <paramref name="use"/> (path, file name, content type), then deletes it.</summary>
    private async Task<IActionResult> WithUpload(UploadFields form, long maxBytes, string tooBig, Func<string, string, string, Task<object?>> use, CancellationToken ct)
    {
        if (Denied() is { } d) return d;
        string? dir = null;
        try
        {
            string path, name, type;
            if (!string.IsNullOrWhiteSpace(form.UploadId))
            {
                if (!Guid.TryParse(form.UploadId, out var uploadId)) return BadRequest(new { message = "Mã tải lên không hợp lệ." });
                dir = ChunkDir(uploadId);
                path = Path.Combine(dir, "data.bin");
                if (!System.IO.File.Exists(path) || new FileInfo(path).Length == 0) return BadRequest(new { message = "Chưa nhận được file (tải lên bị gián đoạn) — chọn file lại." });
                if (new FileInfo(path).Length > maxBytes) return BadRequest(new { message = tooBig });
                name = string.IsNullOrWhiteSpace(form.FileName) ? "upload.bin" : Path.GetFileName(form.FileName);
                type = form.ContentType?.Trim().ToLowerInvariant() ?? "";
            }
            else
            {
                var file = form.File;
                if (file == null || file.Length == 0) return BadRequest(new { message = "Chưa chọn file." });
                if (file.Length > maxBytes) return BadRequest(new { message = tooBig });
                dir = Path.Combine(Path.GetTempPath(), "reup-upload", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "data.bin");
                await using (var dst = System.IO.File.Create(path))
                    await file.CopyToAsync(dst, ct);
                name = Path.GetFileName(file.FileName);
                type = file.ContentType?.ToLowerInvariant() ?? "";
            }
            return await Run(() => use(path, name, type));
        }
        finally
        {
            if (dir != null) { try { Directory.Delete(dir, true); } catch { /* best effort */ } }
        }
    }

    private static string ChunkDir(Guid uploadId) => Path.Combine(Path.GetTempPath(), "reup-chunks", uploadId.ToString("N"));

    /// <summary>Starts a piece-by-piece upload (big videos through the Vercel proxy).</summary>
    [HttpPost("uploads")]
    public IActionResult StartUpload()
    {
        if (Denied() is { } d) return d;
        var id = Guid.NewGuid();
        Directory.CreateDirectory(ChunkDir(id));
        CleanOldChunks();
        return Ok(new { uploadId = id });
    }

    /// <summary>One piece (up to 8 MB) written at its byte offset; sending the same piece again is harmless (retries).</summary>
    [HttpPut("uploads/{id:guid}")]
    [RequestSizeLimit(9 * 1024 * 1024)]
    [Microsoft.AspNetCore.RateLimiting.DisableRateLimiting] // a 600 MB video is ~150 pieces
    public async Task<IActionResult> UploadChunk(Guid id, [FromQuery] long offset, CancellationToken ct)
    {
        if (Denied() is { } d) return d;
        var dir = ChunkDir(id);
        if (!Directory.Exists(dir)) return NotFound(new { message = "Phiên tải lên đã hết hạn — chọn file lại." });
        if (offset < 0 || offset > ReupService.MaxSourceBytes) return BadRequest(new { message = "Vị trí không hợp lệ." });
        var path = Path.Combine(dir, "data.bin");
        await using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous))
        {
            if (offset > fs.Length) return BadRequest(new { message = "Thiếu phần trước của file — tải lại." });
            fs.Position = offset;
            await Request.Body.CopyToAsync(fs, 1 << 16, ct);
            if (fs.Length > ReupService.MaxSourceBytes + 1024 * 1024) return BadRequest(new { message = "File quá lớn (tối đa 600 MB)." });
        }
        return Ok(new { received = new FileInfo(path).Length });
    }

    /// <summary>Pieces of uploads that were never finished (closed tab...) are removed after 6 hours.</summary>
    private static void CleanOldChunks()
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "reup-chunks");
            if (!Directory.Exists(root)) return;
            foreach (var dir in Directory.GetDirectories(root))
                if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddHours(-6))
                    try { Directory.Delete(dir, true); } catch { /* in use */ }
        }
        catch (IOException) { /* best effort */ }
    }

    private async Task<IActionResult> FileResult(Guid? blobId, string? downloadName, CancellationToken ct)
    {
        // the browser streams the file straight from the bucket (R2: no egress fee), not through Railway
        if (await _reup.BucketUrlAsync(blobId, downloadName, ct) is { } direct)
        {
            Response.Headers.CacheControl = "private, max-age=1800"; // the link itself is valid for 1 hour
            return Redirect(direct);
        }
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
        var now = DateTime.UtcNow;
        var online = pc is { Working: true } && WorkerSwitchService.Fresh(pc.HeartbeatUtc, now);
        // on, but on an older version than the cloud: it stays out of the work until its updater installs the new one
        var updating = !online && WorkerSwitchService.Fresh(pc?.SeenUtc, now);
        return await Run(async () =>
        {
            var o = await _reup.OverviewAsync(online, pc?.Machine, ct);
            o.PcUpdating = updating;
            return o;
        });
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

    public class CharacterForm : UploadFields
    {
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
        WithUpload(form, MaxImageBytes, "Ảnh quá lớn (tối đa 15 MB).",
            async (path, _, type) => await _reup.AddCharacterAsync(form.Name ?? "", form.Notes, form.Consent, path, type, ct), ct);

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

    public class BackdropForm : UploadFields
    {
        public string? Name { get; set; }
        public string? Category { get; set; }
        public string? Tags { get; set; }
        public string? SourceName { get; set; }
        public string? SourceUrl { get; set; }
        public string? Author { get; set; }
        public string? LicenseName { get; set; }
        public string? LicenseUrl { get; set; }

        public ReupBackdropInfoDto Info() => new()
        {
            Name = Name, Category = Category, Tags = Tags, SourceName = SourceName, SourceUrl = SourceUrl,
            Author = Author, LicenseName = LicenseName, LicenseUrl = LicenseUrl
        };
    }

    [HttpGet("backdrops")]
    public Task<IActionResult> Backdrops([FromQuery] string? category, [FromQuery] string? kind, [FromQuery] string? q, CancellationToken ct) =>
        Run(async () => await _reup.ListBackdropsAsync(category, kind, q, ct));

    [HttpPost("backdrops")]
    [RequestSizeLimit(MaxBackdropBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxBackdropBytes + 1024 * 1024)]
    public Task<IActionResult> AddBackdrop([FromForm] BackdropForm form, CancellationToken ct) =>
        WithUpload(form, MaxBackdropBytes, "File quá lớn (tối đa 400 MB).", async (path, name, _) => await _reup.AddBackdropAsync(path, name, form.Info(), ct), ct);

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

    public class SourceForm : UploadFields
    {
        public string? Title { get; set; }
    }

    [HttpGet("jobs")]
    public Task<IActionResult> Jobs(CancellationToken ct) => Run(async () => await _reup.ListJobsAsync(ct));

    [HttpGet("jobs/{id:guid}")]
    public Task<IActionResult> Job(Guid id, CancellationToken ct) => Run(async () => await _reup.GetJobAsync(id, ct));

    [HttpPost("jobs")]
    [RequestSizeLimit(MaxSourceUpload)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxSourceUpload)]
    public Task<IActionResult> CreateJob([FromForm] SourceForm form, CancellationToken ct) =>
        WithUpload(form, ReupService.MaxSourceBytes, "Video quá lớn (tối đa 600 MB).",
            async (path, name, _) => await _reup.CreateJobAsync(path, name, form.Title, ct), ct);

    [HttpPut("jobs/{id:guid}")]
    public async Task<IActionResult> UpdateJob(Guid id, [FromBody] ReupJobInputDto input, CancellationToken ct) =>
        Denied() ?? await Run(async () => await _reup.UpdateJobAsync(id, input, ct));

    [HttpPost("jobs/{id:guid}/audio")]
    [RequestSizeLimit(MaxAudioBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAudioBytes + 1024 * 1024)]
    public Task<IActionResult> SetAudio(Guid id, [FromForm] UploadFields form, CancellationToken ct) =>
        WithUpload(form, MaxAudioBytes, "File âm thanh quá lớn (tối đa 60 MB).", async (path, name, _) => await _reup.SetAudioAsync(id, path, name, ct), ct);

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

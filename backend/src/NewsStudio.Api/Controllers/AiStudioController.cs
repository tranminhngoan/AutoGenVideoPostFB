using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NewsStudio.Api.Workers;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Infrastructure.AiStudio;

namespace NewsStudio.Api.Controllers;

/// <summary>
/// AI Video Studio — reference video → local analysis on the owner's PC → scene prompts → new clips with Google Veo.
/// Every data call needs the studio key (<see cref="StudioAccess"/>); media files (thumbnails, keyframes, clips) stay
/// open by their random ids so the page can show them in &lt;img&gt; / &lt;video&gt; tags, and only files that belong to
/// AI Video Studio are served. Uploads are streamed to disk in 4 MB pieces, never held whole in memory.
/// </summary>
[ApiController]
[Route("api/ai-studio")]
public class AiStudioController : ControllerBase
{
    private const long MaxSourceUpload = AiStudioService.MaxSourceBytes + 4L * 1024 * 1024;

    private readonly AiStudioService _studio;
    private readonly IApplicationDbContext _db;
    private readonly IConfiguration _configuration;

    public AiStudioController(AiStudioService studio, IApplicationDbContext db, IConfiguration configuration)
    {
        _studio = studio;
        _db = db;
        _configuration = configuration;
    }

    private IActionResult? Denied() => StudioAccess.Denied(Request, _configuration);

    private async Task<IActionResult> Run(Func<Task<object?>> action)
    {
        if (Denied() is { } d) return d;
        try
        {
            var result = await action();
            return result == null ? NoContent() : Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ───────────── uploads (pieces) ─────────────

    public class UploadFields
    {
        public IFormFile? File { get; set; }
        public string? UploadId { get; set; }
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
        public string? Title { get; set; }
        public string? Topic { get; set; }
    }

    private static string ChunkDir(Guid uploadId) => Path.Combine(Path.GetTempPath(), "ai-studio-chunks", uploadId.ToString("N"));

    /// <summary>Puts the upload in a temporary file, runs <paramref name="use"/> (path, file name, content type), then deletes it.</summary>
    private async Task<IActionResult> WithUpload(UploadFields form, long maxBytes, Func<string, string, string, Task<object?>> use, CancellationToken ct)
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
                if (new FileInfo(path).Length > maxBytes) return BadRequest(new { message = $"File quá lớn (tối đa {maxBytes / 1048576} MB)." });
                name = string.IsNullOrWhiteSpace(form.FileName) ? "upload.bin" : Path.GetFileName(form.FileName);
                type = form.ContentType?.Trim().ToLowerInvariant() ?? "";
            }
            else
            {
                var file = form.File;
                if (file == null || file.Length == 0) return BadRequest(new { message = "Chưa chọn file." });
                if (file.Length > maxBytes) return BadRequest(new { message = $"File quá lớn (tối đa {maxBytes / 1048576} MB)." });
                dir = Path.Combine(Path.GetTempPath(), "ai-studio-upload", Guid.NewGuid().ToString("N"));
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
    [Microsoft.AspNetCore.RateLimiting.DisableRateLimiting] // a 500 MB video is ~125 pieces
    public async Task<IActionResult> UploadChunk(Guid id, [FromQuery] long offset, CancellationToken ct)
    {
        if (Denied() is { } d) return d;
        var dir = ChunkDir(id);
        if (!Directory.Exists(dir)) return NotFound(new { message = "Phiên tải lên đã hết hạn — chọn file lại." });
        if (offset < 0 || offset > AiStudioService.MaxSourceBytes) return BadRequest(new { message = "Vị trí không hợp lệ." });
        var path = Path.Combine(dir, "data.bin");
        await using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous))
        {
            if (offset > fs.Length) return BadRequest(new { message = "Thiếu phần trước của file — tải lại." });
            fs.Position = offset;
            await Request.Body.CopyToAsync(fs, 1 << 16, ct);
            if (fs.Length > AiStudioService.MaxSourceBytes + 1024 * 1024) return BadRequest(new { message = "File quá lớn (tối đa 500 MB)." });
        }
        return Ok(new { received = new FileInfo(path).Length });
    }

    private static void CleanOldChunks()
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "ai-studio-chunks");
            if (!Directory.Exists(root)) return;
            foreach (var dir in Directory.GetDirectories(root))
                if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddHours(-6))
                    try { Directory.Delete(dir, true); } catch { /* in use */ }
        }
        catch (IOException) { /* best effort */ }
    }

    // ───────────── overview / settings / engine ─────────────

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        var pc = await WorkerSwitchService.ReadBeatAsync(_db, WorkerSwitchService.PcKey, ct);
        var now = DateTime.UtcNow;
        var online = pc is { Working: true } && WorkerSwitchService.Fresh(pc.HeartbeatUtc, now);
        var updating = !online && WorkerSwitchService.Fresh(pc?.SeenUtc, now);
        return await Run(async () => await _studio.OverviewAsync(online, updating, pc?.Machine, ct));
    }

    [HttpGet("settings")]
    public Task<IActionResult> Settings(CancellationToken ct) => Run(async () => await _studio.GetSettingsAsync(ct));

    [HttpPut("settings")]
    public Task<IActionResult> SaveSettings([FromBody] AiStudioSettingsDto input, CancellationToken ct) =>
        Run(async () =>
        {
            var saved = await _studio.SaveSettingsAsync(input, ct);
            _studio.RequestProbe(false);
            return saved;
        });

    public sealed record EngineCheckRequest(bool Bench);

    [HttpPost("engine/check")]
    public Task<IActionResult> CheckEngine([FromBody] EngineCheckRequest req) =>
        Run(() =>
        {
            _studio.RequestProbe(req.Bench);
            return Task.FromResult<object?>(new { queued = true });
        });

    // ───────────── projects ─────────────

    [HttpGet("projects")]
    public Task<IActionResult> Projects(CancellationToken ct) => Run(async () => await _studio.ListProjectsAsync(ct));

    [HttpGet("projects/{id:guid}")]
    public Task<IActionResult> Project(Guid id, CancellationToken ct) => Run(async () => await _studio.GetProjectAsync(id, ct));

    [HttpPost("projects")]
    [RequestSizeLimit(MaxSourceUpload)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxSourceUpload)]
    public Task<IActionResult> CreateProject([FromForm] UploadFields form, CancellationToken ct) =>
        WithUpload(form, AiStudioService.MaxSourceBytes, async (path, name, _) => await _studio.CreateProjectAsync(path, name, form.Title, ct, form.Topic), ct);

    [HttpPost("projects/{id:guid}/source")]
    [RequestSizeLimit(MaxSourceUpload)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxSourceUpload)]
    public Task<IActionResult> ReplaceSource(Guid id, [FromForm] UploadFields form, CancellationToken ct) =>
        WithUpload(form, AiStudioService.MaxSourceBytes, async (path, name, _) => await _studio.ReplaceSourceAsync(id, path, name, ct), ct);

    [HttpPut("projects/{id:guid}")]
    public Task<IActionResult> UpdateProject(Guid id, [FromBody] AiStudioProjectUpdateDto input, CancellationToken ct) =>
        Run(async () => await _studio.UpdateProjectAsync(id, input, ct));

    [HttpDelete("projects/{id:guid}")]
    public Task<IActionResult> DeleteProject(Guid id, CancellationToken ct) =>
        Run(async () => { await _studio.DeleteProjectAsync(id, ct); return null; });

    public sealed record AnalyzeRequest(bool Force);

    [HttpPost("projects/{id:guid}/analyze")]
    public Task<IActionResult> Analyze(Guid id, [FromBody] AnalyzeRequest req, CancellationToken ct) =>
        Run(async () => await _studio.StartAnalysisAsync(id, req.Force, ct));

    [HttpPost("projects/{id:guid}/cancel")]
    public Task<IActionResult> CancelAnalysis(Guid id, CancellationToken ct) => Run(async () => await _studio.CancelAnalysisAsync(id, ct));

    // ───────────── prompts / storyboard ─────────────

    [HttpPut("projects/{id:guid}/prompt-settings")]
    public Task<IActionResult> PromptSettings(Guid id, [FromBody] AiStudioPromptSettingsDto input, CancellationToken ct) =>
        Run(async () => await _studio.SetPromptSettingsAsync(id, input, ct));

    [HttpPut("projects/{id:guid}/bibles")]
    public Task<IActionResult> Bibles(Guid id, [FromBody] AiStudioBiblesDto input, CancellationToken ct) =>
        Run(async () => await _studio.SetBiblesAsync(id, input, ct));

    [HttpPut("projects/{id:guid}/order")]
    public Task<IActionResult> Reorder(Guid id, [FromBody] List<Guid> sceneIds, CancellationToken ct) =>
        Run(async () => await _studio.ReorderAsync(id, sceneIds, ct));

    [HttpPut("projects/{id:guid}/scenes/{sceneId:guid}")]
    public Task<IActionResult> UpdateScene(Guid id, Guid sceneId, [FromBody] AiStudioSceneUpdateDto input, CancellationToken ct) =>
        Run(async () => await _studio.UpdateSceneAsync(id, sceneId, input, ct));

    [HttpPut("projects/{id:guid}/scenes/{sceneId:guid}/prompt")]
    public Task<IActionResult> EditPrompt(Guid id, Guid sceneId, [FromBody] AiStudioScenePromptDto input, CancellationToken ct) =>
        Run(async () => await _studio.EditScenePromptAsync(id, sceneId, input.Text, ct));

    // ───────────── narration (read again by an AI narrator) ─────────────

    [HttpPut("projects/{id:guid}/narrator")]
    public Task<IActionResult> Narrator(Guid id, [FromBody] AiStudioNarratorDto input, CancellationToken ct) =>
        Run(async () => await _studio.SetNarratorAsync(id, input, ct));

    [HttpPost("projects/{id:guid}/narration/reset")]
    public Task<IActionResult> ResetNarration(Guid id, CancellationToken ct) => Run(async () => await _studio.ResetNarrationAsync(id, ct));

    [HttpPut("projects/{id:guid}/scenes/{sceneId:guid}/narration")]
    public Task<IActionResult> EditNarration(Guid id, Guid sceneId, [FromBody] AiStudioNarrationDto input, CancellationToken ct) =>
        Run(async () => await _studio.EditNarrationAsync(id, sceneId, input.Text, ct));

    [HttpPost("projects/{id:guid}/review-logic")]
    public Task<IActionResult> ReviewLogic(Guid id, CancellationToken ct) => Run(async () => await _studio.ReviewLogicAsync(id, ct));

    [HttpPost("projects/{id:guid}/fit")]
    public Task<IActionResult> FitDuration(Guid id, CancellationToken ct) => Run(async () => await _studio.FitDurationAsync(id, ct));

    [HttpPost("projects/{id:guid}/narration/condense")]
    public Task<IActionResult> CondenseNarration(Guid id, CancellationToken ct) => Run(async () => await _studio.CondenseNarrationAsync(id, ct));

    [HttpPut("projects/{id:guid}/brand")]
    public Task<IActionResult> Brand(Guid id, [FromBody] AiStudioBrandDto input, CancellationToken ct) => Run(async () => await _studio.SetBrandAsync(id, input, ct));

    [HttpPut("projects/{id:guid}/caption")]
    public Task<IActionResult> Caption(Guid id, [FromBody] AiStudioNarrationDto input, CancellationToken ct) =>
        Run(async () => await _studio.SetCaptionAsync(id, input.Text, ct));

    [HttpPost("projects/{id:guid}/caption/write")]
    public Task<IActionResult> WriteCaption(Guid id, CancellationToken ct) => Run(async () => await _studio.WriteCaptionAsync(id, ct));

    [HttpPost("projects/{id:guid}/narration/speak")]
    public Task<IActionResult> SpeakNarration(Guid id, CancellationToken ct) => Run(async () => await _studio.SpeakNarrationAsync(id, ct));

    [HttpPost("projects/{id:guid}/scenes/{sceneId:guid}/regenerate-prompt")]
    public Task<IActionResult> RegeneratePrompt(Guid id, Guid sceneId, CancellationToken ct) =>
        Run(async () => await _studio.RegenerateScenePromptAsync(id, sceneId, ct));

    [HttpGet("projects/{id:guid}/versions")]
    public Task<IActionResult> Versions(Guid id, [FromQuery] Guid? sceneId, CancellationToken ct) =>
        Run(async () => await _studio.VersionsAsync(id, sceneId, ct));

    public sealed record RestoreRequest(Guid VersionId);

    [HttpPost("projects/{id:guid}/versions/restore")]
    public Task<IActionResult> Restore(Guid id, [FromBody] RestoreRequest req, CancellationToken ct) =>
        Run(async () => await _studio.RestoreVersionAsync(id, req.VersionId, ct));

    // ───────────── clip generator (Veo) ─────────────

    [HttpPut("projects/{id:guid}/generator")]
    public Task<IActionResult> Generator(Guid id, [FromBody] AiStudioGeneratorSettingsDto input, CancellationToken ct) =>
        Run(async () => await _studio.SetGeneratorAsync(id, input, ct));

    [HttpPost("projects/{id:guid}/reference-image")]
    [RequestSizeLimit(AiStudioService.MaxImageBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = AiStudioService.MaxImageBytes + 1024 * 1024)]
    public Task<IActionResult> ReferenceImage(Guid id, [FromForm] UploadFields form, CancellationToken ct) =>
        WithUpload(form, AiStudioService.MaxImageBytes, async (path, _, type) => await _studio.SetReferenceImageAsync(id, path, type, ct), ct);

    [HttpDelete("projects/{id:guid}/reference-image")]
    public Task<IActionResult> RemoveReferenceImage(Guid id, CancellationToken ct) => Run(async () => await _studio.RemoveReferenceImageAsync(id, ct));

    public sealed record EstimateRequest(List<Guid> SceneIds);

    [HttpPost("projects/{id:guid}/estimate")]
    public Task<IActionResult> Estimate(Guid id, [FromBody] EstimateRequest req, CancellationToken ct) =>
        Run(async () => await _studio.EstimateAsync(id, req.SceneIds ?? new(), ct));

    [HttpPost("projects/{id:guid}/generate")]
    public Task<IActionResult> Generate(Guid id, [FromBody] AiStudioGenerateRequestDto req, CancellationToken ct) =>
        Run(async () => await _studio.GenerateAsync(id, req, ct));

    [HttpPost("projects/{id:guid}/assemble")]
    public Task<IActionResult> Assemble(Guid id, CancellationToken ct) => Run(async () => await _studio.AssembleAsync(id, ct));

    [HttpGet("generations")]
    public Task<IActionResult> Generations([FromQuery] Guid? projectId, CancellationToken ct) => Run(async () => await _studio.GenerationsAsync(projectId, ct));

    [HttpPost("generations/{genId:guid}/recover")]
    public Task<IActionResult> Recover(Guid genId, CancellationToken ct) => Run(async () => await _studio.RecoverAsync(genId, ct));

    [HttpPost("generations/{genId:guid}/cancel")]
    public Task<IActionResult> CancelGeneration(Guid genId, CancellationToken ct) => Run(async () => await _studio.CancelGenerationAsync(genId, ct));

    // ───────────── media (open by id, AI Video Studio files only) ─────────────

    private async Task<IActionResult> FileResult(Guid? blobId, string? downloadName, CancellationToken ct)
    {
        if (blobId == null) return NotFound();
        // the browser streams the file straight from the bucket (R2: no egress fee), not through Railway
        if (await _studio.BucketUrlAsync(blobId, downloadName, ct) is { } direct)
        {
            Response.Headers.CacheControl = "private, max-age=1800";
            return Redirect(direct);
        }
        if (await _studio.FileAsync(blobId, ct) is not { } f) return NotFound();
        Response.Headers.CacheControl = "private, max-age=3600";
        return downloadName == null
            ? PhysicalFile(f.Path, f.MimeType, enableRangeProcessing: true)
            : PhysicalFile(f.Path, f.MimeType, downloadName, enableRangeProcessing: true);
    }

    [HttpGet("projects/{id:guid}/source")]
    public async Task<IActionResult> Source(Guid id, CancellationToken ct) => await FileResult(await _studio.ProjectBlobAsync(id, "source", ct), null, ct);

    [HttpGet("projects/{id:guid}/thumb")]
    public async Task<IActionResult> Thumb(Guid id, CancellationToken ct) => await FileResult(await _studio.ProjectBlobAsync(id, "thumb", ct), null, ct);

    [HttpGet("projects/{id:guid}/reference-image")]
    public async Task<IActionResult> ReferenceImageFile(Guid id, CancellationToken ct) => await FileResult(await _studio.ProjectBlobAsync(id, "reference", ct), null, ct);

    [HttpGet("projects/{id:guid}/final")]
    public async Task<IActionResult> Final(Guid id, [FromQuery] bool download = false, CancellationToken ct = default) =>
        await FileResult(await _studio.ProjectBlobAsync(id, "final", ct), download ? $"ai_studio_{id.ToString("N")[..8]}.mp4" : null, ct);

    [HttpGet("scenes/{sceneId:guid}/thumb")]
    public async Task<IActionResult> SceneThumb(Guid sceneId, CancellationToken ct) => await FileResult(await _studio.SceneThumbAsync(sceneId, ct), null, ct);

    [HttpGet("projects/{id:guid}/narration")]
    public async Task<IActionResult> NarrationTake(Guid id, CancellationToken ct) => await FileResult(await _studio.ProjectBlobAsync(id, "narration", ct), null, ct);

    [HttpGet("scenes/{sceneId:guid}/frames/{blobId:guid}")]
    public async Task<IActionResult> Frame(Guid sceneId, Guid blobId, CancellationToken ct) => await FileResult(await _studio.FrameBlobAsync(sceneId, blobId, ct), null, ct);

    [HttpGet("generations/{genId:guid}/clip")]
    public async Task<IActionResult> Clip(Guid genId, [FromQuery] bool download = false, CancellationToken ct = default) =>
        await FileResult(await _studio.ClipBlobAsync(genId, ct), download ? $"scene_{genId.ToString("N")[..8]}.mp4" : null, ct);
}

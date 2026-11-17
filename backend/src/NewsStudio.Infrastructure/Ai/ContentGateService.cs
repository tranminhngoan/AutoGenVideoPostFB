using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Infrastructure.Ai;

/// <summary>
/// The "Kiểm chứng" step of the pipeline, run after the scenes are planned and BEFORE the paid voice-over:
/// free checks of the script against the article (numbers, source sentences, chart data), one AI editorial
/// pass (headline vs article + caption + first comment, reused at posting time), the picture checks, and the
/// resulting publication gate.
/// </summary>
public class ContentGateService : IContentGateService
{
    private readonly IApplicationDbContext _context;
    private readonly IEditorialAssistantService _editorial;
    private readonly Rendering.SceneImageService _images;
    private readonly ILogger<ContentGateService> _logger;

    public ContentGateService(IApplicationDbContext context, IEditorialAssistantService editorial, Rendering.SceneImageService images, ILogger<ContentGateService> logger)
    {
        _images = images;
        _context = context;
        _editorial = editorial;
        _logger = logger;
    }

    public async Task<FactCheckReportDto> CheckAsync(Guid videoProjectId, bool runEditorial = true, CancellationToken cancellationToken = default)
    {
        var project = await _context.VideoProjects
            .Include(p => p.Script)
            .Include(p => p.Article)
            .Include(p => p.Scenes).ThenInclude(s => s.SelectedArticleImage)
            .FirstOrDefaultAsync(p => p.Id == videoProjectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy video.");
        var articleText = project.Article?.ContentBody ?? string.Empty;

        // 1. Free checks (numbers, source sentences, chart)
        FactSheetDto? sheet = null;
        if (project.Script?.FactSheetJson is { Length: > 0 } json)
        {
            try { sheet = JsonSerializer.Deserialize<FactSheetDto>(json); }
            catch (JsonException) { sheet = null; }
        }
        if (sheet != null && project.Script != null)
        {
            FactCheck.Check(sheet, project.Script.VoiceOverScript, articleText);
            project.Script.FactSheetJson = JsonSerializer.Serialize(sheet);
        }

        // 2. One AI pass: headline vs article, caption, first comment (kept for posting)
        EditorialPassDto? pass = null;
        if (runEditorial && project.Script != null)
        {
            try
            {
                pass = await _editorial.RunAsync(videoProjectId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Editorial pass for {ProjectId} failed: {Message}", videoProjectId, ex.Message);
            }
        }
        else if (project.EditorialPassJson is { Length: > 0 } passJson)
        {
            try { pass = JsonSerializer.Deserialize<EditorialPassDto>(passJson); }
            catch (JsonException) { pass = null; }
        }

        // 3. Pictures: pick them now (article photos, the subject's licensed picture, chart) so the checks see
        //    the real ones — a story without any usable picture stops here, before the paid voice-over
        if (runEditorial) await _images.EnsureSceneImagesAsync(project, cancellationToken);
        var pictureProblems = PictureProblems(project, sheet);

        // 4. Gate
        var gate = QualityGate.Evaluate(sheet, pass, articleText.Length, pictureProblems);
        project.GateStatus = gate.Status;
        project.GateJson = JsonSerializer.Serialize(gate);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Gate of {ProjectId}: {Status} ({Reasons})", videoProjectId, gate.Status, string.Join(" | ", gate.Reasons));

        return await GetAsync(videoProjectId, cancellationToken);
    }

    public static List<ImageProvenanceDto> Provenance(Domain.Entities.VideoProject project) =>
        project.Scenes
            .Where(s => s.SelectedArticleImage != null)
            .GroupBy(s => s.SelectedArticleImage!.Id)
            .Select(g =>
            {
                var i = g.First().SelectedArticleImage!;
                return new ImageProvenanceDto
                {
                    ImageId = i.Id,
                    Url = i.OriginalImageUrl.StartsWith("chart://", StringComparison.Ordinal) ? null : i.OriginalImageUrl,
                    ImageType = i.ImageType ?? Common.ImageKinds.Real,
                    SourceName = i.SourceName ?? project.Publisher,
                    SourcePageUrl = i.SourcePageUrl ?? project.SourceUrl,
                    LicenseType = i.LicenseType ?? "Ảnh báo — dùng có ghi nguồn",
                    LicenseEvidence = i.LicenseEvidence,
                    SubjectName = i.SubjectName,
                    SubjectType = i.SubjectType,
                    VerificationStatus = i.VerificationStatus,
                    Confidence = i.Confidence,
                    Caption = i.Caption,
                    Scenes = g.Select(s => s.SceneNumber).OrderBy(n => n).ToList()
                };
            })
            .OrderBy(p => p.Scenes.FirstOrDefault())
            .ToList();

    public async Task<FactCheckReportDto> GetAsync(Guid videoProjectId, CancellationToken cancellationToken = default)
    {
        var project = await _context.VideoProjects.AsNoTracking()
            .Include(p => p.Script)
            .Include(p => p.Article)
            .Include(p => p.Scenes).ThenInclude(s => s.SelectedArticleImage)
            .FirstOrDefaultAsync(p => p.Id == videoProjectId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy video.");
        T? Read<T>(string? json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<T>(json); } catch (JsonException) { return null; }
        }
        return new FactCheckReportDto
        {
            Gate = Read<QualityGateDto>(project.GateJson) ?? new QualityGateDto { Status = project.GateStatus },
            FactSheet = Read<FactSheetDto>(project.Script?.FactSheetJson),
            EditorialPass = Read<EditorialPassDto>(project.EditorialPassJson),
            Images = Provenance(project),
            ArticleId = project.ArticleId,
            ArticleUrl = string.IsNullOrWhiteSpace(project.Article?.CanonicalUrl) ? project.SourceUrl : project.Article.CanonicalUrl,
            ArticlePublisher = project.Article?.Publisher ?? project.Publisher,
            ArticlePublishedAtUtc = project.Article?.PublishedAtUtc
        };
    }

    /// <summary>
    /// A person (category A) must never be shown with a picture found by keyword search (it may be someone
    /// else); every video needs real pictures.
    /// </summary>
    public static List<string> PictureProblems(Domain.Entities.VideoProject project, FactSheetDto? sheet)
    {
        var problems = new List<string>();
        var images = project.Scenes.Select(s => s.SelectedArticleImage).Where(i => i != null).Select(i => i!).DistinctBy(i => i.Id).ToList();
        if (images.Count == 0)
        {
            problems.Add("Video chưa có ảnh nào: mỗi video phải có hình ảnh thật.");
            return problems;
        }
        var people = sheet?.Entities.Where(e => e.Category == "A").Select(e => e.Name).ToList() ?? new List<string>();
        foreach (var img in images.Where(i => i.SubjectType == "A" && i.VerificationStatus != "VERIFIED"))
            problems.Add($"Ảnh nhân vật \"{img.SubjectName}\" chưa xác minh đúng người.");
        if (people.Count > 0 && images.Any(i => i.ImageType == "LICENSED" && i.Status == Common.ImageStatus.WebSearch && i.SubjectType == null))
            problems.Add("Tin về nhân vật có dùng ảnh minh hoạ tìm theo từ khoá: kiểm tra không nhầm người.");
        return problems;
    }
}

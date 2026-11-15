using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Infrastructure.Services;

public class ScriptService : IScriptService
{
    private readonly IApplicationDbContext _context;
    private readonly IAiEditorFactory _aiFactory;
    private readonly IScriptPlannerService _plannerService;
    private readonly IArticleService _articleService;
    private readonly ILogger<ScriptService> _logger;

    public ScriptService(
        IApplicationDbContext context,
        IAiEditorFactory aiFactory,
        IScriptPlannerService plannerService,
        IArticleService articleService,
        ILogger<ScriptService> logger)
    {
        _context = context;
        _aiFactory = aiFactory;
        _plannerService = plannerService;
        _articleService = articleService;
        _logger = logger;
    }

    public async Task<VideoScriptResponseDto> GenerateScriptForProjectAsync(
        Guid projectId,
        GenerateScriptRequestDto? options = null,
        CancellationToken cancellationToken = default)
    {
        var project = await _context.VideoProjects
            .Include(p => p.Article)
                .ThenInclude(a => a!.Images)
            .Include(p => p.Script)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            throw new KeyNotFoundException($"VideoProject '{projectId}' not found.");
        }

        // If article not yet linked, extract it automatically
        if (project.Article == null && !string.IsNullOrWhiteSpace(project.SourceUrl))
        {
            _logger.LogInformation("Project {ProjectId} has no article linked. Extracting from SourceUrl {Url}", projectId, project.SourceUrl);
            var extractedDto = await _articleService.ExtractAndSaveArticleAsync(project.SourceUrl, cancellationToken);
            project.ArticleId = extractedDto.Id;
            await _context.SaveChangesAsync(cancellationToken);

            project.Article = await _context.Articles
                .Include(a => a.Images)
                .FirstOrDefaultAsync(a => a.Id == extractedDto.Id, cancellationToken);
        }

        var article = project.Article;
        if (article == null)
        {
            throw new InvalidOperationException($"Cannot generate script: Video project {projectId} has no article content.");
        }

        var providerName = options?.AiProvider ?? "Gemini";
        var editor = _aiFactory.GetEditorService(providerName);

        var promptInput = new EditorialScriptPromptInput
        {
            ProjectId = projectId,
            ArticleTitle = article.Title,
            Publisher = article.Publisher,
            Author = article.Author,
            PublishedDate = article.PublishedAtUtc,
            CanonicalUrl = article.CanonicalUrl,
            ContentBody = article.ContentBody,
            TargetDurationSeconds = options?.TargetDurationSeconds ?? project.TargetDurationSeconds,
            AvailableImages = article.Images.Select(img => new ArticleImageInputDto
            {
                Id = img.Id,
                OriginalImageUrl = img.OriginalImageUrl,
                Caption = img.Caption,
                AltText = img.AltText,
                Order = img.ArticleImageOrder,
                Category = img.Category.ToString(),
                RightsStatus = img.RightsStatus.ToString(),
                SelectedForVideo = img.Status != "Excluded"
            }).ToList()
        };

        var scriptResult = await editor.GenerateEditorialScriptAsync(promptInput, options?.ModelName, cancellationToken);

        // Save or update VideoScript
        var scriptEntity = project.Script;
        if (scriptEntity == null)
        {
            scriptEntity = new VideoScript
            {
                VideoProjectId = projectId
            };
            _context.VideoScripts.Add(scriptEntity);
            project.Script = scriptEntity;
        }

        scriptEntity.Title = scriptResult.VideoTitle;
        scriptEntity.Hook = scriptResult.Hook;
        scriptEntity.VoiceOverScript = scriptResult.VoiceOverScript;
        scriptEntity.KeyFacts = scriptResult.KeyFacts;
        scriptEntity.Context = scriptResult.Context;
        scriptEntity.Analysis = scriptResult.Analysis;
        scriptEntity.Conclusion = scriptResult.Conclusion;
        scriptEntity.SourceAttribution = scriptResult.SourceAttribution;
        scriptEntity.AiProvider = scriptResult.AiProvider;
        scriptEntity.ModelName = scriptResult.ModelName;
        scriptEntity.TokenUsage = scriptResult.TokenUsage;
        scriptEntity.EstimatedCostUsd = scriptResult.EstimatedCostUsd;
        scriptEntity.Status = "Completed";

        project.Title = scriptResult.VideoTitle;
        project.Status = VideoProjectStatus.Scripting;

        await _context.SaveChangesAsync(cancellationToken);

        // Also plan scenes right away
        await PlanScenesForProjectInternalAsync(project, scriptResult, promptInput.AvailableImages, cancellationToken);

        return MapScriptToDto(scriptEntity, scriptResult.WordCount, scriptResult.EstimatedReadingSeconds);
    }

    public async Task<VideoScriptResponseDto?> GetScriptByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var script = await _context.VideoScripts
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.VideoProjectId == projectId, cancellationToken);

        if (script == null) return null;

        var words = script.VoiceOverScript.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        var estSeconds = Math.Clamp((int)Math.Round(words / 2.3), 55, 65);

        return MapScriptToDto(script, words, estSeconds);
    }

    public async Task<VideoScriptResponseDto> UpdateScriptAsync(
        Guid projectId,
        UpdateScriptRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        var script = await _context.VideoScripts
            .FirstOrDefaultAsync(s => s.VideoProjectId == projectId, cancellationToken);

        if (script == null)
        {
            throw new KeyNotFoundException($"Script for VideoProject '{projectId}' not found.");
        }

        script.Title = dto.Title;
        script.Hook = dto.Hook;
        script.VoiceOverScript = dto.VoiceOverScript;
        script.KeyFacts = dto.KeyFacts;
        script.Context = dto.Context;
        script.Analysis = dto.Analysis;
        script.Conclusion = dto.Conclusion;
        script.SourceAttribution = dto.SourceAttribution;

        await _context.SaveChangesAsync(cancellationToken);

        var words = script.VoiceOverScript.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        var estSeconds = Math.Clamp((int)Math.Round(words / 2.3), 55, 65);

        return MapScriptToDto(script, words, estSeconds);
    }

    public async Task<List<VideoScenePlanDto>> PlanScenesForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _context.VideoProjects
            .Include(p => p.Script)
            .Include(p => p.Article)
                .ThenInclude(a => a!.Images)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null) throw new KeyNotFoundException($"Project '{projectId}' not found.");
        if (project.Script == null) throw new InvalidOperationException("Project does not have a script generated yet.");

        var images = project.Article?.Images.Select(img => new ArticleImageInputDto
        {
            Id = img.Id,
            OriginalImageUrl = img.OriginalImageUrl,
            Caption = img.Caption,
            AltText = img.AltText,
            Order = img.ArticleImageOrder,
            Category = img.Category.ToString(),
            RightsStatus = img.RightsStatus.ToString(),
            SelectedForVideo = img.Status != "Excluded"
        }).ToList() ?? new List<ArticleImageInputDto>();

        var scriptResult = new EditorialScriptResult
        {
            VideoTitle = project.Script.Title,
            Hook = project.Script.Hook,
            VoiceOverScript = project.Script.VoiceOverScript,
            KeyFacts = project.Script.KeyFacts,
            Context = project.Script.Context,
            Analysis = project.Script.Analysis,
            Conclusion = project.Script.Conclusion,
            SourceAttribution = project.Script.SourceAttribution
        };

        return await PlanScenesForProjectInternalAsync(project, scriptResult, images, cancellationToken);
    }

    public async Task<List<VideoScenePlanDto>> GetScenesByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var scenes = await _context.VideoScenes
            .AsNoTracking()
            .Include(s => s.SelectedArticleImage)
            .Where(s => s.VideoProjectId == projectId)
            .OrderBy(s => s.SceneNumber)
            .ToListAsync(cancellationToken);

        return scenes.Select(s => new VideoScenePlanDto
        {
            Id = s.Id,
            SceneNumber = s.SceneNumber,
            StartTimeSeconds = s.StartTimeSeconds,
            EndTimeSeconds = s.EndTimeSeconds,
            VoiceText = s.VoiceText,
            OnScreenText = s.OnScreenText,
            SelectedArticleImageId = s.SelectedArticleImageId,
            ImageUrl = s.SelectedArticleImage?.OriginalImageUrl,
            ImageCaption = s.ImageCaption ?? s.SelectedArticleImage?.Caption,
            VisualType = s.SelectedArticleImageId.HasValue ? "Image" : "Typography",
            MotionType = s.MotionType,
            TransitionType = s.TransitionType,
            ScenePurpose = s.ScenePurpose
        }).ToList();
    }

    public async Task<List<VideoScenePlanDto>> UpdateScenesAsync(
        Guid projectId,
        UpdateScenesRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.VideoScenes
            .Where(s => s.VideoProjectId == projectId)
            .ToListAsync(cancellationToken);

        _context.VideoScenes.RemoveRange(existing);

        int order = 1;
        foreach (var sc in dto.Scenes)
        {
            var entity = new VideoScene
            {
                VideoProjectId = projectId,
                SceneNumber = order++,
                StartTimeSeconds = sc.StartTimeSeconds,
                EndTimeSeconds = sc.EndTimeSeconds,
                VoiceText = sc.VoiceText,
                OnScreenText = sc.OnScreenText,
                SelectedArticleImageId = sc.SelectedArticleImageId,
                ImageCaption = sc.ImageCaption,
                MotionType = sc.MotionType,
                TransitionType = sc.TransitionType,
                ScenePurpose = sc.ScenePurpose,
                Status = "Planned"
            };
            _context.VideoScenes.Add(entity);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return await GetScenesByProjectIdAsync(projectId, cancellationToken);
    }

    private async Task<List<VideoScenePlanDto>> PlanScenesForProjectInternalAsync(
        VideoProject project,
        EditorialScriptResult scriptResult,
        List<ArticleImageInputDto> images,
        CancellationToken cancellationToken)
    {
        var planned = await _plannerService.PlanScenesAsync(new ScriptPlanningInput
        {
            ProjectId = project.Id,
            Script = scriptResult,
            AvailableImages = images,
            TargetDurationSeconds = project.TargetDurationSeconds
        }, cancellationToken);

        // Remove old scenes for this project
        var oldScenes = await _context.VideoScenes
            .Where(s => s.VideoProjectId == project.Id)
            .ToListAsync(cancellationToken);

        if (oldScenes.Count > 0)
        {
            _context.VideoScenes.RemoveRange(oldScenes);
        }

        foreach (var p in planned)
        {
            var sceneEntity = new VideoScene
            {
                VideoProjectId = project.Id,
                SceneNumber = p.SceneNumber,
                StartTimeSeconds = p.StartTimeSeconds,
                EndTimeSeconds = p.EndTimeSeconds,
                VoiceText = p.VoiceText,
                OnScreenText = p.OnScreenText,
                SelectedArticleImageId = p.SelectedArticleImageId,
                ImageCaption = p.ImageCaption,
                MotionType = p.MotionType,
                TransitionType = p.TransitionType,
                ScenePurpose = p.ScenePurpose,
                Status = "Planned"
            };
            _context.VideoScenes.Add(sceneEntity);
        }

        project.Status = VideoProjectStatus.PlanningScenes;
        await _context.SaveChangesAsync(cancellationToken);

        return planned;
    }

    private static VideoScriptResponseDto MapScriptToDto(VideoScript entity, int wordCount, int estSeconds)
    {
        return new VideoScriptResponseDto
        {
            Id = entity.Id,
            VideoProjectId = entity.VideoProjectId,
            Title = entity.Title,
            Hook = entity.Hook,
            VoiceOverScript = entity.VoiceOverScript,
            KeyFacts = entity.KeyFacts,
            Context = entity.Context,
            Analysis = entity.Analysis,
            Conclusion = entity.Conclusion,
            SourceAttribution = entity.SourceAttribution,
            AiProvider = entity.AiProvider,
            ModelName = entity.ModelName,
            TokenUsage = entity.TokenUsage,
            EstimatedCostUsd = entity.EstimatedCostUsd,
            Status = entity.Status,
            WordCount = wordCount,
            EstimatedReadingSeconds = estSeconds
        };
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Infrastructure.FilmFactory;

public class FilmFactoryService : IFilmFactoryService
{
    private readonly IApplicationDbContext _context;
    private readonly IFilmCostController _costController;
    private readonly ILogger<FilmFactoryService> _logger;

    public FilmFactoryService(
        IApplicationDbContext context,
        IFilmCostController costController,
        ILogger<FilmFactoryService> logger)
    {
        _context = context;
        _costController = costController;
        _logger = logger;
    }

    public async Task SeedDefaultFilmPagesAsync(CancellationToken ct = default)
    {
        var count = await _context.FilmPages.CountAsync(ct);
        if (count >= 5) return;

        _logger.LogInformation("Seeding 5 distinct AI Film Pages...");

        var defaults = new List<FilmPage>
        {
            new()
            {
                PageNumber = 1,
                Name = "CinemaX - Khoa Học Viễn Tưởng & Tương Lai",
                Description = "Kênh phim người thật AI thể loại viễn tưởng, AI, du hành thời gian và tương lai nhân loại.",
                PrimaryGenre = "SciFi",
                VisualArtStyle = "Cinematic Neo-Noir 35mm",
                BrandColorHex = "#3B82F6", // Blue
                DefaultVoice = "vi-VN-NamMinhNeural",
                Language = "vi-VN",
                TargetAudience = "Khán giả trẻ yêu thích công nghệ, bí ẩn và trí tuệ nhân tạo",
                AspectRatio = "9:16",
                Status = "Active",
                IsActive = true
            },
            new()
            {
                PageNumber = 2,
                Name = "DramaVerse - Kịch Tính & Xã Hội",
                Description = "Kênh phim tâm lý xã hội sâu sắc, đấu trí gia đình, thương trường và góc khuất cuộc đời.",
                PrimaryGenre = "Drama",
                VisualArtStyle = "Cinematic Ultra-Realistic",
                BrandColorHex = "#E11D48", // Rose
                DefaultVoice = "vi-VN-HoaiMyNeural",
                Language = "vi-VN",
                TargetAudience = "Khán giả trưởng thành, yêu thích phim kịch tính, cảm xúc dồn nén",
                AspectRatio = "9:16",
                Status = "Active",
                IsActive = true
            },
            new()
            {
                PageNumber = 3,
                Name = "Mythos - Huyền Huyễn & Thần Thoại",
                Description = "Thế giới cổ phong, truyền thuyết dân gian, siêu nhiên huyền bí và thần thoại Á Đông.",
                PrimaryGenre = "Fantasy",
                VisualArtStyle = "Ethereal Mythic Cinematic",
                BrandColorHex = "#8B5CF6", // Violet
                DefaultVoice = "vi-VN-NamMinhNeural",
                Language = "vi-VN",
                TargetAudience = "Khán giả say mê thế giới huyền ảo, kiếm hiệp và truyền thuyết",
                AspectRatio = "9:16",
                Status = "Active",
                IsActive = true
            },
            new()
            {
                PageNumber = 4,
                Name = "DarkTales - Trinh Thám & Giật Gân",
                Description = "Những vụ án hóc búa, tâm lý tội phạm, hồi hộp từng giây và những cú lừa nghẹt thở.",
                PrimaryGenre = "Thriller",
                VisualArtStyle = "Moody Dark Cinematic Contrast",
                BrandColorHex = "#F59E0B", // Amber
                DefaultVoice = "vi-VN-NamMinhNeural",
                Language = "vi-VN",
                TargetAudience = "Khán giả đam mê suy luận phá án, trinh thám hình sự",
                AspectRatio = "9:16",
                Status = "Active",
                IsActive = true
            },
            new()
            {
                PageNumber = 5,
                Name = "RomanceAI - Tình Cảm & Tuổi Trẻ",
                Description = "Những câu chuyện tình yêu chữa lành, rung động thanh xuân và lựa chọn số phận ngọt ngào.",
                PrimaryGenre = "Romance",
                VisualArtStyle = "Warm Golden Hour Cinematic",
                BrandColorHex = "#EC4899", // Pink
                DefaultVoice = "vi-VN-HoaiMyNeural",
                Language = "vi-VN",
                TargetAudience = "Khán giả trẻ tuổi mộng mơ, yêu thích chuyện tình lãng mạn",
                AspectRatio = "9:16",
                Status = "Active",
                IsActive = true
            }
        };

        // Gán FacebookAccount mặc định nếu có
        var existingFb = await _context.FacebookAccounts.FirstOrDefaultAsync(f => f.IsActive, ct);

        foreach (var def in defaults)
        {
            var exists = await _context.FilmPages.AnyAsync(p => p.PageNumber == def.PageNumber, ct);
            if (!exists)
            {
                if (existingFb != null)
                {
                    def.LinkedFacebookAccountId = existingFb.Id;
                }
                _context.FilmPages.Add(def);
            }
        }

        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded 5 AI Film Pages successfully.");
    }

    public async Task<List<FilmPageDto>> GetFilmPagesAsync(CancellationToken ct = default)
    {
        await SeedDefaultFilmPagesAsync(ct);

        var pages = await _context.FilmPages
            .Include(p => p.LinkedFacebookAccount)
            .Include(p => p.Projects)
                .ThenInclude(pr => pr.Episodes)
            .OrderBy(p => p.PageNumber)
            .ToListAsync(ct);

        return pages.Select(p => new FilmPageDto
        {
            Id = p.Id,
            PageNumber = p.PageNumber,
            Name = p.Name,
            Description = p.Description,
            AvatarUrl = p.AvatarUrl,
            CoverUrl = p.CoverUrl,
            PrimaryGenre = p.PrimaryGenre,
            VisualArtStyle = p.VisualArtStyle,
            BrandColorHex = p.BrandColorHex,
            DefaultVoice = p.DefaultVoice,
            Language = p.Language,
            TargetAudience = p.TargetAudience,
            AspectRatio = p.AspectRatio,
            LinkedFacebookAccountId = p.LinkedFacebookAccountId,
            LinkedFacebookPageName = p.LinkedFacebookAccount?.PageName,
            TotalProjectsCount = p.Projects.Count,
            TotalEpisodesCompleted = p.Projects.SelectMany(pr => pr.Episodes).Count(e => e.Status == "Completed"),
            TotalCostSpentUsd = Math.Round(p.Projects.Sum(pr => pr.TotalActualCostUsd), 4),
            IsActive = p.IsActive
        }).ToList();
    }

    public async Task<FilmPageDto?> GetFilmPageByIdAsync(Guid pageId, CancellationToken ct = default)
    {
        var p = await _context.FilmPages
            .Include(p => p.LinkedFacebookAccount)
            .Include(p => p.Projects)
                .ThenInclude(pr => pr.Episodes)
            .FirstOrDefaultAsync(x => x.Id == pageId, ct);

        if (p == null) return null;

        return new FilmPageDto
        {
            Id = p.Id,
            PageNumber = p.PageNumber,
            Name = p.Name,
            Description = p.Description,
            AvatarUrl = p.AvatarUrl,
            CoverUrl = p.CoverUrl,
            PrimaryGenre = p.PrimaryGenre,
            VisualArtStyle = p.VisualArtStyle,
            BrandColorHex = p.BrandColorHex,
            DefaultVoice = p.DefaultVoice,
            Language = p.Language,
            TargetAudience = p.TargetAudience,
            AspectRatio = p.AspectRatio,
            LinkedFacebookAccountId = p.LinkedFacebookAccountId,
            LinkedFacebookPageName = p.LinkedFacebookAccount?.PageName,
            TotalProjectsCount = p.Projects.Count,
            TotalEpisodesCompleted = p.Projects.SelectMany(pr => pr.Episodes).Count(e => e.Status == "Completed"),
            TotalCostSpentUsd = Math.Round(p.Projects.Sum(pr => pr.TotalActualCostUsd), 4),
            IsActive = p.IsActive
        };
    }

    public async Task<FilmPageDto> UpdateFilmPageAsync(Guid pageId, UpdateFilmPageRequestDto request, CancellationToken ct = default)
    {
        var page = await _context.FilmPages.FirstOrDefaultAsync(p => p.Id == pageId, ct)
            ?? throw new ArgumentException("Không tìm thấy Film Page với ID này.");

        page.Name = request.Name.Trim();
        page.Description = request.Description.Trim();
        page.AvatarUrl = request.AvatarUrl;
        page.CoverUrl = request.CoverUrl;
        page.PrimaryGenre = request.PrimaryGenre;
        page.VisualArtStyle = request.VisualArtStyle;
        page.BrandColorHex = request.BrandColorHex;
        page.DefaultVoice = request.DefaultVoice;
        page.Language = request.Language;
        page.TargetAudience = request.TargetAudience;
        page.AspectRatio = request.AspectRatio;
        page.LinkedFacebookAccountId = request.LinkedFacebookAccountId;
        page.IsActive = request.IsActive;
        page.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
        return (await GetFilmPageByIdAsync(pageId, ct))!;
    }

    public async Task<List<FilmProjectDto>> GetProjectsAsync(Guid? pageId = null, CancellationToken ct = default)
    {
        var query = _context.FilmProjects
            .Include(p => p.FilmPage)
            .Include(p => p.Episodes)
            .AsNoTracking();

        if (pageId.HasValue && pageId.Value != Guid.Empty)
        {
            query = query.Where(p => p.FilmPageId == pageId.Value);
        }

        var list = await query.OrderByDescending(p => p.CreatedAtUtc).ToListAsync(ct);

        return list.Select(p => new FilmProjectDto
        {
            Id = p.Id,
            FilmPageId = p.FilmPageId,
            FilmPageName = p.FilmPage?.Name ?? "Film Page",
            Title = p.Title,
            Logline = p.Logline,
            Genre = p.Genre,
            VisualArtStyle = p.VisualArtStyle,
            AspectRatio = p.AspectRatio,
            Status = p.Status,
            ErrorMessage = p.ErrorMessage,
            TotalBudgetLimitUsd = p.TotalBudgetLimitUsd,
            TotalActualCostUsd = Math.Round(p.TotalActualCostUsd, 4),
            TargetEpisodes = p.TargetEpisodes,
            CompletedEpisodesCount = p.Episodes.Count(e => e.Status == "Completed"),
            TargetDurationPerEpisodeSeconds = p.TargetDurationPerEpisodeSeconds,
            CreatedAtUtc = p.CreatedAtUtc,
            UpdatedAtUtc = p.UpdatedAtUtc
        }).ToList();
    }

    public async Task<FilmProjectDetailDto?> GetProjectByIdAsync(Guid projectId, CancellationToken ct = default)
    {
        var p = await _context.FilmProjects
            .Include(p => p.FilmPage)
            .Include(p => p.StoryBible)
            .Include(p => p.Characters)
            .Include(p => p.Locations)
            .Include(p => p.Episodes)
                .ThenInclude(e => e.Scenes)
            .FirstOrDefaultAsync(x => x.Id == projectId, ct);

        if (p == null) return null;

        var costSummary = await _costController.EvaluateBudgetStatusAsync(projectId, ct);

        return new FilmProjectDetailDto
        {
            Id = p.Id,
            FilmPageId = p.FilmPageId,
            FilmPageName = p.FilmPage?.Name ?? "Film Page",
            Title = p.Title,
            Logline = p.Logline,
            Genre = p.Genre,
            VisualArtStyle = p.VisualArtStyle,
            AspectRatio = p.AspectRatio,
            Status = p.Status,
            ErrorMessage = p.ErrorMessage,
            TotalBudgetLimitUsd = p.TotalBudgetLimitUsd,
            TotalActualCostUsd = Math.Round(p.TotalActualCostUsd, 4),
            TargetEpisodes = p.TargetEpisodes,
            CompletedEpisodesCount = p.Episodes.Count(e => e.Status == "Completed"),
            TargetDurationPerEpisodeSeconds = p.TargetDurationPerEpisodeSeconds,
            CreatedAtUtc = p.CreatedAtUtc,
            UpdatedAtUtc = p.UpdatedAtUtc,
            StoryBible = p.StoryBible == null ? null : new FilmStoryBibleDto
            {
                Id = p.StoryBible.Id,
                FilmProjectId = p.StoryBible.FilmProjectId,
                Logline = p.StoryBible.Logline,
                Genre = p.StoryBible.Genre,
                WorldRules = p.StoryBible.WorldRules,
                TimePeriod = p.StoryBible.TimePeriod,
                GeographicSetting = p.StoryBible.GeographicSetting,
                MainConflict = p.StoryBible.MainConflict,
                KeyMysteriesAndTwists = p.StoryBible.KeyMysteriesAndTwists,
                ClimaxResolution = p.StoryBible.ClimaxResolution,
                OverallArcOutline = p.StoryBible.OverallArcOutline,
                EpisodesSummaryJson = p.StoryBible.EpisodesSummaryJson
            },
            Characters = p.Characters.Select(c => new FilmCharacterDto
            {
                Id = c.Id,
                FilmProjectId = c.FilmProjectId,
                CharacterId = c.CharacterId,
                Name = c.Name,
                Role = c.Role,
                Age = c.Age,
                Gender = c.Gender,
                PhysicalAppearance = c.PhysicalAppearance,
                Hairstyle = c.Hairstyle,
                DefaultCostume = c.DefaultCostume,
                Personality = c.Personality,
                EmotionalTrait = c.EmotionalTrait,
                VoiceModel = c.VoiceModel,
                ConsistencyPrompt = c.ConsistencyPrompt,
                FrontReferenceImageUrl = c.FrontReferenceImageUrl,
                SideReferenceImageUrl = c.SideReferenceImageUrl,
                FullBodyReferenceImageUrl = c.FullBodyReferenceImageUrl
            }).ToList(),
            Locations = p.Locations.Select(l => new FilmLocationDto
            {
                Id = l.Id,
                FilmProjectId = l.FilmProjectId,
                LocationId = l.LocationId,
                Name = l.Name,
                ArchitectureStyle = l.ArchitectureStyle,
                LightingMood = l.LightingMood,
                TimeOfDay = l.TimeOfDay,
                KeyProps = l.KeyProps,
                ReferenceImageUrl = l.ReferenceImageUrl
            }).ToList(),
            Episodes = p.Episodes.OrderBy(e => e.EpisodeNumber).Select(e => new FilmEpisodeDto
            {
                Id = e.Id,
                FilmProjectId = e.FilmProjectId,
                EpisodeNumber = e.EpisodeNumber,
                Title = e.Title,
                Synopsis = e.Synopsis,
                OpeningHook = e.OpeningHook,
                EndingCliffhanger = e.EndingCliffhanger,
                TargetDurationSeconds = e.TargetDurationSeconds,
                ActualDurationSeconds = e.ActualDurationSeconds,
                Status = e.Status,
                OutputVideoPath = e.OutputVideoPath,
                ThumbnailPath = e.ThumbnailPath,
                EstimatedCostUsd = e.EstimatedCostUsd,
                ActualCostUsd = e.ActualCostUsd,
                QualityScore = e.QualityScore,
                ErrorMessage = e.ErrorMessage,
                ScenesCount = e.Scenes.Count,
                ShotsCount = 0
            }).ToList(),
            CostSummary = costSummary
        };
    }

    public async Task<FilmProjectDto> CreateProjectAsync(CreateFilmProjectRequestDto request, CancellationToken ct = default)
    {
        var page = await _context.FilmPages.FirstOrDefaultAsync(p => p.Id == request.FilmPageId, ct)
            ?? throw new ArgumentException("Film Page không tồn tại.");

        var title = string.IsNullOrWhiteSpace(request.Title)
            ? $"Series: {request.Logline.Split('\n')[0].Trim().Take(50)}..."
            : request.Title.Trim();

        var project = new FilmProject
        {
            FilmPageId = request.FilmPageId,
            Title = title,
            Logline = request.Logline.Trim(),
            Genre = request.Genre ?? page.PrimaryGenre,
            VisualArtStyle = request.VisualArtStyle ?? page.VisualArtStyle,
            AspectRatio = request.AspectRatio ?? page.AspectRatio,
            TotalBudgetLimitUsd = request.BudgetLimitUsd > 0 ? request.BudgetLimitUsd : 5.0m,
            TargetEpisodes = 10,
            TargetDurationPerEpisodeSeconds = 300,
            Status = "Draft"
        };

        _context.FilmProjects.Add(project);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Created new AI Film Project {Id}: {Title} on Page {Page}",
            project.Id, project.Title, page.Name);

        return new FilmProjectDto
        {
            Id = project.Id,
            FilmPageId = project.FilmPageId,
            FilmPageName = page.Name,
            Title = project.Title,
            Logline = project.Logline,
            Genre = project.Genre,
            VisualArtStyle = project.VisualArtStyle,
            AspectRatio = project.AspectRatio,
            Status = project.Status,
            TotalBudgetLimitUsd = project.TotalBudgetLimitUsd,
            TotalActualCostUsd = 0m,
            TargetEpisodes = 10,
            CompletedEpisodesCount = 0,
            TargetDurationPerEpisodeSeconds = 300,
            CreatedAtUtc = project.CreatedAtUtc
        };
    }

    public async Task<bool> DeleteProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await _context.FilmProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return false;

        _context.FilmProjects.Remove(project);
        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Deleted Film Project {Id}", projectId);
        return true;
    }

    public async Task<FilmStoryBibleDto?> GetStoryBibleAsync(Guid projectId, CancellationToken ct = default)
    {
        var b = await _context.FilmStoryBibles.AsNoTracking().FirstOrDefaultAsync(x => x.FilmProjectId == projectId, ct);
        if (b == null) return null;

        return new FilmStoryBibleDto
        {
            Id = b.Id,
            FilmProjectId = b.FilmProjectId,
            Logline = b.Logline,
            Genre = b.Genre,
            WorldRules = b.WorldRules,
            TimePeriod = b.TimePeriod,
            GeographicSetting = b.GeographicSetting,
            MainConflict = b.MainConflict,
            KeyMysteriesAndTwists = b.KeyMysteriesAndTwists,
            ClimaxResolution = b.ClimaxResolution,
            OverallArcOutline = b.OverallArcOutline,
            EpisodesSummaryJson = b.EpisodesSummaryJson
        };
    }

    public async Task<List<FilmCharacterDto>> GetCharactersAsync(Guid projectId, CancellationToken ct = default)
    {
        var list = await _context.FilmCharacters.AsNoTracking()
            .Where(c => c.FilmProjectId == projectId)
            .OrderBy(c => c.Role == "Protagonist" ? 0 : c.Role == "Antagonist" ? 1 : 2)
            .ToListAsync(ct);

        return list.Select(c => new FilmCharacterDto
        {
            Id = c.Id,
            FilmProjectId = c.FilmProjectId,
            CharacterId = c.CharacterId,
            Name = c.Name,
            Role = c.Role,
            Age = c.Age,
            Gender = c.Gender,
            PhysicalAppearance = c.PhysicalAppearance,
            Hairstyle = c.Hairstyle,
            DefaultCostume = c.DefaultCostume,
            Personality = c.Personality,
            EmotionalTrait = c.EmotionalTrait,
            VoiceModel = c.VoiceModel,
            ConsistencyPrompt = c.ConsistencyPrompt,
            FrontReferenceImageUrl = c.FrontReferenceImageUrl,
            SideReferenceImageUrl = c.SideReferenceImageUrl,
            FullBodyReferenceImageUrl = c.FullBodyReferenceImageUrl
        }).ToList();
    }

    public async Task<List<FilmLocationDto>> GetLocationsAsync(Guid projectId, CancellationToken ct = default)
    {
        var list = await _context.FilmLocations.AsNoTracking()
            .Where(l => l.FilmProjectId == projectId)
            .ToListAsync(ct);

        return list.Select(l => new FilmLocationDto
        {
            Id = l.Id,
            FilmProjectId = l.FilmProjectId,
            LocationId = l.LocationId,
            Name = l.Name,
            ArchitectureStyle = l.ArchitectureStyle,
            LightingMood = l.LightingMood,
            TimeOfDay = l.TimeOfDay,
            KeyProps = l.KeyProps,
            ReferenceImageUrl = l.ReferenceImageUrl
        }).ToList();
    }

    public async Task<List<FilmEpisodeDto>> GetEpisodesAsync(Guid projectId, CancellationToken ct = default)
    {
        var list = await _context.FilmEpisodes
            .Include(e => e.Scenes)
            .Include(e => e.Shots)
            .AsNoTracking()
            .Where(e => e.FilmProjectId == projectId)
            .OrderBy(e => e.EpisodeNumber)
            .ToListAsync(ct);

        return list.Select(e => new FilmEpisodeDto
        {
            Id = e.Id,
            FilmProjectId = e.FilmProjectId,
            EpisodeNumber = e.EpisodeNumber,
            Title = e.Title,
            Synopsis = e.Synopsis,
            OpeningHook = e.OpeningHook,
            EndingCliffhanger = e.EndingCliffhanger,
            TargetDurationSeconds = e.TargetDurationSeconds,
            ActualDurationSeconds = e.ActualDurationSeconds,
            Status = e.Status,
            OutputVideoPath = e.OutputVideoPath,
            ThumbnailPath = e.ThumbnailPath,
            EstimatedCostUsd = e.EstimatedCostUsd,
            ActualCostUsd = e.ActualCostUsd,
            QualityScore = e.QualityScore,
            ErrorMessage = e.ErrorMessage,
            ScenesCount = e.Scenes.Count,
            ShotsCount = e.Shots.Count
        }).ToList();
    }

    public async Task<FilmEpisodeDetailDto?> GetEpisodeDetailAsync(Guid episodeId, CancellationToken ct = default)
    {
        var e = await _context.FilmEpisodes
            .Include(e => e.Scenes)
                .ThenInclude(s => s.Shots)
            .FirstOrDefaultAsync(x => x.Id == episodeId, ct);

        if (e == null) return null;

        return new FilmEpisodeDetailDto
        {
            Id = e.Id,
            FilmProjectId = e.FilmProjectId,
            EpisodeNumber = e.EpisodeNumber,
            Title = e.Title,
            Synopsis = e.Synopsis,
            OpeningHook = e.OpeningHook,
            EndingCliffhanger = e.EndingCliffhanger,
            TargetDurationSeconds = e.TargetDurationSeconds,
            ActualDurationSeconds = e.ActualDurationSeconds,
            Status = e.Status,
            OutputVideoPath = e.OutputVideoPath,
            ThumbnailPath = e.ThumbnailPath,
            EstimatedCostUsd = e.EstimatedCostUsd,
            ActualCostUsd = e.ActualCostUsd,
            QualityScore = e.QualityScore,
            ErrorMessage = e.ErrorMessage,
            ScenesCount = e.Scenes.Count,
            ShotsCount = e.Scenes.SelectMany(s => s.Shots).Count(),
            Scenes = e.Scenes.OrderBy(s => s.SceneNumber).Select(s => new FilmSceneDto
            {
                Id = s.Id,
                FilmEpisodeId = s.FilmEpisodeId,
                SceneNumber = s.SceneNumber,
                LocationId = s.LocationId,
                LocationName = s.LocationName,
                ScenePurpose = s.ScenePurpose,
                TimeOfDay = s.TimeOfDay,
                NarrativeSummary = s.NarrativeSummary,
                EstimatedDurationSeconds = s.EstimatedDurationSeconds,
                Shots = s.Shots.OrderBy(sh => sh.ShotNumber).Select(sh => new FilmShotDto
                {
                    Id = sh.Id,
                    FilmSceneId = sh.FilmSceneId,
                    FilmEpisodeId = sh.FilmEpisodeId,
                    ShotNumber = sh.ShotNumber,
                    ShotType = sh.ShotType,
                    CameraMovement = sh.CameraMovement,
                    VisualMethod = sh.VisualMethod,
                    VisualPrompt = sh.VisualPrompt,
                    SpeakerCharacterId = sh.SpeakerCharacterId,
                    DialogueText = sh.DialogueText,
                    ActingInstructions = sh.ActingInstructions,
                    EmotionalTone = sh.EmotionalTone,
                    DurationSeconds = sh.DurationSeconds,
                    Provider = sh.Provider,
                    ActualCostUsd = sh.ActualCostUsd,
                    RenderStatus = sh.RenderStatus,
                    RenderedShotVideoPath = sh.RenderedShotVideoPath,
                    RawImagePath = sh.RawImagePath
                }).ToList()
            }).ToList()
        };
    }

    public async Task<FilmCostSummaryDto> GetCostSummaryAsync(Guid projectId, CancellationToken ct = default)
    {
        return await _costController.EvaluateBudgetStatusAsync(projectId, ct);
    }
}

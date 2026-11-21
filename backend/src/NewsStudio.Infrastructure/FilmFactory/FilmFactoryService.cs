using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Entities;
using NewsStudio.Infrastructure.Common;

namespace NewsStudio.Infrastructure.FilmFactory;

public class FilmFactoryService : IFilmFactoryService
{
    private readonly IApplicationDbContext _context;
    private readonly IFilmCostController _costController;
    private readonly FilmGeminiClient _gemini;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FilmFactoryService> _logger;

    private string Root => Path.Combine(TempStorage.GetRoot(_configuration), "film_factory");

    public FilmFactoryService(
        IApplicationDbContext context,
        IFilmCostController costController,
        FilmGeminiClient gemini,
        IConfiguration configuration,
        ILogger<FilmFactoryService> logger)
    {
        _context = context;
        _costController = costController;
        _gemini = gemini;
        _configuration = configuration;
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

        var existingPages = await _context.FilmPages.ToListAsync(ct);
        var existingNumbers = existingPages.Select(p => p.PageNumber).ToHashSet();

        foreach (var def in defaults)
        {
            if (!existingNumbers.Contains(def.PageNumber))
            {
                def.LinkedFacebookAccountId = null;
                _context.FilmPages.Add(def);
                existingNumbers.Add(def.PageNumber);
            }
        }

        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded 5 AI Film Pages successfully.");
    }

    public async Task<List<FilmPageDto>> GetFilmPagesAsync(CancellationToken ct = default)
    {
        // 1. Đồng bộ các FacebookAccount thực tế đang kết nối trong hệ thống thành FilmPage
        var activeFbAccounts = await _context.FacebookAccounts
            .Where(f => f.IsActive)
            .OrderBy(f => f.CreatedAtUtc)
            .ToListAsync(ct);

        var existingPages = await _context.FilmPages.ToListAsync(ct);
        var existingNumbers = existingPages.Select(p => p.PageNumber).ToHashSet();
        int currentMaxNumber = existingPages.Count > 0 ? existingPages.Max(p => p.PageNumber) : 0;
        bool hasChanges = false;

        // Dọn dẹp liên kết trùng lặp trong lịch sử: Mỗi tài khoản Facebook chỉ gắn duy nhất 1 Kênh Phim
        var seenAccounts = new HashSet<Guid>();
        foreach (var page in existingPages.Where(p => p.LinkedFacebookAccountId != null))
        {
            if (!seenAccounts.Add(page.LinkedFacebookAccountId!.Value))
            {
                // Gỡ liên kết thừa để kênh quay lại làm kênh độc lập, không hiển thị trùng tên Facebook Page
                page.LinkedFacebookAccountId = null;
                hasChanges = true;
            }
        }

        foreach (var fb in activeFbAccounts)
        {
            var existing = existingPages.FirstOrDefault(p => p.LinkedFacebookAccountId == fb.Id);
            if (existing == null)
            {
                currentMaxNumber++;
                while (existingNumbers.Contains(currentMaxNumber))
                {
                    currentMaxNumber++;
                }
                existingNumbers.Add(currentMaxNumber);

                var newPage = new FilmPage
                {
                    PageNumber = currentMaxNumber,
                    Name = fb.PageName,
                    Description = $"Kênh phim chính thức kết nối với Facebook Page {fb.PageName} (ID: {fb.PageId})",
                    PrimaryGenre = "Drama",
                    VisualArtStyle = "Cinematic 35mm Realism",
                    BrandColorHex = "#4F46E5",
                    DefaultVoice = "vi-VN-NamMinhNeural",
                    Language = "vi-VN",
                    TargetAudience = "Khán giả đại chúng yêu thích phim ngắn điện ảnh",
                    AspectRatio = "9:16",
                    LinkedFacebookAccountId = fb.Id,
                    Status = fb.Status,
                    IsActive = fb.IsActive
                };
                _context.FilmPages.Add(newPage);
                existingPages.Add(newPage);
                hasChanges = true;
            }
            else
            {
                if (existing.Name != fb.PageName || existing.Status != fb.Status || existing.IsActive != fb.IsActive)
                {
                    existing.Name = fb.PageName;
                    existing.Status = fb.Status;
                    existing.IsActive = fb.IsActive;
                    hasChanges = true;
                }
            }
        }

        if (hasChanges)
        {
            await _context.SaveChangesAsync(ct);
        }

        // Nếu chưa từng có Facebook Page nào được kết nối, seed các kênh mặc định
        var hasRealPages = existingPages.Any(p => p.LinkedFacebookAccountId != null);
        if (!hasRealPages && existingPages.Count == 0)
        {
            await SeedDefaultFilmPagesAsync(ct);
        }

        var query = _context.FilmPages
            .Include(p => p.LinkedFacebookAccount)
            .Include(p => p.Projects)
                .ThenInclude(pr => pr.Episodes)
            .AsQueryable();

        // Ưu tiên hiển thị các Page thực tế đã liên kết Facebook
        if (hasRealPages)
        {
            query = query.Where(p => p.LinkedFacebookAccountId != null);
        }

        var pages = await query.OrderBy(p => p.PageNumber).ToListAsync(ct);

        // Khử trùng lặp chắc chắn theo Facebook Account ID
        var distinctPages = new List<FilmPage>();
        var distinctFbSeen = new HashSet<Guid>();
        foreach (var p in pages)
        {
            if (p.LinkedFacebookAccountId.HasValue)
            {
                if (distinctFbSeen.Add(p.LinkedFacebookAccountId.Value))
                {
                    distinctPages.Add(p);
                }
            }
            else
            {
                distinctPages.Add(p);
            }
        }

        return distinctPages.Select(p => new FilmPageDto
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
            LinkedFacebookPageId = p.LinkedFacebookAccount?.PageId,
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

        var targetEpisodes = Math.Clamp(request.TargetEpisodes > 0 ? request.TargetEpisodes : 5, 1, 20);
        var durationSeconds = (int)Math.Round((request.TargetDurationMinutes > 0 ? request.TargetDurationMinutes : 2.5) * 60);

        var project = new FilmProject
        {
            FilmPageId = request.FilmPageId,
            Title = title,
            Logline = request.Logline.Trim(),
            Genre = request.Genre ?? page.PrimaryGenre,
            VisualArtStyle = request.VisualArtStyle ?? page.VisualArtStyle,
            AspectRatio = request.AspectRatio ?? page.AspectRatio,
            TotalBudgetLimitUsd = request.BudgetLimitUsd > 0 ? request.BudgetLimitUsd : 5.0m,
            TargetEpisodes = targetEpisodes,
            TargetDurationPerEpisodeSeconds = durationSeconds,
            Status = "Draft"
        };

        _context.FilmProjects.Add(project);

        // Khởi tạo trước các tập tương ứng với targetEpisodes và durationSeconds
        for (int i = 1; i <= targetEpisodes; i++)
        {
            _context.FilmEpisodes.Add(new FilmEpisode
            {
                FilmProjectId = project.Id,
                EpisodeNumber = i,
                Title = $"Tập {i}",
                Synopsis = $"Tập {i} của series {title}",
                OpeningHook = "Mở đầu kịch tính...",
                EndingCliffhanger = "Nút thắt bất ngờ...",
                TargetDurationSeconds = durationSeconds,
                Status = "Pending"
            });
        }

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Created new AI Film Project {Id}: {Title} on Page {Page} ({Episodes} eps x {Duration}s)",
            project.Id, project.Title, page.Name, targetEpisodes, durationSeconds);

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
            TargetEpisodes = targetEpisodes,
            CompletedEpisodesCount = 0,
            TargetDurationPerEpisodeSeconds = durationSeconds,
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

    public async Task<FilmCharacterDto?> GetCharacterByIdAsync(Guid characterId, CancellationToken ct = default)
    {
        var c = await _context.FilmCharacters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == characterId, ct);
        if (c == null) return null;

        return new FilmCharacterDto
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
        };
    }

    public async Task<FilmCharacterDto> UpdateCharacterAsync(Guid characterId, UpdateFilmCharacterRequestDto request, CancellationToken ct = default)
    {
        var c = await _context.FilmCharacters.FirstOrDefaultAsync(x => x.Id == characterId, ct)
            ?? throw new ArgumentException("Không tìm thấy nhân vật.");

        if (!string.IsNullOrWhiteSpace(request.Name)) c.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.Role)) c.Role = request.Role.Trim();
        if (request.Age.HasValue && request.Age.Value > 0) c.Age = request.Age.Value;
        if (!string.IsNullOrWhiteSpace(request.Gender)) c.Gender = request.Gender.Trim();
        if (request.PhysicalAppearance != null) c.PhysicalAppearance = request.PhysicalAppearance.Trim();
        if (request.Hairstyle != null) c.Hairstyle = request.Hairstyle.Trim();
        if (request.DefaultCostume != null) c.DefaultCostume = request.DefaultCostume.Trim();
        if (request.Personality != null) c.Personality = request.Personality.Trim();
        if (request.EmotionalTrait != null) c.EmotionalTrait = request.EmotionalTrait.Trim();
        if (!string.IsNullOrWhiteSpace(request.VoiceModel)) c.VoiceModel = request.VoiceModel.Trim();
        if (!string.IsNullOrWhiteSpace(request.ConsistencyPrompt)) c.ConsistencyPrompt = request.ConsistencyPrompt.Trim();

        c.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Updated character {Name} ({Id})", c.Name, c.Id);
        return (await GetCharacterByIdAsync(characterId, ct))!;
    }

    public async Task<FilmCharacterDto> SetCharacterPortraitPathAsync(Guid characterId, string imagePath, CancellationToken ct = default)
    {
        var c = await _context.FilmCharacters.FirstOrDefaultAsync(x => x.Id == characterId, ct)
            ?? throw new ArgumentException("Không tìm thấy nhân vật.");

        c.FrontReferenceImageUrl = imagePath;
        c.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Saved reference portrait for character {Name} ({Id}) -> {Path}", c.Name, c.Id, imagePath);
        return (await GetCharacterByIdAsync(characterId, ct))!;
    }

    public async Task<FilmCharacterDto> GenerateCharacterPortraitAsync(Guid characterId, CancellationToken ct = default)
    {
        var c = await _context.FilmCharacters
            .Include(x => x.FilmProject)
            .FirstOrDefaultAsync(x => x.Id == characterId, ct)
            ?? throw new ArgumentException("Không tìm thấy nhân vật.");

        var project = c.FilmProject ?? throw new InvalidOperationException("Nhân vật không thuộc dự án phim nào.");

        await _costController.CanExecutePaidTaskAsync(project.Id, _gemini.EstimatedImageCostUsd, ct);

        var prompt = $"Ultra-detailed cinematic character reference portrait, waist-up framing, facing front looking directly at camera, neutral balanced facial expression, professional studio dark grey background, soft natural studio lighting. " +
                     $"{c.ConsistencyPrompt}. Style: {project.VisualArtStyle}, photorealistic 8k, sharp focus, perfect anatomy, highly consistent facial features, no text, no watermark, no artifacts.";

        _logger.LogInformation("Generating AI portrait anchor for character {Name} ({Id}) with prompt: {Prompt}", c.Name, c.Id, prompt);

        var result = await _gemini.GenerateImageAsync(prompt, "3:4", Array.Empty<FilmGeminiClient.ReferenceImage>(), ct);

        var dir = Directory.CreateDirectory(Path.Combine(Root, "characters")).FullName;
        var path = Path.Combine(dir, $"{c.Id}_{DateTime.UtcNow.Ticks}.jpg");

        await File.WriteAllBytesAsync(path, result.Bytes, ct);

        await _costController.RecordCostAsync(project.Id, null, null, "Gemini", "ImageGen", result.Tokens, result.CostUsd, result.FreeKey,
            $"Tạo ảnh mẫu nhân vật {c.Name}", ct);

        c.FrontReferenceImageUrl = path;
        c.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("AI generated portrait anchor for {Name} saved at {Path}", c.Name, path);
        return (await GetCharacterByIdAsync(characterId, ct))!;
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

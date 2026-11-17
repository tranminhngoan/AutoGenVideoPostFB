using System;
using System.Collections.Generic;

namespace NewsStudio.Application.DTOs;

public class FilmPageDto
{
    public Guid Id { get; set; }
    public int PageNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string PrimaryGenre { get; set; } = string.Empty;
    public string VisualArtStyle { get; set; } = string.Empty;
    public string BrandColorHex { get; set; } = string.Empty;
    public string DefaultVoice { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string TargetAudience { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = string.Empty;
    public Guid? LinkedFacebookAccountId { get; set; }
    public string? LinkedFacebookPageName { get; set; }
    public int TotalProjectsCount { get; set; }
    public int TotalEpisodesCompleted { get; set; }
    public decimal TotalCostSpentUsd { get; set; }
    public bool IsActive { get; set; }
}

public class UpdateFilmPageRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string PrimaryGenre { get; set; } = "Drama";
    public string VisualArtStyle { get; set; } = "Cinematic 35mm";
    public string BrandColorHex { get; set; } = "#6366F1";
    public string DefaultVoice { get; set; } = "vi-VN-NamMinhNeural";
    public string Language { get; set; } = "vi-VN";
    public string TargetAudience { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = "9:16";
    public Guid? LinkedFacebookAccountId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CreateFilmProjectRequestDto
{
    public Guid FilmPageId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Logline { get; set; } = string.Empty; // Ý tưởng phim người dùng nhập
    public string? Genre { get; set; }
    public string? VisualArtStyle { get; set; }
    public string? AspectRatio { get; set; }
    public decimal BudgetLimitUsd { get; set; } = 5.0m;
    public bool AutoProduceAll { get; set; } = true; // Tự động chạy toàn bộ quy trình ngay sau khi tạo
}

public class FilmProjectDto
{
    public Guid Id { get; set; }
    public Guid FilmPageId { get; set; }
    public string FilmPageName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Logline { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string VisualArtStyle { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = "9:16";
    public string Status { get; set; } = "Draft";
    public string? ErrorMessage { get; set; }
    public decimal TotalBudgetLimitUsd { get; set; }
    public decimal TotalActualCostUsd { get; set; }
    public int TargetEpisodes { get; set; } = 10;
    public int CompletedEpisodesCount { get; set; }
    public int TargetDurationPerEpisodeSeconds { get; set; } = 300;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public class FilmProjectDetailDto : FilmProjectDto
{
    public FilmStoryBibleDto? StoryBible { get; set; }
    public List<FilmCharacterDto> Characters { get; set; } = new();
    public List<FilmLocationDto> Locations { get; set; } = new();
    public List<FilmEpisodeDto> Episodes { get; set; } = new();
    public FilmCostSummaryDto CostSummary { get; set; } = new();
}

public class FilmStoryBibleDto
{
    public Guid Id { get; set; }
    public Guid FilmProjectId { get; set; }
    public string Logline { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string WorldRules { get; set; } = string.Empty;
    public string TimePeriod { get; set; } = string.Empty;
    public string GeographicSetting { get; set; } = string.Empty;
    public string MainConflict { get; set; } = string.Empty;
    public string KeyMysteriesAndTwists { get; set; } = string.Empty;
    public string ClimaxResolution { get; set; } = string.Empty;
    public string OverallArcOutline { get; set; } = string.Empty;
    public string EpisodesSummaryJson { get; set; } = string.Empty;
}

public class FilmCharacterDto
{
    public Guid Id { get; set; }
    public Guid FilmProjectId { get; set; }
    public string CharacterId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string PhysicalAppearance { get; set; } = string.Empty;
    public string Hairstyle { get; set; } = string.Empty;
    public string DefaultCostume { get; set; } = string.Empty;
    public string Personality { get; set; } = string.Empty;
    public string EmotionalTrait { get; set; } = string.Empty;
    public string VoiceModel { get; set; } = string.Empty;
    public string ConsistencyPrompt { get; set; } = string.Empty;
    public string? FrontReferenceImageUrl { get; set; }
    public string? SideReferenceImageUrl { get; set; }
    public string? FullBodyReferenceImageUrl { get; set; }
}

public class FilmLocationDto
{
    public Guid Id { get; set; }
    public Guid FilmProjectId { get; set; }
    public string LocationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ArchitectureStyle { get; set; } = string.Empty;
    public string LightingMood { get; set; } = string.Empty;
    public string TimeOfDay { get; set; } = string.Empty;
    public string KeyProps { get; set; } = string.Empty;
    public string? ReferenceImageUrl { get; set; }
}

public class FilmEpisodeDto
{
    public Guid Id { get; set; }
    public Guid FilmProjectId { get; set; }
    public int EpisodeNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Synopsis { get; set; } = string.Empty;
    public string OpeningHook { get; set; } = string.Empty;
    public string EndingCliffhanger { get; set; } = string.Empty;
    public int TargetDurationSeconds { get; set; } = 300;
    public double ActualDurationSeconds { get; set; }
    public string Status { get; set; } = "Pending";
    public string? OutputVideoPath { get; set; }
    public string? ThumbnailPath { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public decimal ActualCostUsd { get; set; }
    public double QualityScore { get; set; }
    public string? ErrorMessage { get; set; }
    public int ScenesCount { get; set; }
    public int ShotsCount { get; set; }
}

public class FilmEpisodeDetailDto : FilmEpisodeDto
{
    public List<FilmSceneDto> Scenes { get; set; } = new();
}

public class FilmSceneDto
{
    public Guid Id { get; set; }
    public Guid FilmEpisodeId { get; set; }
    public int SceneNumber { get; set; }
    public string LocationId { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public string ScenePurpose { get; set; } = string.Empty;
    public string TimeOfDay { get; set; } = string.Empty;
    public string NarrativeSummary { get; set; } = string.Empty;
    public double EstimatedDurationSeconds { get; set; }
    public List<FilmShotDto> Shots { get; set; } = new();
}

public class FilmShotDto
{
    public Guid Id { get; set; }
    public Guid FilmSceneId { get; set; }
    public Guid FilmEpisodeId { get; set; }
    public int ShotNumber { get; set; }
    public string ShotType { get; set; } = string.Empty;
    public string CameraMovement { get; set; } = string.Empty;
    public string VisualMethod { get; set; } = string.Empty;
    public string VisualPrompt { get; set; } = string.Empty;
    public string? SpeakerCharacterId { get; set; }
    public string? DialogueText { get; set; }
    public string? ActingInstructions { get; set; }
    public string? EmotionalTone { get; set; }
    public double DurationSeconds { get; set; }
    public string Provider { get; set; } = string.Empty;
    public decimal ActualCostUsd { get; set; }
    public string RenderStatus { get; set; } = "Pending";
    public string? RenderedShotVideoPath { get; set; }
    public string? RawImagePath { get; set; }
}

public class FilmCostSummaryDto
{
    public decimal TotalBudgetLimitUsd { get; set; } = 5.0m;
    public decimal TotalSpentUsd { get; set; } = 0.0m;
    public decimal RemainingBudgetUsd { get; set; } = 5.0m;
    public string BudgetStatus { get; set; } = "FREE_ONLY"; // FREE_ONLY, LOW_COST, BUDGET_WARNING, BUDGET_EXHAUSTED
    public decimal ScriptCostUsd { get; set; } = 0.0m;
    public decimal VisualCostUsd { get; set; } = 0.0m;
    public decimal VoiceCostUsd { get; set; } = 0.0m;
    public decimal RenderCostUsd { get; set; } = 0.0m;
    public int TotalAiVideoShots { get; set; }
    public int TotalMotionImageShots { get; set; }
    public int TotalFreeApiCalls { get; set; }
    public int TotalPaidApiCalls { get; set; }
}

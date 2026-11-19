using System;
using System.Collections.Generic;
using NewsStudio.Domain.Common;

namespace NewsStudio.Domain.Entities;

/// <summary>
/// Quản lý 5 Film Page độc lập trong hệ thống AI Film Factory.
/// Mỗi Page có định vị thể loại, phong cách hình ảnh, giọng đọc và khán giả riêng biệt.
/// </summary>
public class FilmPage : BaseEntity
{
    public int PageNumber { get; set; } // 1 to 5
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string PrimaryGenre { get; set; } = "Drama"; // SciFi, Drama, Fantasy, Thriller, Romance, Action, Comedy
    public string VisualArtStyle { get; set; } = "Cinematic 35mm"; // Cinematic 35mm, Neo-Noir, Hyper-Realistic, etc.
    public string BrandColorHex { get; set; } = "#6366F1";
    public string DefaultVoice { get; set; } = "vi-VN-NamMinhNeural";
    public string Language { get; set; } = "vi-VN";
    public string TargetAudience { get; set; } = "Khán giả đại chúng yêu thích phim ngắn";
    public string AspectRatio { get; set; } = "9:16"; // 9:16 (Reels/TikTok) hoặc 16:9 (Cinema/YouTube)
    public string? PublishingConfigJson { get; set; }
    
    /// <summary>Liên kết với Facebook/Instagram Page hiện có trong hệ thống</summary>
    public Guid? LinkedFacebookAccountId { get; set; }
    public FacebookAccount? LinkedFacebookAccount { get; set; }

    public bool IsActive { get; set; } = true;
    public string Status { get; set; } = "Active";

    // Navigations
    public ICollection<FilmProject> Projects { get; set; } = new List<FilmProject>();
}

/// <summary>
/// Đại diện cho một dự án phim nhiều tập (Series 10 tập × 5 phút = 50 phút).
/// </summary>
public class FilmProject : BaseEntity
{
    public Guid FilmPageId { get; set; }
    public FilmPage? FilmPage { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Logline { get; set; } = string.Empty; // Ý tưởng cốt lõi do người dùng nhập
    public string Genre { get; set; } = "Drama";
    public string VisualArtStyle { get; set; } = "Cinematic 35mm";
    public string AspectRatio { get; set; } = "9:16";

    /// <summary>Draft, ShowrunnerPlanning, BibleReady, InProduction, Completed, Failed</summary>
    public string Status { get; set; } = "Draft";
    public string? ErrorMessage { get; set; }

    // Ngân sách và giới hạn chi phí cứng (Mặc định tối đa 5.0 USD cho cả 10 tập)
    public decimal TotalBudgetLimitUsd { get; set; } = 5.0m;
    public decimal TotalActualCostUsd { get; set; } = 0.0m;

    public int TargetEpisodes { get; set; } = 10;
    public int TargetDurationPerEpisodeSeconds { get; set; } = 300; // 5 phút / tập

    // Tỷ lệ phân bổ sản xuất hình ảnh kết hợp
    public int VisualRatioAiVideo { get; set; } = 15; // 10-15% video AI
    public int VisualRatioMotionImage { get; set; } = 70; // 70-75% ảnh điện ảnh chuyển động
    public int VisualRatioMotionGraphics { get; set; } = 15; // 10-15% đồ họa/chuyển cảnh

    // Navigations
    public FilmStoryBible? StoryBible { get; set; }
    public ICollection<FilmCharacter> Characters { get; set; } = new List<FilmCharacter>();
    public ICollection<FilmLocation> Locations { get; set; } = new List<FilmLocation>();
    public ICollection<FilmEpisode> Episodes { get; set; } = new List<FilmEpisode>();
    public ICollection<FilmCostRecord> CostRecords { get; set; } = new List<FilmCostRecord>();
    public ICollection<FilmContinuityRecord> ContinuityRecords { get; set; } = new List<FilmContinuityRecord>();
}

/// <summary>
/// Bộ nhớ dài hạn của bộ phim (Series Bible), lưu trữ quy tắc thế giới, bí mật, dòng thời gian.
/// </summary>
public class FilmStoryBible : BaseEntity
{
    public Guid FilmProjectId { get; set; }
    public FilmProject? FilmProject { get; set; }

    public string Logline { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string WorldRules { get; set; } = string.Empty; // Quy tắc thế giới phim
    public string TimePeriod { get; set; } = string.Empty; // Bối cảnh thời gian (hiện đại, cổ trang, tương lai)
    public string GeographicSetting { get; set; } = string.Empty; // Địa lý, thành phố, không gian
    public string MainConflict { get; set; } = string.Empty; // Xung đột trung tâm
    public string KeyMysteriesAndTwists { get; set; } = string.Empty; // Bí mật, nút thắt và cú lừa (plot twists)
    public string ClimaxResolution { get; set; } = string.Empty; // Cao trào và cách giải quyết ở tập 10
    public string OverallArcOutline { get; set; } = string.Empty; // Dàn ý 3 hồi xuyên suốt 10 tập
    public string EpisodesSummaryJson { get; set; } = string.Empty; // JSON tóm tắt 10 tập (tập sau nối tập trước)
}

/// <summary>
/// Hồ sơ nhân vật (Character Bible), đảm bảo giữ nguyên khuôn mặt và tính cách xuyên suốt 10 tập.
/// </summary>
public class FilmCharacter : BaseEntity
{
    public Guid FilmProjectId { get; set; }
    public FilmProject? FilmProject { get; set; }

    public string CharacterId { get; set; } = string.Empty; // char_hero, char_villain, char_mentor
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = "Protagonist"; // Protagonist, Antagonist, Supporting, Mentor
    public int Age { get; set; } = 25;
    public string Gender { get; set; } = "Nam";
    public string PhysicalAppearance { get; set; } = string.Empty; // Khuôn mặt, mắt, mũi, dáng người
    public string Hairstyle { get; set; } = string.Empty;
    public string DefaultCostume { get; set; } = string.Empty; // Trang phục nhận diện
    public string Personality { get; set; } = string.Empty; // Tính cách
    public string EmotionalTrait { get; set; } = string.Empty; // Nét cảm xúc đặc trưng
    public string VoiceModel { get; set; } = "vi-VN-NamMinhNeural"; // Giọng EdgeTTS riêng biệt
    public string ConsistencyPrompt { get; set; } = string.Empty; // Prompt khuôn mặt chuẩn để giữ nhận diện

    public string? FrontReferenceImageUrl { get; set; }
    public string? SideReferenceImageUrl { get; set; }
    public string? FullBodyReferenceImageUrl { get; set; }

    public ICollection<FilmCharacterReference> References { get; set; } = new List<FilmCharacterReference>();
}

/// <summary>
/// Ảnh tham chiếu góc chụp khác nhau của nhân vật.
/// </summary>
public class FilmCharacterReference : BaseEntity
{
    public Guid FilmCharacterId { get; set; }
    public FilmCharacter? FilmCharacter { get; set; }

    public string Angle { get; set; } = "Front"; // Front, Side, ThreeQuarter, FullBody, Expression
    public string ImageUrl { get; set; } = string.Empty;
    public string? LocalFilePath { get; set; }
}

/// <summary>
/// Hồ sơ bối cảnh (Location Bible) để bối cảnh nhất quán giữa các cảnh quay.
/// </summary>
public class FilmLocation : BaseEntity
{
    public Guid FilmProjectId { get; set; }
    public FilmProject? FilmProject { get; set; }

    public string LocationId { get; set; } = string.Empty; // loc_nha_anh_khoa, loc_hem_vang
    public string Name { get; set; } = string.Empty;
    public string ArchitectureStyle { get; set; } = string.Empty;
    public string LightingMood { get; set; } = string.Empty; // Ánh sáng, gam màu
    public string TimeOfDay { get; set; } = "Night"; // Day, Night, Sunset, Dawn
    public string KeyProps { get; set; } = string.Empty; // Đồ vật đặc trưng trong phòng
    public string? ReferenceImageUrl { get; set; }
}

/// <summary>
/// Nhật ký giữ tính liên tục (Continuity Bible) để Continuity Checker phát hiện mâu thuẫn.
/// </summary>
public class FilmContinuityRecord : BaseEntity
{
    public Guid FilmProjectId { get; set; }
    public FilmProject? FilmProject { get; set; }

    public int EpisodeNumber { get; set; }
    public int SceneNumber { get; set; }
    public string KeyDetailsJson { get; set; } = string.Empty; // Trang phục, vết thương, đồ vật đang cầm
    public string? PotentialConflicts { get; set; }
    public bool IsResolved { get; set; } = true;
}

/// <summary>
/// Một tập phim trong series 10 tập (Mục tiêu 5 phút = 300s).
/// </summary>
public class FilmEpisode : BaseEntity
{
    public Guid FilmProjectId { get; set; }
    public FilmProject? FilmProject { get; set; }

    public int EpisodeNumber { get; set; } // 1 to 10
    public string Title { get; set; } = string.Empty;
    public string Synopsis { get; set; } = string.Empty;
    public string OpeningHook { get; set; } = string.Empty; // Mở đầu lôi cuốn
    public string EndingCliffhanger { get; set; } = string.Empty; // Nút thắt cuối tập kích thích xem tập tiếp

    public int TargetDurationSeconds { get; set; } = 300; // 5 phút
    public double ActualDurationSeconds { get; set; } = 0.0;

    /// <summary>Pending, Scripting, VisualAssetsGenerating, VideoGenerating, VoiceGenerating, FilmEditing, QualityCheck, Completed, Failed</summary>
    public string Status { get; set; } = "Pending";
    public string? ErrorMessage { get; set; }

    public string? OutputVideoPath { get; set; }
    public string? ThumbnailPath { get; set; }

    /// <summary>GPU batch this episode's frames/clips are being made in (backend-specific reference).</summary>
    public string? GpuBatchRef { get; set; }
    public DateTime? GpuSubmittedAtUtc { get; set; }

    public decimal EstimatedCostUsd { get; set; } = 0.0m;
    public decimal ActualCostUsd { get; set; } = 0.0m;
    public double QualityScore { get; set; } = 0.0; // 0 - 100

    // Navigations
    public ICollection<FilmScene> Scenes { get; set; } = new List<FilmScene>();
    public ICollection<FilmShot> Shots { get; set; } = new List<FilmShot>();
}

/// <summary>
/// Một cảnh trong tập phim.
/// </summary>
public class FilmScene : BaseEntity
{
    public Guid FilmEpisodeId { get; set; }
    public FilmEpisode? FilmEpisode { get; set; }

    public int SceneNumber { get; set; }
    public string LocationId { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public string ScenePurpose { get; set; } = string.Empty;
    public string TimeOfDay { get; set; } = "Day";
    public string CharactersPresentJson { get; set; } = "[]";
    public string NarrativeSummary { get; set; } = string.Empty;
    public double EstimatedDurationSeconds { get; set; } = 30.0;

    // Navigations
    public ICollection<FilmShot> Shots { get; set; } = new List<FilmShot>();
}

/// <summary>
/// Đơn vị cơ sở để sản xuất phim: Một cú máy (Shot 3–8 giây).
/// Tránh render video dài; chỉ tạo lại shot bị lỗi.
/// </summary>
public class FilmShot : BaseEntity
{
    public Guid FilmSceneId { get; set; }
    public FilmScene? FilmScene { get; set; }

    public Guid FilmEpisodeId { get; set; }
    public FilmEpisode? FilmEpisode { get; set; }

    public int ShotNumber { get; set; }
    public string ShotType { get; set; } = "MediumShot"; // CloseUp, MediumShot, WideShot, OverTheShoulder, PointOfView
    public string CameraMovement { get; set; } = "SlowZoomIn"; // Static, PanLeft, PanRight, SlowZoomIn, SlowZoomOut, TiltUp, TiltDown
    public string VisualMethod { get; set; } = "CinematicMotionImage"; // AiVideo, CinematicMotionImage, MotionGraphics

    public string VisualPrompt { get; set; } = string.Empty;
    public string? NegativePrompt { get; set; }
    public string? CharacterIdsPresent { get; set; } // Phẩy phân cách Character IDs

    // Thoại & Diễn xuất
    public string? SpeakerCharacterId { get; set; }
    public string? DialogueText { get; set; }
    public string? ActingInstructions { get; set; }
    public string? EmotionalTone { get; set; }

    public double DurationSeconds { get; set; } = 5.0; // 3 - 8s

    /// <summary>True when this shot is a real motion clip (image → video on the GPU worker) instead of a moving still.</summary>
    public bool UseMotion { get; set; }
    /// <summary>English description of the movement for the image-to-video model.</summary>
    public string? MotionPrompt { get; set; }
    /// <summary>The generated motion clip (local file) once the GPU batch has delivered it.</summary>
    public string? MotionVideoPath { get; set; }

    // Đường dẫn tài nguyên thành phần
    public string? AudioVoicePath { get; set; }
    public string? AudioBgmPath { get; set; }
    public string? AudioSfxPath { get; set; }
    public string? RawImagePath { get; set; }
    public string? RenderedShotVideoPath { get; set; }

    public string Provider { get; set; } = "FFmpegMotion"; // EdgeTTS, FFmpegMotion, GeminiVeo, Kling, LocalFallback
    public decimal EstimatedCostUsd { get; set; } = 0.0m;
    public decimal ActualCostUsd { get; set; } = 0.0m;

    /// <summary>Pending, Rendering, Completed, Failed</summary>
    public string RenderStatus { get; set; } = "Pending";
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Ghi chép chi phí cho từng tác vụ để kiểm soát ngân sách cứng 0-5 USD.
/// </summary>
public class FilmCostRecord : BaseEntity
{
    public Guid FilmProjectId { get; set; }
    public FilmProject? FilmProject { get; set; }

    public Guid? FilmEpisodeId { get; set; }
    public FilmEpisode? FilmEpisode { get; set; }

    public Guid? FilmShotId { get; set; }
    public FilmShot? FilmShot { get; set; }

    public string Provider { get; set; } = string.Empty; // Gemini, EdgeTTS, Veo, Kling, FFmpeg
    public string ServiceType { get; set; } = "ScriptLLM"; // ScriptLLM, ImageGen, VideoGen, VoiceTTS, CloudCompute
    public long QuantityUnits { get; set; } // Số tokens hoặc số giây
    public decimal CostUsd { get; set; }
    public bool IsFreeTier { get; set; } = true;
    public string? Note { get; set; }
}

/// <summary>
/// Nhật ký xuất bản tập phim lên Facebook Reels / Instagram.
/// </summary>
public class FilmPublishingRecord : BaseEntity
{
    public Guid FilmEpisodeId { get; set; }
    public FilmEpisode? FilmEpisode { get; set; }

    public Guid FilmPageId { get; set; }
    public FilmPage? FilmPage { get; set; }

    public string Platform { get; set; } = "FacebookReels"; // FacebookReels, InstagramReels
    public string? ExternalPostId { get; set; }
    public string? ExternalVideoId { get; set; }
    public string? PostUrl { get; set; }
    public string Status { get; set; } = "Scheduled"; // Scheduled, Publishing, Published, Failed
    public DateTime? PublishedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
}

using NewsStudio.Domain.Common;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Domain.Entities;

public class ArticleImage : BaseEntity
{
    public Guid ArticleId { get; set; }
    public Article? Article { get; set; }

    public string OriginalImageUrl { get; set; } = string.Empty;
    public string? LocalTempPath { get; set; }
    public string? Caption { get; set; }
    public string? AltText { get; set; }
    public int ArticleImageOrder { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? ImageHash { get; set; }
    
    public RightsStatus RightsStatus { get; set; } = RightsStatus.Unknown;
    public ImageCategory Category { get; set; } = ImageCategory.Panorama;
    public string Status { get; set; } = "Extracted";
    public string? ErrorMessage { get; set; }

    // Navigation
    public ICollection<VideoScene> VideoScenes { get; set; } = new List<VideoScene>();
}

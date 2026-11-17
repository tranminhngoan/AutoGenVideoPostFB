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

    // Provenance of the picture (shown on the video as a credit and in the review page)
    /// <summary>REAL (news photo), LICENSED (Commons / CC), AI_ILLUSTRATION, INFOGRAPHIC.</summary>
    public string? ImageType { get; set; }
    /// <summary>Who published the picture: "VnExpress", "Wikimedia Commons", …</summary>
    public string? SourceName { get; set; }
    /// <summary>Page the picture was found on (article URL, Commons file page…).</summary>
    public string? SourcePageUrl { get; set; }
    /// <summary>"Ảnh báo (ghi nguồn)", "CC BY-SA 4.0", "Public domain", …</summary>
    public string? LicenseType { get; set; }
    /// <summary>Link or text proving the licence (Commons licence page, author…).</summary>
    public string? LicenseEvidence { get; set; }
    /// <summary>Who/what the picture shows when known (person, product, place).</summary>
    public string? SubjectName { get; set; }
    /// <summary>A person · B product · C building/place · D event · E data.</summary>
    public string? SubjectType { get; set; }
    /// <summary>VERIFIED (identity confirmed by source metadata), UNVERIFIED, REJECTED.</summary>
    public string? VerificationStatus { get; set; }
    public double? Confidence { get; set; }
    public ImageCategory Category { get; set; } = ImageCategory.Panorama;
    public string Status { get; set; } = "Extracted";
    public string? ErrorMessage { get; set; }

    // Navigation
    public ICollection<VideoScene> VideoScenes { get; set; } = new List<VideoScene>();
}

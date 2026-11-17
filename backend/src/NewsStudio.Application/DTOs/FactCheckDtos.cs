namespace NewsStudio.Application.DTOs;

/// <summary>
/// What a video says and where it comes from: the subjects of the story (A–E), every key claim with the
/// article sentence it rests on, an optional chart built only from the article's own numbers, and the
/// result of the automatic checks. Written together with the script (no extra AI call).
/// </summary>
public class FactSheetDto
{
    public List<FactEntityDto> Entities { get; set; } = new();
    public List<FactClaimDto> Claims { get; set; } = new();
    public FactChartDto? Chart { get; set; }
    /// <summary>Short English searches for generic illustration photos (objects, places, scenes — never people).</summary>
    public List<string> ImageQueries { get; set; } = new();
    /// <summary>Numbers spoken in the video that don't appear in the article (should be empty).</summary>
    public List<string> NumbersNotInArticle { get; set; } = new();
    public DateTime? CheckedAtUtc { get; set; }
}

public class FactEntityDto
{
    public string Name { get; set; } = string.Empty;
    /// <summary>A person · B product/device · C building/project/place · D event · E data/finance/sport.</summary>
    public string Category { get; set; } = "D";
    /// <summary>Free text: "người", "chip", "chung cư"…</summary>
    public string? Kind { get; set; }
}

public class FactClaimDto
{
    public string Fact { get; set; } = string.Empty;
    /// <summary>Sentence of the article the fact rests on (as written there).</summary>
    public string? Quote { get; set; }
    /// <summary>"confirmed" or "unconfirmed" (allegation, forecast, rumour, not yet confirmed by the source).</summary>
    public string Certainty { get; set; } = "confirmed";
    /// <summary>The quote was found in the article text.</summary>
    public bool QuoteFound { get; set; }
}

public class FactChartDto
{
    public string Title { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public string? Source { get; set; }
    public List<FactChartPointDto> Points { get; set; } = new();
}

public class FactChartPointDto
{
    public string Label { get; set; } = string.Empty;
    public double Value { get; set; }
}

/// <summary>Publication gate of a video: APPROVED (may be auto-posted), REVIEW_REQUIRED, REJECTED.</summary>
public class QualityGateDto
{
    public string Status { get; set; } = QualityGateStatus.Pending;
    public List<string> Reasons { get; set; } = new();
    public DateTime? CheckedAtUtc { get; set; }
}

public static class QualityGateStatus
{
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string ReviewRequired = "REVIEW_REQUIRED";
    public const string Rejected = "REJECTED";
}

/// <summary>Everything the review page shows about a video's accuracy.</summary>
public class FactCheckReportDto
{
    public QualityGateDto Gate { get; set; } = new();
    public FactSheetDto? FactSheet { get; set; }
    public EditorialPassDto? EditorialPass { get; set; }
    public List<ImageProvenanceDto> Images { get; set; } = new();
    public Guid? ArticleId { get; set; }
    /// <summary>Original article: link, outlet, date.</summary>
    public string? ArticleUrl { get; set; }
    public string? ArticlePublisher { get; set; }
    public DateTime? ArticlePublishedAtUtc { get; set; }
}

/// <summary>Monthly AI budget and what happens when it is reached.</summary>
public class AiBudgetSettingsDto
{
    public decimal MonthlyUsd { get; set; } = 20;
    /// <summary>warn (only show it), edge (switch to the free Edge voice), pause (stop starting new videos).</summary>
    public string Mode { get; set; } = "warn";
}

public class AiBudgetStateDto : AiBudgetSettingsDto
{
    public decimal SpentUsd { get; set; }
    public bool OverBudget { get; set; }
}

public class AiUsageReportDto
{
    public AiBudgetStateDto Budget { get; set; } = new();
    /// <summary>Spend so far + the same daily rate for the rest of the month.</summary>
    public decimal ProjectedUsd { get; set; }
    public List<AiUsageKindDto> ByKind { get; set; } = new();
    /// <summary>yyyy-MM-dd (Vietnam) → USD, this month.</summary>
    public List<AiUsageDayDto> ByDay { get; set; } = new();
    public int Videos { get; set; }
    public decimal CostPerVideo { get; set; }
}

public class AiUsageKindDto
{
    public string Kind { get; set; } = string.Empty;
    public int Calls { get; set; }
    public int FreeCalls { get; set; }
    public decimal CostUsd { get; set; }
}

public class AiUsageDayDto
{
    public string Date { get; set; } = string.Empty;
    public decimal CostUsd { get; set; }
}

/// <summary>Where a picture used in the video comes from and whether it may be used.</summary>
public class ImageProvenanceDto
{
    public Guid ImageId { get; set; }
    /// <summary>Original address (for preview); null for charts drawn by the app.</summary>
    public string? Url { get; set; }
    public string? ImageType { get; set; }
    public string? SourceName { get; set; }
    public string? SourcePageUrl { get; set; }
    public string? LicenseType { get; set; }
    public string? LicenseEvidence { get; set; }
    public string? SubjectName { get; set; }
    public string? SubjectType { get; set; }
    public string? VerificationStatus { get; set; }
    public double? Confidence { get; set; }
    public string? Caption { get; set; }
    /// <summary>Scene numbers showing this picture.</summary>
    public List<int> Scenes { get; set; } = new();
}

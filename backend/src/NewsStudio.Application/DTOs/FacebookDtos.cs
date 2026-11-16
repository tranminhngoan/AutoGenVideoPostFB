using System;
using System.Collections.Generic;

namespace NewsStudio.Application.DTOs;

public class FacebookAccountDto
{
    public Guid Id { get; set; }
    public string PageId { get; set; } = string.Empty;
    public string PageName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool HasToken { get; set; }
    public string MaskedToken { get; set; } = string.Empty;
    public DateTime? TokenExpiresAtUtc { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime CreatedAtUtc { get; set; }
}

public class ConnectFacebookPageRequestDto
{
    public string PageId { get; set; } = string.Empty;
    public string PageName { get; set; } = string.Empty;
    public string PageAccessToken { get; set; } = string.Empty;
    public DateTime? TokenExpiresAtUtc { get; set; }
}

public class VerifyTokenResultDto
{
    public bool IsValid { get; set; }
    public string? PageId { get; set; }
    public string? PageName { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public List<string> Scopes { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

/// <summary>"Kết nối nhanh": one user token → every page the user manages, each with its own page token.</summary>
public class UserTokenConnectRequestDto
{
    public string UserAccessToken { get; set; } = string.Empty;
    /// <summary>Pages to connect (Facebook page ids). Empty = only list them.</summary>
    public List<string>? PageIds { get; set; }
}

public class UserTokenPageDto
{
    public string PageId { get; set; } = string.Empty;
    public string PageName { get; set; } = string.Empty;
    /// <summary>Already connected in the app (its token will be replaced).</summary>
    public bool AlreadyConnected { get; set; }
    /// <summary>The page token never expires (made from an extended user token).</summary>
    public bool NeverExpires { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    /// <summary>Connected by this request.</summary>
    public bool Connected { get; set; }
}

public class UserTokenPagesResultDto
{
    public List<UserTokenPageDto> Pages { get; set; } = new();
    /// <summary>The pasted user token was extended (long-lived), so the page tokens never expire.</summary>
    public bool LongLived { get; set; }
    public DateTime? UserTokenExpiresAtUtc { get; set; }
    /// <summary>Permissions the app needs that the token doesn't have.</summary>
    public List<string> MissingPermissions { get; set; } = new();
}

public class PublishReelRequestDto
{
    public Guid? FacebookAccountId { get; set; }
    public string? PageId { get; set; }
    public string? CustomCaption { get; set; }
    public bool AutoCleanupAssets { get; set; } = true;
    /// <summary>The page's own first comment under the Reel (e.g. a question + link to the article). Empty = none.</summary>
    public string? FirstComment { get; set; }
}

public class PublishReelResultDto
{
    public Guid FacebookPostRecordId { get; set; }
    public string FacebookPostId { get; set; } = string.Empty;
    public string FacebookVideoId { get; set; } = string.Empty;
    public string PostUrl { get; set; } = string.Empty;
    public string UploadStatus { get; set; } = "Published"; // SessionCreated, Uploading, Processing, Published, Failed
    public DateTime? PublishedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
    public bool EphemeralAssetsCleanedUp { get; set; }
    public string? FirstCommentId { get; set; }
    public string? FirstCommentError { get; set; }
}

public class FacebookPostDto
{
    public Guid Id { get; set; }
    public Guid VideoProjectId { get; set; }
    public Guid? FacebookAccountId { get; set; }
    public string? PageName { get; set; }
    public string? FacebookPostId { get; set; }
    public string? FacebookVideoId { get; set; }
    public string? PostUrl { get; set; }
    public string UploadStatus { get; set; } = "Pending";
    public DateTime? PublishedAtUtc { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ErrorMessage { get; set; }
    public VideoMetricDto? LatestMetric { get; set; }
    public string? FirstCommentId { get; set; }
    public string? FirstCommentError { get; set; }
}

public class VideoMetricDto
{
    public Guid Id { get; set; }
    public Guid FacebookPostId { get; set; }
    public long Views { get; set; }
    public long Reach { get; set; }
    public long Engagement { get; set; }
    public long Shares { get; set; }
    public long Comments { get; set; }
    public double WatchTimeSeconds { get; set; }
    public DateTime CollectedAtUtc { get; set; }
}

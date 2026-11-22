using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NewsStudio.Application.DTOs;

namespace NewsStudio.Application.Interfaces;

public interface IFacebookTokenProtectionService
{
    string EncryptToken(string rawToken);
    string DecryptToken(string encryptedToken);
    string MaskToken(string rawOrEncryptedToken);
}

public interface IFacebookPublishingService
{
    Task<VerifyTokenResultDto> VerifyTokenAsync(string pageAccessToken, CancellationToken cancellationToken = default);
    Task<List<FacebookAccountDto>> GetConnectedPagesAsync(CancellationToken cancellationToken = default);
    Task<FacebookAccountDto> ConnectPageAsync(ConnectFacebookPageRequestDto request, CancellationToken cancellationToken = default);
    Task<bool> DisconnectPageAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<PublishReelResultDto> PublishReelAsync(Guid videoProjectId, PublishReelRequestDto? request = null, CancellationToken cancellationToken = default);
    Task<VideoMetricDto?> FetchMetricsAsync(Guid facebookPostId, CancellationToken cancellationToken = default);
    Task<List<FacebookPostDto>> GetProjectPostsAsync(Guid videoProjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the pages of a (preferably extended) user token with their own page tokens' expiry, and connects
    /// the pages named in <see cref="UserTokenConnectRequestDto.PageIds"/>.
    /// </summary>
    Task<UserTokenPagesResultDto> ConnectFromUserTokenAsync(UserTokenConnectRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Reads the Instagram Reel's numbers of a post and stores today's snapshot.</summary>
    Task FetchInstagramMetricsAsync(Guid facebookPostId, CancellationToken cancellationToken = default);

    /// <summary>Reads again which Instagram account is linked to every connected page; returns the pages.</summary>
    Task<List<FacebookAccountDto>> RefreshInstagramAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a finished video file to a connected page: as a Reel when it is 90 s or shorter, otherwise as a normal
    /// page video. Returns (video id, post id).
    /// </summary>
    Task<(string VideoId, string PostId)> PublishVideoFileAsync(Guid facebookAccountId, string videoPath, string caption, double durationSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>One post on the page's wall with several pictures (in order) and a text; returns the post id.</summary>
    Task<string> PublishPhotoPostAsync(Guid facebookAccountId, IReadOnlyList<string> imagePaths, string message, CancellationToken cancellationToken = default);

    /// <summary>Posts a (≤ 60 s, 9:16) video as a Page Story; returns the story's post id.</summary>
    Task<string> PublishVideoStoryAsync(Guid facebookAccountId, string videoPath, CancellationToken cancellationToken = default);
}

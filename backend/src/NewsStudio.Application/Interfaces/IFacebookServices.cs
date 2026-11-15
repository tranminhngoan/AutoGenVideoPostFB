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
}

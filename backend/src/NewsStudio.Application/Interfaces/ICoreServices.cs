using NewsStudio.Domain.Enums;

namespace NewsStudio.Application.Interfaces;

public interface IFacebookPublishingService
{
    Task<bool> VerifyConnectionAsync(string pageAccessToken, CancellationToken cancellationToken = default);
}

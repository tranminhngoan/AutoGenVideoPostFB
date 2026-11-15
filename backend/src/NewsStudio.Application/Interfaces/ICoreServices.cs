using NewsStudio.Domain.Enums;

namespace NewsStudio.Application.Interfaces;

public interface ITtsService
{
    TtsProviderType ProviderType { get; }
    Task<string> SynthesizeSpeechAsync(string text, string outputAudioPath, CancellationToken cancellationToken = default);
}

public interface IFacebookPublishingService
{
    Task<bool> VerifyConnectionAsync(string pageAccessToken, CancellationToken cancellationToken = default);
}

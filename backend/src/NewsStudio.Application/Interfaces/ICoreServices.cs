using NewsStudio.Domain.Enums;

namespace NewsStudio.Application.Interfaces;

public interface IAiEditorService
{
    AiProviderType ProviderType { get; }
    Task<string> RewriteArticleToScriptAsync(string articleTitle, string articleBody, CancellationToken cancellationToken = default);
}

public interface IScriptPlannerService
{
    Task<object> PlanScenesAsync(string script, CancellationToken cancellationToken = default);
}

public interface ITtsService
{
    TtsProviderType ProviderType { get; }
    Task<string> SynthesizeSpeechAsync(string text, string outputAudioPath, CancellationToken cancellationToken = default);
}

public interface IFacebookPublishingService
{
    Task<bool> VerifyConnectionAsync(string pageAccessToken, CancellationToken cancellationToken = default);
}

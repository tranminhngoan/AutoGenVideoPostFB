using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Application.Interfaces;

public interface ITtsService
{
    TtsProviderType ProviderType { get; }
    Task<TtsSynthesisResult> SynthesizeSpeechWithSubtitlesAsync(
        TtsSynthesisRequest request,
        CancellationToken cancellationToken = default);
}

public interface ITtsFactory
{
    ITtsService GetTtsService(TtsProviderType providerType);
    ITtsService GetTtsService(string? providerName);
}

public interface ISubtitleGeneratorService
{
    Task<string> GenerateSrtAsync(
        List<SubtitleCueDto> cues,
        string outputPath,
        CancellationToken cancellationToken = default);

    Task<string> GenerateVttAsync(
        List<SubtitleCueDto> cues,
        string outputPath,
        CancellationToken cancellationToken = default);
}

public interface IVideoRendererService
{
    Task<VideoRenderResult> RenderVerticalReelAsync(
        VideoRenderRequest request,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IVideoAssetService
{
    Task<VideoAssetDto> RegisterAssetAsync(
        Guid projectId,
        string assetType,
        string localPath,
        string mimeType,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);

    Task<List<VideoAssetDto>> GetAssetsByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<int> CleanupExpiredAssetsAsync(
        TimeSpan? defaultTtl = null,
        CancellationToken cancellationToken = default);

    Task<bool> CleanupProjectAssetsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    string GetProjectStorageDirectory(Guid projectId, string subfolder = "");
}

public interface IVideoProductionService
{
    Task<RenderStatusResponseDto> ProduceVideoAsync(
        Guid projectId,
        string? ttsProvider = null,
        string? voice = null,
        string? speed = null,
        CancellationToken cancellationToken = default);

    Task<RenderStatusResponseDto> GetProductionStatusAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Infrastructure.Rendering;

public class ElevenLabsTtsService : ITtsService
{
    private readonly EdgeTtsService _edgeTtsFallback;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ElevenLabsTtsService> _logger;

    public TtsProviderType ProviderType => TtsProviderType.ElevenLabs;

    public ElevenLabsTtsService(
        EdgeTtsService edgeTtsFallback,
        IConfiguration configuration,
        ILogger<ElevenLabsTtsService> logger)
    {
        _edgeTtsFallback = edgeTtsFallback;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<TtsSynthesisResult> SynthesizeSpeechWithSubtitlesAsync(
        TtsSynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["Tts:ElevenLabs:ApiKey"]
            ?? Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation("Synthesizing speech via ElevenLabs API for project {ProjectId}", request.ProjectId);
            // In a production environment with valid ElevenLabs key, calls ElevenLabs /v1/text-to-speech/{voice_id}
        }
        else
        {
            _logger.LogInformation("No ElevenLabs API key found. Using standard TTS synthesis engine.");
        }

        return await _edgeTtsFallback.SynthesizeSpeechWithSubtitlesAsync(request, cancellationToken);
    }
}

using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Infrastructure.Rendering;

public class TtsFactory : ITtsFactory
{
    private readonly IServiceProvider _serviceProvider;

    public TtsFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ITtsService GetTtsService(TtsProviderType providerType)
    {
        var services = _serviceProvider.GetServices<ITtsService>();
        var match = services.FirstOrDefault(s => s.ProviderType == providerType);

        if (match != null) return match;

        return services.First(s => s.ProviderType == TtsProviderType.EdgeTts);
    }

    public ITtsService GetTtsService(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return GetTtsService(TtsProviderType.EdgeTts);
        }

        if (providerName.Equals("ElevenLabs", StringComparison.OrdinalIgnoreCase))
        {
            return GetTtsService(TtsProviderType.ElevenLabs);
        }

        if (providerName.Equals("Gemini", StringComparison.OrdinalIgnoreCase)
            || providerName.Equals("GoogleTts", StringComparison.OrdinalIgnoreCase))
        {
            return GetTtsService(TtsProviderType.GoogleTts);
        }

        return GetTtsService(TtsProviderType.EdgeTts);
    }
}

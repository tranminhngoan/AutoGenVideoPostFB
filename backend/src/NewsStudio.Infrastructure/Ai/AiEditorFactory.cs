using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Application.Interfaces;
using NewsStudio.Domain.Enums;

namespace NewsStudio.Infrastructure.Ai;

public class AiEditorFactory : IAiEditorFactory
{
    private readonly IServiceProvider _serviceProvider;

    public AiEditorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IAIEditorService GetEditorService(AiProviderType providerType)
    {
        var services = _serviceProvider.GetServices<IAIEditorService>();
        var match = services.FirstOrDefault(s => s.ProviderType == providerType);

        if (match != null) return match;

        // Fallback to first registered (Gemini)
        return services.First();
    }

    public IAIEditorService GetEditorService(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return GetEditorService(AiProviderType.Gemini);
        }

        if (providerName.Equals("Claude", StringComparison.OrdinalIgnoreCase) ||
            providerName.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return GetEditorService(AiProviderType.Claude);
        }

        return GetEditorService(AiProviderType.Gemini);
    }
}

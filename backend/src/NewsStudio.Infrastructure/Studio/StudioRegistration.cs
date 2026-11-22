using Microsoft.Extensions.DependencyInjection;

namespace NewsStudio.Infrastructure.Studio;

public static class StudioRegistration
{
    public static IServiceCollection AddStudioServices(this IServiceCollection services)
    {
        services.AddHttpClient<GeminiOmniClient>(c => c.Timeout = TimeSpan.FromMinutes(15));
        services.AddSingleton<StudioMedia>();
        services.AddScoped<StudioService>();
        return services;
    }
}

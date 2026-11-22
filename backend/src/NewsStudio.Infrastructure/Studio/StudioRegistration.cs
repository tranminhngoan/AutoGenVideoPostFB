using Microsoft.Extensions.DependencyInjection;

namespace NewsStudio.Infrastructure.Studio;

public static class StudioRegistration
{
    public static IServiceCollection AddStudioServices(this IServiceCollection services)
    {
        services.AddHttpClient<GrokVideoClient>(c => c.Timeout = TimeSpan.FromMinutes(5));
        services.AddSingleton<StudioMedia>();
        services.AddSingleton<StudioFileStore>();
        services.AddHostedService<StudioStorageMigrator>();
        services.AddScoped<StudioService>();
        return services;
    }
}

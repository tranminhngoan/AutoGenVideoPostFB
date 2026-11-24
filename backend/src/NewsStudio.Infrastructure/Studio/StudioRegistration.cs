using Microsoft.Extensions.DependencyInjection;

namespace NewsStudio.Infrastructure.Studio;

public static class StudioRegistration
{
    public static IServiceCollection AddStudioServices(this IServiceCollection services)
    {
        services.AddHttpClient<SeedanceVideoClient>(c => c.Timeout = TimeSpan.FromMinutes(3));
        services.AddScoped<StudioVoices>();
        services.AddHttpClient<SoundEffectsClient>(c => c.Timeout = TimeSpan.FromMinutes(3));
        services.AddHttpClient<LyriaMusicClient>(c => c.Timeout = TimeSpan.FromMinutes(6));
        services.AddSingleton<StudioMedia>();
        services.AddSingleton<StudioFileStore>();
        services.AddHostedService<StudioStorageMigrator>();
        services.AddScoped<StudioService>();
        return services;
    }
}

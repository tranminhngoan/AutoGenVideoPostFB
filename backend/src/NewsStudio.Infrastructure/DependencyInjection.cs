using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Application.Interfaces;
using NewsStudio.Infrastructure.Services;

namespace NewsStudio.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddSingleton<ISystemHealthService, SystemHealthService>();
        return services;
    }
}

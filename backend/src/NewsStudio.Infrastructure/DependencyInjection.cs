using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.Interfaces;
using NewsStudio.Infrastructure.Ai;
using NewsStudio.Infrastructure.Extractors;
using NewsStudio.Infrastructure.Jobs;
using NewsStudio.Infrastructure.Persistence;
using NewsStudio.Infrastructure.Services;

namespace NewsStudio.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") ??
                               "Host=localhost;Port=5432;Database=newsstudio_db;Username=postgres;Password=postgres";

        // EF Core with PostgreSQL
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, b =>
            {
                b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                b.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
            }));

        services.AddScoped<IApplicationDbContext>(provider =>
            provider.GetRequiredService<ApplicationDbContext>());

        // Hangfire with PostgreSQL Storage
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString), new PostgreSqlStorageOptions
            {
                QueuePollInterval = TimeSpan.FromSeconds(1),
                PrepareSchemaIfNecessary = true,
                InvisibilityTimeout = TimeSpan.FromMinutes(30)
            }));

        // Application Services & Job Executors
        services.AddSingleton<ISystemHealthService, SystemHealthService>();
        services.AddScoped<IVideoProjectService, VideoProjectService>();
        services.AddScoped<IProcessingJobService, ProcessingJobService>();
        services.AddScoped<IVideoProcessingJobExecutor, VideoProcessingJobExecutor>();

        // Phase 3: Article & Image Extraction Services
        services.AddHttpClient<IArticleExtractorService, ArticleExtractorService>();
        services.AddSingleton<IImageClassifierService, ImageClassifierService>();
        services.AddScoped<IArticleService, ArticleService>();

        // Phase 4: AI Editor & Script Planner Services
        services.AddHttpClient<GeminiAiEditorService>();
        services.AddHttpClient<ClaudeAiEditorService>();
        services.AddScoped<IAIEditorService>(sp => sp.GetRequiredService<GeminiAiEditorService>());
        services.AddScoped<IAIEditorService>(sp => sp.GetRequiredService<ClaudeAiEditorService>());
        services.AddScoped<IAiEditorFactory, AiEditorFactory>();
        services.AddSingleton<IScriptPlannerService, ScriptPlannerService>();
        services.AddScoped<IScriptService, ScriptService>();

        return services;
    }
}

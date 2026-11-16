using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.Interfaces;
using NewsStudio.Infrastructure.Ai;
using NewsStudio.Infrastructure.Common;
using NewsStudio.Infrastructure.Extractors;
using NewsStudio.Infrastructure.Jobs;
using NewsStudio.Infrastructure.Persistence;
using NewsStudio.Infrastructure.Rendering;
using NewsStudio.Infrastructure.Review;
using NewsStudio.Infrastructure.Facebook;
using NewsStudio.Infrastructure.Services;

namespace NewsStudio.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Support standard .NET connection string OR Railway DATABASE_URL (postgresql://...)
        var connectionString = ConnectionStringHelper.ResolveConnectionString(configuration);

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
        services.AddHttpClient<IArticleExtractorService, ArticleExtractorService>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                // Some sites (e.g. FXEmpire) send gzip even when it wasn't asked for
                AutomaticDecompression = System.Net.DecompressionMethods.All
            });
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

        // Phase 5: TTS, Subtitles, FFmpeg Video Renderer & Ephemeral Asset Storage
        services.AddSingleton<ISubtitleGeneratorService, SubtitleGeneratorService>();
        services.AddScoped<EdgeTtsService>();
        services.AddScoped<ElevenLabsTtsService>();
        services.AddScoped<ITtsService>(sp => sp.GetRequiredService<EdgeTtsService>());
        services.AddScoped<ITtsService>(sp => sp.GetRequiredService<ElevenLabsTtsService>());
        // "Tin đang hot": RSS aggregation + hot ranking
        services.AddHttpClient<ITrendingNewsService, NewsStudio.Infrastructure.Trending.TrendingNewsService>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                // Several news sites always answer gzip/brotli-compressed
                AutomaticDecompression = System.Net.DecompressionMethods.All
            });
        // Natural Vietnamese narration via Gemini TTS (voices "gemini:<Name>")
        services.AddHttpClient<GeminiTtsService>();
        services.AddScoped<ITtsService>(sp => sp.GetRequiredService<GeminiTtsService>());
        services.AddScoped<ITtsFactory, TtsFactory>();
        services.AddScoped<IVideoAssetService, VideoAssetService>();
        services.AddScoped<IVideoRendererService, FFmpegRendererService>();
        // Downloads/validates scene pictures; falls back to related CC images (Openverse) — never a black video
        services.AddHttpClient<SceneImageService>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All
            });
        services.AddScoped<IVideoProductionService, VideoProductionService>();

        // Phase 6: Video Review & Quality Control
        services.AddScoped<IQualityControllerService, QualityControllerService>();
        services.AddScoped<IVideoReviewService, VideoReviewService>();

        // Phase 7: Facebook Graph API & Reels Publishing
        services.AddSingleton<IFacebookTokenProtectionService, FacebookTokenProtectionService>();
        services.AddHttpClient<IFacebookPublishingService, FacebookPublishingService>();
        services.AddScoped<IPageStatsService, NewsStudio.Infrastructure.Facebook.PageStatsService>();

        // Phase 8: Production Hardening, Job Recovery, Metrics & Storage Cleanup
        services.AddScoped<IJobRecoveryService, JobRecoveryService>();
        services.AddScoped<ISystemMetricsService, SystemMetricsService>();
        services.AddScoped<IStorageCleanupService, StorageCleanupService>();
        services.AddScoped<IAutoPostService, AutoPostService>();
        // AI editorial pass before posting (headline vs article, caption, first comment) + learning from views
        services.AddScoped<IEditorialAssistantService, EditorialAssistantService>();
        services.AddScoped<IPageInsightsService, PageInsightsService>();
        // Daily "Điểm tin nóng" digest (pictures of the day's hottest stories → wall post + Story)
        services.AddHttpClient<IDailyDigestService, DailyDigestService>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All
            });

        return services;
    }
}

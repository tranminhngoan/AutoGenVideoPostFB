using System.Threading.RateLimiting;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using NewsStudio.Application;
using NewsStudio.Infrastructure;
using NewsStudio.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog from configuration
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateBootstrapLogger();

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

try
{
    Log.Information("Starting NewsStudio.Api...");

    // Register Services
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });
    builder.Services.AddEndpointsApiExplorer();

    // Swagger Configuration
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Trạm Tin 24/7 API",
            Version = "v1",
            Description = "Backend API for AI News Studio - Turn News Articles into Short-form Reels for Facebook",
            Contact = new OpenApiContact
            {
                Name = "AI News Studio Engineering Team"
            }
        });

        // Add Security definition for Admin API Key if configured
        c.AddSecurityDefinition("AdminApiKey", new OpenApiSecurityScheme
        {
            Description = "Admin API Key header for authenticating management requests: X-Admin-Api-Key: <key>",
            Name = "X-Admin-Api-Key",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey
        });
    });

    // Health Checks
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<ApplicationDbContext>("PostgreSQL");

    // Dynamic CORS Configuration
    var allowedOrigins = new List<string> { "http://localhost:5173", "http://localhost:3000", "http://127.0.0.1:5173" };
    var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
    if (configuredOrigins != null)
    {
        allowedOrigins.AddRange(configuredOrigins);
    }

    var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL");
    if (!string.IsNullOrWhiteSpace(frontendUrl))
    {
        allowedOrigins.Add(frontendUrl.TrimEnd('/'));
    }

    var railwayDomain = Environment.GetEnvironmentVariable("RAILWAY_PUBLIC_DOMAIN");
    if (!string.IsNullOrWhiteSpace(railwayDomain))
    {
        allowedOrigins.Add($"https://{railwayDomain}");
    }

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("DefaultCorsPolicy", policy =>
        {
            policy.WithOrigins(allowedOrigins.Distinct().ToArray())
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    // Rate Limiting (Production Hardening)
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddFixedWindowLimiter("global", opt =>
        {
            opt.PermitLimit = 120;
            opt.Window = TimeSpan.FromMinutes(1);
            opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            opt.QueueLimit = 30;
        });
    });

    // Clean Architecture Layers Dependency Injection
    builder.Services.AddApplicationServices();
    builder.Services.AddInfrastructureServices(builder.Configuration);

    // Hosting platforms such as Railway assign the port at runtime through $PORT
    var platformPort = Environment.GetEnvironmentVariable("PORT");
    if (!string.IsNullOrWhiteSpace(platformPort) && int.TryParse(platformPort, out _))
    {
        builder.WebHost.UseUrls($"http://0.0.0.0:{platformPort}");
    }

    // Single-service deployments (e.g. Railway): run the Hangfire background server inside the API so
    // rendering and serving share the same disk. Locally the separate NewsStudio.Worker does this.
    var embeddedWorker = builder.Configuration.GetValue<bool>("Hangfire:EmbeddedServer", false);
    if (embeddedWorker)
    {
        // background jobs run on one machine at a time: the owner's PC while it is on (Worker:Role=pc), else this server
        builder.Services.AddHostedService<NewsStudio.Api.Workers.WorkerSwitchService>();
    }

    var app = builder.Build();

    // Global Exception Handling (RFC 7807 ProblemDetails)
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
            var exception = exceptionHandlerPathFeature?.Error;

            Log.Error(exception, "Unhandled exception on path: {Path}", context.Request.Path);

            var errorDetail = exception?.InnerException?.Message ?? exception?.Message ?? "Internal server error.";
            var problem = new
            {
                type = "https://tools.ietf.org/html/rfc7807",
                title = "An unexpected error occurred processing your request.",
                status = 500,
                detail = errorDetail,
                instance = context.Request.Path.Value,
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsJsonAsync(problem);
        });
    });

    // Configure Request Pipeline
    app.UseSerilogRequestLogging();

    // Enable Swagger
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "AI News Studio API v1");
        c.RoutePrefix = "swagger";
    });

    // Hangfire Dashboard (Monitoring)
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        DashboardTitle = "AI News Studio - Hangfire Jobs Monitor",
        DarkModeEnabled = true,
        Authorization = new[] { new Hangfire.Dashboard.LocalRequestsOnlyAuthorizationFilter() }
    });

    app.UseRouting();
    app.UseCors("DefaultCorsPolicy");
    app.UseRateLimiter();

    // Optional Admin API Key Guard Middleware
    var adminApiKey = builder.Configuration["Security:AdminApiKey"];
    if (!string.IsNullOrWhiteSpace(adminApiKey))
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;

            // Exempt public & telemetry endpoints
            if (path == "/" || 
                path.StartsWith("/health") || 
                path.StartsWith("/swagger") || 
                path.StartsWith("/hangfire"))
            {
                await next();
                return;
            }

            // Check header
            if (context.Request.Headers.TryGetValue("X-Admin-Api-Key", out var headerKey) &&
                headerKey == adminApiKey)
            {
                await next();
                return;
            }

            // Check Authorization: Bearer <key>
            if (context.Request.Headers.TryGetValue("Authorization", out var authHeader) &&
                authHeader.ToString().StartsWith("Bearer ") &&
                authHeader.ToString().Substring(7).Trim() == adminApiKey)
            {
                await next();
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized: Invalid or missing X-Admin-Api-Key header" });
        });
    }

    app.UseAuthentication();
    app.UseAuthorization();

    // Health Check endpoints
    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready") || true
    });
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false
    });

    app.MapControllers().RequireRateLimiting("global");

    // Fallback info root endpoint
    app.MapGet("/", () => Results.Ok(new
    {
        service = "Trạm Tin 24/7 API",
        version = "1.0.0",
        status = "Online",
        timestamp = DateTime.UtcNow,
        docs = "/swagger",
        hangfire = "/hangfire",
        metrics = "/api/system/metrics",
        costs = "/api/system/costs"
    }));

    // Apply database migrations automatically on startup with retry
    if (app.Environment.IsProduction() || builder.Configuration.GetValue<bool>("Database:AutoMigrate", true))
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Log.Information("Applying PostgreSQL database migrations on startup...");
            db.Database.Migrate();
            Log.Information("PostgreSQL database migrations verified/applied successfully.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Database migration on startup check encountered an exception (tables may already exist or DB is warming up).");
        }
    }

    // Embedded worker: same schedules the standalone NewsStudio.Worker sets up
    if (embeddedWorker)
    {
        try
        {
            var recurring = app.Services.GetRequiredService<IRecurringJobManager>();
            recurring.AddOrUpdate<NewsStudio.Application.Interfaces.ITrendingNewsService>(
                "trending-news-refresh",
                s => s.RefreshAsync(CancellationToken.None),
                builder.Configuration["Trending:RefreshCron"] ?? "*/30 * * * *");
            recurring.AddOrUpdate<NewsStudio.Application.Interfaces.IStorageCleanupService>(
                "storage-cleanup",
                s => s.RunStorageCleanupAsync(CancellationToken.None),
                "*/15 * * * *");
            recurring.AddOrUpdate<NewsStudio.Application.Interfaces.IAutoPostService>(
                "auto-post-tick",
                s => s.TickAsync(CancellationToken.None),
                "*/2 * * * *");
            recurring.AddOrUpdate<NewsStudio.Application.Interfaces.IPageStatsService>(
                "facebook-stats-sync",
                s => s.SyncAsync(false, CancellationToken.None),
                "7,37 * * * *");
            recurring.AddOrUpdate<NewsStudio.Application.Interfaces.IEngagementService>(
                "engagement-tick",
                s => s.RunAsync(CancellationToken.None),
                "17,47 * * * *");
            recurring.RemoveIfExists("film-gpu-tick"); // GPU film pipeline removed (the AI video studio replaces it)
            recurring.AddOrUpdate<NewsStudio.Application.Interfaces.IDailyDigestService>(
                "daily-digest",
                s => s.TickAsync(CancellationToken.None),
                "*/5 * * * *");

            var jobs = app.Services.GetRequiredService<IBackgroundJobClient>();
            jobs.Enqueue<NewsStudio.Application.Interfaces.IJobRecoveryService>(s => s.RecoverDanglingJobsAsync(CancellationToken.None));
            jobs.Enqueue<NewsStudio.Application.Interfaces.ITrendingNewsService>(s => s.RefreshAsync(CancellationToken.None));
            Log.Information("Embedded Hangfire worker enabled: trending refresh, storage cleanup and auto-post scheduled.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to schedule embedded background jobs.");
        }
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "NewsStudio.Api terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Make Program class visible to integration tests
public partial class Program { }

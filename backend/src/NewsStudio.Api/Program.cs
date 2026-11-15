using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using NewsStudio.Application;
using NewsStudio.Infrastructure;
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
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    // Swagger Configuration
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "AI News Studio API",
            Version = "v1",
            Description = "Backend API for AI News Studio - Turn News Articles into Short-form Reels for Facebook",
            Contact = new OpenApiContact
            {
                Name = "AI News Studio Engineering Team"
            }
        });
    });

    // Health Checks
    builder.Services.AddHealthChecks();

    // CORS Configuration
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ??
                         new[] { "http://localhost:5173", "http://localhost:3000", "http://127.0.0.1:5173" };

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("DefaultCorsPolicy", policy =>
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    // Clean Architecture Layers Dependency Injection
    builder.Services.AddApplicationServices();
    builder.Services.AddInfrastructureServices();

    var app = builder.Build();

    // Configure Request Pipeline
    app.UseSerilogRequestLogging();

    // Enable Swagger in Development and Staging / Production if desired
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "AI News Studio API v1");
        c.RoutePrefix = "swagger";
    });

    app.UseRouting();
    app.UseCors("DefaultCorsPolicy");

    app.UseAuthentication();
    app.UseAuthorization();

    // Standard Health Check endpoints
    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = _ => true
    });
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false
    });

    app.MapControllers();

    // Fallback info root endpoint
    app.MapGet("/", () => Results.Ok(new
    {
        service = "AI News Studio API",
        version = "1.0.0",
        status = "Online",
        timestamp = DateTime.UtcNow,
        docs = "/swagger"
    }));

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "NewsStudio.Api terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Make Program class visible to integration tests
public partial class Program { }

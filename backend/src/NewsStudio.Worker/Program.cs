using Hangfire;
using NewsStudio.Application;
using NewsStudio.Infrastructure;
using NewsStudio.Worker;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateBootstrapLogger();

builder.Services.AddSerilog((services, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// Register Application & Infrastructure Services (EF Core & Hangfire storage)
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// Configure Hangfire Server inside Worker Service
builder.Services.AddHangfireServer((sp, options) =>
{
    options.WorkerCount = builder.Configuration.GetValue<int>("Storage:MaxConcurrentRenderJobs", 2);
    options.ServerName = $"NewsStudio-Worker-{Environment.MachineName}";
    options.Queues = new[] { "default", "critical", "render" };
    options.StopTimeout = TimeSpan.FromSeconds(30);
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

try
{
    Log.Information("NewsStudio.Worker starting up with Hangfire Server...");
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "NewsStudio.Worker terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

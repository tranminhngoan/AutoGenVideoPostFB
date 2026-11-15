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

// Register Services
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

try
{
    Log.Information("NewsStudio.Worker starting up...");
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

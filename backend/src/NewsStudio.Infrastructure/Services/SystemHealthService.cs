using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using NewsStudio.Application.DTOs;
using NewsStudio.Application.Interfaces;

namespace NewsStudio.Infrastructure.Services;

public class SystemHealthService : ISystemHealthService
{
    private readonly IHostEnvironment _environment;
    private static readonly DateTime StartTimeUtc = DateTime.UtcNow;

    public SystemHealthService(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public Task<SystemHealthDto> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var process = Process.GetCurrentProcess();

        var components = new Dictionary<string, ComponentHealthDto>
        {
            ["SystemRuntime"] = new ComponentHealthDto
            {
                Status = "Healthy",
                Description = $".NET {Environment.Version} on {Environment.OSVersion.Platform}",
                Duration = TimeSpan.FromMilliseconds(2)
            },
            ["MemoryUsage"] = new ComponentHealthDto
            {
                Status = "Healthy",
                Description = $"Allocated: {process.WorkingSet64 / (1024 * 1024)} MB",
                Duration = TimeSpan.FromMilliseconds(1)
            },
            ["Uptime"] = new ComponentHealthDto
            {
                Status = "Healthy",
                Description = $"Active since {StartTimeUtc:u} ({(DateTime.UtcNow - StartTimeUtc).TotalMinutes:F1} mins)",
                Duration = TimeSpan.FromMilliseconds(1)
            }
        };

        sw.Stop();

        return Task.FromResult(new SystemHealthDto
        {
            Status = "Healthy",
            Environment = _environment.EnvironmentName,
            Version = "1.0.0",
            ServerTimeUtc = DateTime.UtcNow,
            Components = components
        });
    }

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public Task<bool> IsLiveAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}

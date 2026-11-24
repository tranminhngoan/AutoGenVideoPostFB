using Hangfire;
using Microsoft.AspNetCore.Mvc;
using NewsStudio.Api.Workers;
using NewsStudio.Application.Common.Interfaces;

namespace NewsStudio.Api.Controllers;

/// <summary>Who runs the background jobs right now (the owner's PC or Railway) — the badge in the web's sidebar.</summary>
[ApiController]
[Route("api/system/worker")]
[Produces("application/json")]
public class WorkerStatusController : ControllerBase
{
    private readonly IApplicationDbContext _db;
    private readonly IServiceProvider _services;

    public WorkerStatusController(IApplicationDbContext db, IServiceProvider services)
    {
        _db = db;
        _services = services;
    }

    public sealed record Side(bool Online, bool Working, string? Version, string? Machine, DateTime? HeartbeatUtc, List<string> RunningJobs);

    public sealed record WorkerStatus(string Worker, string WebVersion, Side Pc, Side Cloud, DateTime NowUtc);

    [HttpGet]
    public async Task<ActionResult<WorkerStatus>> Get(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var pc = await WorkerSwitchService.ReadBeatAsync(_db, WorkerSwitchService.PcKey, ct);
        var cloud = await WorkerSwitchService.ReadBeatAsync(_db, WorkerSwitchService.CloudKey, ct);

        var running = new List<(string Server, string Job)>();
        try
        {
            if (_services.GetService(typeof(JobStorage)) is JobStorage storage)
                running = storage.GetMonitoringApi().ProcessingJobs(0, 50)
                    .Select(j => (j.Value?.ServerId ?? "", j.Value?.Job?.Method.Name ?? "?"))
                    .ToList();
        }
        catch
        {
            // the badge still shows who works without the job list
        }

        Side Make(WorkerSwitchService.Beat? b, string tag) => new(
            b?.HeartbeatUtc is { } t && now - t < TimeSpan.FromSeconds(75),
            b?.Working == true,
            b?.Version,
            b?.Machine,
            b?.HeartbeatUtc,
            running.Where(r => r.Server.Contains(tag, StringComparison.OrdinalIgnoreCase)).Select(r => r.Job).ToList());

        return new WorkerStatus(
            WorkerSwitchService.WhoWorks(pc, cloud, now),
            WorkerSwitchService.Version(),
            Make(pc, "-pc-"),
            Make(cloud, "-cloud-"),
            now);
    }
}

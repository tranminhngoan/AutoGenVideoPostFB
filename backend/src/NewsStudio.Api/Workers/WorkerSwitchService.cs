using System.Text.Json;
using Hangfire;
using Hangfire.Client;
using Hangfire.Server;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Api.Workers;

/// <summary>
/// Runs the background jobs (renders, studio clips, auto-posting…) on ONE machine at a time:
/// <list type="bullet">
/// <item><b>pc</b> (Worker:Role=pc, the owner's computer): writes a heartbeat every 20 s and works — but only while it
/// runs the same version as the cloud server, so a stale copy never takes jobs it can't run;</item>
/// <item><b>cloud</b> (default, Railway): works while no current PC heartbeat exists; when the PC comes online it hands
/// over as soon as it is idle (no job is ever cut), and takes over again ~1 minute after the PC stops.</item>
/// </list>
/// Both share the database (jobs, data) and, for the video studio, the S3 bucket (clips, final videos).
/// </summary>
public class WorkerSwitchService : BackgroundService
{
    public const string PcKey = "Worker.Pc";
    public const string CloudKey = "Worker.Cloud";
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PcFresh = TimeSpan.FromSeconds(75);

    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WorkerSwitchService> _logger;
    private BackgroundJobServer? _server;
    private string? _lastNote;

    public WorkerSwitchService(IServiceProvider services, IConfiguration configuration, ILogger<WorkerSwitchService> logger)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
    }

    public sealed class Beat
    {
        public DateTime? HeartbeatUtc { get; set; }
        public string? Version { get; set; }
        public string? Machine { get; set; }
        public bool Working { get; set; }
        /// <summary>Last time the process was alive, even while it stays out of the work (e.g. older version).</summary>
        public DateTime? SeenUtc { get; set; }
    }

    public static bool Fresh(DateTime? t, DateTime nowUtc) => t is { } v && nowUtc - v < PcFresh;

    private bool IsPc => string.Equals(_configuration["Worker:Role"], "pc", StringComparison.OrdinalIgnoreCase);

    /// <summary>The code version this process runs (git commit; Railway provides it, the PC script passes it).</summary>
    public static string Version() =>
        Environment.GetEnvironmentVariable("APP_VERSION") is { Length: > 0 } v ? v
        : Environment.GetEnvironmentVariable("RAILWAY_GIT_COMMIT_SHA") is { Length: > 0 } r ? r
        : "dev";

    /// <summary>Whether the cloud server should leave the work to the PC.</summary>
    public static bool PcShouldWork(Beat? pc, string cloudVersion, DateTime nowUtc) =>
        pc?.HeartbeatUtc != null && nowUtc - pc.HeartbeatUtc.Value < PcFresh && pc.Version == cloudVersion;

    /// <summary>Who runs the background jobs now, for the web badge: "pc", "cloud", "both" (handing over) or "none".</summary>
    public static string WhoWorks(Beat? pc, Beat? cloud, DateTime nowUtc)
    {
        bool On(Beat? b) => b is { Working: true, HeartbeatUtc: { } t } && nowUtc - t < PcFresh;
        return (On(pc), On(cloud)) switch
        {
            (true, true) => "both",
            (true, false) => "pc",
            (false, true) => "cloud",
            _ => "none",
        };
    }

    public static async Task<Beat?> ReadBeatAsync(IApplicationDbContext db, string key, CancellationToken ct)
    {
        var raw = await db.SystemSettings.AsNoTracking().Where(s => s.SettingKey == key).Select(s => s.SettingValue).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return JsonSerializer.Deserialize<Beat>(raw); } catch (JsonException) { return null; }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker switch: role {Role}, version {Version}", IsPc ? "pc" : "cloud", Version());
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (IsPc) await PcTickAsync(stoppingToken);
                else await CloudTickAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Worker switch tick failed: {Message}", ex.Message);
                // can't read the database: the cloud keeps working, the PC stops (it can't prove it is the worker)
                if (IsPc) StopServer();
                else StartServer("không đọc được cơ sở dữ liệu — cloud tự làm");
            }
            try { await Task.Delay(Tick, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task CloudTickAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var pc = await ReadAsync(PcKey, ct);
        var handOver = PcShouldWork(pc, Version(), now);
        if (handOver && _server != null && !BusyHere()) StopServer("máy tính đang bật — nhường việc cho máy tính");
        else if (!handOver && _server == null) StartServer(pc?.HeartbeatUtc == null ? "chạy trên cloud" : "máy tính đã tắt — cloud nhận lại việc");
        await WriteAsync(CloudKey, new Beat { HeartbeatUtc = now, Version = Version(), Machine = Environment.MachineName, Working = _server != null }, ct);
    }

    private async Task PcTickAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var cloud = await ReadAsync(CloudKey, ct);
        // same code as the cloud (or the cloud hasn't been heard from for a long time): work; otherwise stay out
        var sameVersion = cloud?.Version == null || cloud.Version == Version()
                          || cloud.HeartbeatUtc == null || now - cloud.HeartbeatUtc.Value > TimeSpan.FromMinutes(10);
        if (!sameVersion)
        {
            StopServer($"máy tính chạy bản {Short(Version())}, cloud chạy bản {Short(cloud!.Version)} — cập nhật máy tính (chạy lại start-pc-worker)");
            await WriteAsync(PcKey, new Beat { HeartbeatUtc = null, Version = Version(), Machine = Environment.MachineName, SeenUtc = now }, ct);
            return;
        }
        StartServer("máy tính nhận việc");
        await WriteAsync(PcKey, new Beat { HeartbeatUtc = now, Version = Version(), Machine = Environment.MachineName, Working = true, SeenUtc = now }, ct);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (IsPc)
        {
            // hand back at once instead of after the heartbeat expires
            try { await WriteAsync(PcKey, new Beat { HeartbeatUtc = null, Version = Version(), Machine = Environment.MachineName }, cancellationToken); }
            catch (Exception ex) { _logger.LogInformation("Clearing the PC heartbeat failed: {Message}", ex.Message); }
        }
        StopServer("dừng chương trình");
        await base.StopAsync(cancellationToken);
    }

    private void StartServer(string reason)
    {
        if (_server != null) return;
        var options = new BackgroundJobServerOptions
        {
            WorkerCount = _configuration.GetValue<int>("Storage:MaxConcurrentRenderJobs", 2),
            ServerName = $"NewsStudio-{(IsPc ? "PC" : "Cloud")}-{Environment.MachineName}",
            Queues = new[] { "default", "critical", "render" }
        };
        _server = CreateServer(_services, options);
        Note($"Bắt đầu làm việc nền ({reason}).");
    }

    /// <summary>A Hangfire server wired like AddHangfireServer (jobs activated through dependency injection).</summary>
    public static BackgroundJobServer CreateServer(IServiceProvider services, BackgroundJobServerOptions options)
    {
        var storage = services.GetRequiredService<JobStorage>();
        var factory = services.GetService<IBackgroundJobFactory>();
        var performer = services.GetService<IBackgroundJobPerformer>();
        var stateChanger = services.GetService<IBackgroundJobStateChanger>();
        return factory != null && performer != null && stateChanger != null
            ? new BackgroundJobServer(options, storage, Array.Empty<IBackgroundProcess>(), null, null, factory, performer, stateChanger)
            : new BackgroundJobServer(options, storage);
    }

    private void StopServer(string? reason = null)
    {
        if (_server == null)
        {
            if (reason != null) Note($"Không làm việc nền: {reason}.");
            return;
        }
        var server = _server;
        _server = null;
        server.SendStop();
        server.Dispose();
        Note($"Ngừng làm việc nền: {reason}.");
    }

    /// <summary>A job of this server is running now (never cut it: hand over once idle).</summary>
    private bool BusyHere()
    {
        try
        {
            var name = $"NewsStudio-Cloud-{Environment.MachineName}";
            return JobStorage.Current.GetMonitoringApi().ProcessingJobs(0, 200)
                .Any(j => j.Value?.ServerId?.StartsWith(name, StringComparison.OrdinalIgnoreCase) == true);
        }
        catch
        {
            return true; // unsure: keep working
        }
    }

    private async Task<Beat?> ReadAsync(string key, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        return await ReadBeatAsync(scope.ServiceProvider.GetRequiredService<IApplicationDbContext>(), key, ct);
    }

    private async Task WriteAsync(string key, Beat beat, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var json = JsonSerializer.Serialize(beat);
        var row = await db.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == key, ct);
        if (row == null) db.SystemSettings.Add(new SystemSetting { SettingKey = key, SettingValue = json, Group = "Worker" });
        else row.SettingValue = json;
        await db.SaveChangesAsync(ct);
    }

    private void Note(string message)
    {
        if (message == _lastNote) return;
        _lastNote = message;
        _logger.LogInformation("Worker switch: {Message}", message);
    }

    private static string Short(string? v) => string.IsNullOrEmpty(v) ? "?" : v.Length > 7 ? v[..7] : v;
}

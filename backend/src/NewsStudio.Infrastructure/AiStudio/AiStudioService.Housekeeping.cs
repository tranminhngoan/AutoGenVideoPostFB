using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.DTOs;
using NewsStudio.Infrastructure.Studio;

namespace NewsStudio.Infrastructure.AiStudio;

public partial class AiStudioService
{
    // ───────────── settings / overview ─────────────

    public Task<AiStudioSettingsDto> GetSettingsAsync(CancellationToken ct) => AiStudioSettingsStore.GetAsync(_db, ct);

    public async Task<AiStudioSettingsDto> SaveSettingsAsync(AiStudioSettingsDto input, CancellationToken ct)
    {
        await AiStudioSettingsStore.SaveAsync(_db, input, ct);
        return await GetSettingsAsync(ct);
    }

    public async Task<AiStudioOverviewDto> OverviewAsync(bool pcOnline, bool pcUpdating, string? pcMachine, CancellationToken ct)
    {
        var projectBlobs = await _db.AiStudioProjects.AsNoTracking()
            .Select(p => new { p.SourceBlobId, p.SourceDeleted, p.SourceThumbBlobId, p.ReferenceImageBlobId, p.FinalBlobId }).ToListAsync(ct);
        var ids = new List<Guid>();
        foreach (var x in projectBlobs)
        {
            if (!x.SourceDeleted && x.SourceBlobId is { } s) ids.Add(s);
            foreach (var b in new[] { x.SourceThumbBlobId, x.ReferenceImageBlobId, x.FinalBlobId }) if (b is { } id) ids.Add(id);
        }
        ids.AddRange(await _db.AiStudioGenerations.AsNoTracking().Where(g => g.ClipBlobId != null && !g.FilesDeleted).Select(g => g.ClipBlobId!.Value).ToListAsync(ct));
        foreach (var frames in await _db.AiStudioScenes.AsNoTracking().Select(s => s.FramesJson).ToListAsync(ct))
            ids.AddRange(ReadFrames(frames).Where(f => f.BlobId != null).Select(f => f.BlobId!.Value));
        var bytes = ids.Count == 0 ? 0 : await _db.StudioBlobs.AsNoTracking().Where(b => ids.Contains(b.Id)).SumAsync(b => (long?)b.SizeBytes, ct) ?? 0;

        return new AiStudioOverviewDto
        {
            PcOnline = pcOnline, PcUpdating = pcUpdating, PcMachine = pcMachine,
            Engine = await AiStudioSettingsStore.GetEngineAsync(_db, ct),
            SeedanceConfigured = _video.IsConfigured,
            VideoModels = _video.Models().Select(m => new AiStudioVideoModelDto
            {
                Id = m.Id, Label = m.Label, Note = m.Note, PricePerSecondUsd = m.PricePerSecond.ToDictionary(k => k.Key, k => k.Value)
            }).ToList(),
            Topics = AiStudioTopics.All.Select(t => new AiStudioTopicDto { Id = t.Id, Label = t.Label, Description = t.Description }).ToList(),
            Voices = await NarratorVoicesAsync(ct),
            Resolutions = SeedanceVideoClient.Resolutions,
            MinClipSeconds = SeedanceVideoClient.MinSeconds,
            MaxClipSeconds = SeedanceVideoClient.MaxSeconds,
            Budget = await BudgetAsync(ct),
            Projects = await _db.AiStudioProjects.CountAsync(ct),
            Analyzing = await _db.AiStudioProjects.CountAsync(p => AnalysisRunning.Contains(p.Status), ct),
            Generating = await _db.AiStudioGenerations.CountAsync(g => GenRunning.Contains(g.Status), ct),
            SpentUsd = Math.Round(await _db.AiStudioGenerations.SumAsync(g => (decimal?)g.CostUsd, ct) ?? 0, 4),
            StorageMb = Math.Round(bytes / 1048576.0, 1)
        };
    }

    // ───────────── engine on the PC ─────────────

    /// <summary>On the PC: what is installed and how fast the local models answer (shown on the page).</summary>
    [Queue(PcQueue)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ProbeEngineAsync(bool bench, CancellationToken ct)
    {
        var settings = await AiStudioSettingsStore.GetAsync(_db, ct);
        var previous = await AiStudioSettingsStore.GetEngineAsync(_db, ct);
        var sameMachine = previous.Machine == Environment.MachineName;
        var status = new AiStudioEngineStatusDto
        {
            CheckedAtUtc = DateTime.UtcNow, Machine = Environment.MachineName, Home = _engine.Paths.Home, CpuThreads = Environment.ProcessorCount,
            RamGb = Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824.0, 1),
            TokensPerSecond = sameMachine ? previous.TokensPerSecond : new(), BenchBackend = sameMachine ? previous.BenchBackend : new(),
            Gpu = sameMachine ? previous.Gpu : null
        };
        try
        {
            var root = _engine.Paths.WorkRoot(settings);
            Directory.CreateDirectory(root);
            status.FreeDiskGb = Math.Round(new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace / 1073741824.0, 1);
            var probe = await _engine.CommandAsync(new[] { "probe", "--home", _engine.Paths.Home }, TimeSpan.FromMinutes(2), ct);
            status.Python = probe["python"]?.GetValue<string>();
            status.OpenCv = probe["opencv"]?.GetValue<string>();
            status.Backends = probe["backends"]?.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value?.GetValue<bool>() == true) ?? new();
            status.Models = probe["models"]?.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value?.GetValue<bool>() == true) ?? new();
            status.Whisper = probe["whisper"]?.GetValue<bool>() == true;
            status.Ready = probe["ok"]?.GetValue<bool>() == true;
            status.Error = probe["error"]?.GetValue<string>();
            // the speed test loads each model once (~10–40 s of GPU), only when asked or never measured, and never during an analysis
            if (status.Ready && AnalysisLock.Wait(0))
            {
                try
                {
                    foreach (var m in status.Models.Where(kv => kv.Value).Select(kv => kv.Key))
                    {
                        if (!bench && status.TokensPerSecond.ContainsKey(m)) continue;
                        var threads = settings.Threads > 0 ? settings.Threads : Math.Clamp(Environment.ProcessorCount - 2, 2, 14);
                        var b = await _engine.CommandAsync(new[] { "bench", "--home", _engine.Paths.Home, "--model", m, "--threads", threads.ToString(Inv), "--gpu", settings.Gpu },
                            TimeSpan.FromMinutes(6), ct);
                        status.TokensPerSecond[m] = b["genTokensPerSecond"]?.GetValue<double>() ?? 0;
                        status.BenchBackend[m] = b["backend"]?.GetValue<string>() ?? "";
                    }
                }
                finally
                {
                    AnalysisLock.Release();
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            status.Ready = false;
            status.Error = ex.Message;
        }
        await AiStudioSettingsStore.SaveEngineAsync(_db, status, CancellationToken.None);
    }

    public void RequestProbe(bool bench) => _jobs.Enqueue<AiStudioService>(s => s.ProbeEngineAsync(bench, CancellationToken.None));

    /// <summary>On the PC at start: work folders of analyses nobody resumed for 3 days.</summary>
    public void CleanLeftoverWork(AiStudioSettingsDto settings)
    {
        try
        {
            var root = _engine.Paths.WorkRoot(settings);
            if (!Directory.Exists(root)) return;
            foreach (var dir in Directory.GetDirectories(root))
                if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddDays(-3)) DeleteDir(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogInformation("AI Studio leftover cleanup: {Message}", ex.Message);
        }
    }

    // ───────────── housekeeping ─────────────

    /// <summary>
    /// Every hour (any machine): reference videos and generated clips past their retention leave the bucket (the
    /// analysis, prompts, keyframes and history stay), analyses / clips nobody reports on any more are failed.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task CleanupAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var settings = await AiStudioSettingsStore.GetAsync(_db, ct);
        var sourceCut = now.AddDays(-settings.SourceRetentionDays);
        foreach (var p in await _db.AiStudioProjects.Where(p => !p.SourceDeleted && p.SourceBlobId != null && p.CreatedAtUtc < sourceCut
                     && p.Status != "Queued" && p.Status != "Analyzing").ToListAsync(ct))
        {
            await DeleteBlobAsync(p.SourceBlobId, ct);
            p.SourceBlobId = null;
            p.SourceDeleted = true;
        }
        var clipCut = now.AddDays(-settings.ClipRetentionDays);
        foreach (var g in await _db.AiStudioGenerations.Where(g => !g.FilesDeleted && g.ClipBlobId != null && g.CreatedAtUtc < clipCut).ToListAsync(ct))
        {
            await DeleteBlobAsync(g.ClipBlobId, ct);
            g.ClipBlobId = null;
            g.FilesDeleted = true;
        }
        foreach (var p in await _db.AiStudioProjects.Where(p => p.SourceAudioBlobId != null && p.CreatedAtUtc < clipCut).ToListAsync(ct))
        {
            await DeleteBlobAsync(p.SourceAudioBlobId, ct);
            p.SourceAudioBlobId = null;
            if (p.AudioMode == "Original") p.AudioMode = "None";
        }
        foreach (var p in await _db.AiStudioProjects.Where(p => p.NarrationBlobId != null && p.CreatedAtUtc < clipCut).ToListAsync(ct))
        {
            await DeleteBlobAsync(p.NarrationBlobId, ct);
            (p.NarrationBlobId, p.NarrationSeconds, p.NarrationKey) = (null, null, null);
        }
        // per-scene takes of earlier versions: no longer used (the narration is read in one take)
        foreach (var sc in await _db.AiStudioScenes.Where(x => x.VoiceBlobId != null).ToListAsync(ct))
        {
            await DeleteBlobAsync(sc.VoiceBlobId, ct);
            (sc.VoiceBlobId, sc.VoiceSeconds, sc.VoiceKey) = (null, null, null);
        }
        foreach (var p in await _db.AiStudioProjects.Where(p => p.FinalBlobId != null && p.FinalUpdatedAtUtc < clipCut).ToListAsync(ct))
        {
            await DeleteBlobAsync(p.FinalBlobId, ct);
            p.FinalBlobId = null;
            p.FinalStatus = null;
        }
        var silent = now.AddHours(-2);
        foreach (var p in await _db.AiStudioProjects.Where(p => p.Status == "Analyzing" && (p.UpdatedAtUtc ?? p.CreatedAtUtc) < silent).ToListAsync(ct))
        {
            p.Status = "Failed";
            p.ErrorMessage = "Máy tính ngừng báo tiến độ hơn 2 giờ (tắt máy / mất mạng giữa chừng) — bấm Phân tích để chạy tiếp.";
        }
        var stuck = now.AddMinutes(-40);
        foreach (var g in await _db.AiStudioGenerations.Where(g => (g.Status == "Generating" || g.Status == "Recovering") && (g.UpdatedAtUtc ?? g.CreatedAtUtc) < stuck).ToListAsync(ct))
        {
            g.Status = "Failed";
            g.FinishedAtUtc = now;
            g.ErrorMessage = g.ProviderOperation != null
                ? "Mất theo dõi clip (máy chủ khởi động lại?) — bấm \"Lấy lại clip\" (miễn phí)."
                : "Mất theo dõi clip trước khi gửi Seedance — tạo lại.";
        }
        foreach (var p in await _db.AiStudioProjects.Where(p => p.FinalStatus == "Assembling" && (p.UpdatedAtUtc ?? p.CreatedAtUtc) < stuck).ToListAsync(ct))
        {
            p.FinalStatus = "Failed";
            p.FinalError = "Ghép video bị gián đoạn — bấm Ghép lại.";
        }
        await _db.SaveChangesAsync(ct);
    }
}

public static class AiStudioRegistration
{
    public static IServiceCollection AddAiStudioServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<AiStudioPaths>();
        services.AddSingleton<AiStudioMedia>();
        services.AddSingleton<AiStudioEngine>();
        services.AddScoped<IAiStudioNarrator, StudioVoicesNarrator>();
        services.AddScoped<AiStudioService>();
        if (string.Equals(configuration["Worker:Role"], "pc", StringComparison.OrdinalIgnoreCase))
            services.AddHostedService<AiStudioPcMonitor>();
        return services;
    }
}

/// <summary>Only on the owner's PC: reports the analyzer (libraries, models, speed) shortly after start and every 15 minutes.</summary>
public class AiStudioPcMonitor : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AiStudioPcMonitor> _logger;

    public AiStudioPcMonitor(IServiceProvider services, ILogger<AiStudioPcMonitor> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
        var first = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var studio = scope.ServiceProvider.GetRequiredService<AiStudioService>();
                if (first) studio.CleanLeftoverWork(await studio.GetSettingsAsync(stoppingToken));
                await studio.ProbeEngineAsync(false, stoppingToken);
                first = false;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("AI Studio engine check failed: {Message}", ex.Message);
            }
            try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}

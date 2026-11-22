using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Infrastructure.FilmFactory;

/// <summary>
/// The GPU backend new batches go to. Starts from Film:Gpu:Backend, but a choice made in the app (renting a Vast
/// machine, "Đổi ID/URL") is stored in SystemSettings and wins over Railway variables — so the API and the Worker
/// (both run the GPU tick) agree, and a restart keeps it. Batches already submitted keep talking to the backend
/// named in their reference ("vastai:…", "runpod:…"), so switching never orphans a running batch.
/// </summary>
public class ActiveFilmGpuBackend : IFilmGpuBackend
{
    public const string BackendKey = "Film.Gpu.Backend";
    public const string VastInstanceKey = "Film.Gpu.VastAi.InstanceId";
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(15);

    private readonly VastAiGpuBackend _vast;
    private readonly RunPodGpuBackend _runpod;
    private readonly IServiceScopeFactory? _scopes;
    private volatile IFilmGpuBackend _current;
    private DateTime _refreshedAt = DateTime.MinValue;

    public ActiveFilmGpuBackend(IFilmGpuBackend initial, VastAiGpuBackend vast, RunPodGpuBackend runpod, IServiceScopeFactory? scopes = null)
    {
        _current = initial;
        _vast = vast;
        _runpod = runpod;
        _scopes = scopes;
    }

    public IFilmGpuBackend Current => _current;
    public bool IsVast => _current is VastAiGpuBackend;

    /// <summary>Applies a stored choice. A key that was never stored leaves the configured default alone.</summary>
    public void Apply(string? backend, bool hasInstanceKey, string? vastInstance)
    {
        if (string.Equals(backend, "VastAi", StringComparison.OrdinalIgnoreCase)) _current = _vast;
        else if (string.Equals(backend, "RunPod", StringComparison.OrdinalIgnoreCase)) _current = _runpod;
        if (hasInstanceKey) VastAiGpuBackend.SetRuntimeInstance(vastInstance);
    }

    /// <summary>Re-reads the stored choice (at most every 15 s unless forced): another process may have changed it.</summary>
    public async Task RefreshAsync(CancellationToken ct = default, bool force = false)
    {
        if (_scopes == null || (!force && DateTime.UtcNow - _refreshedAt < RefreshEvery)) return;
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var rows = await db.SystemSettings.AsNoTracking()
            .Where(s => s.SettingKey == BackendKey || s.SettingKey == VastInstanceKey)
            .ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue, ct);
        Apply(rows.GetValueOrDefault(BackendKey), rows.ContainsKey(VastInstanceKey), NullIfEmpty(rows.GetValueOrDefault(VastInstanceKey)));
        _refreshedAt = DateTime.UtcNow;
    }

    /// <summary>After renting: Vast becomes the backend and this machine its target, for every process, across restarts.</summary>
    public async Task SelectVastAsync(string instanceId, CancellationToken ct = default)
    {
        await SaveAsync(BackendKey, "VastAi", ct);
        await SaveAsync(VastInstanceKey, instanceId, ct);
        Apply("VastAi", true, instanceId);
    }

    /// <summary>Points the Vast backend at another machine (null = whichever machine is running).</summary>
    public async Task SetVastInstanceAsync(string? instanceIdOrUrl, CancellationToken ct = default)
    {
        await SaveAsync(VastInstanceKey, instanceIdOrUrl ?? "", ct);
        Apply(null, true, NullIfEmpty(instanceIdOrUrl));
    }

    public string? StoredVastInstance => VastAiGpuBackend.GetRuntimeInstance();

    private async Task SaveAsync(string key, string value, CancellationToken ct)
    {
        if (_scopes == null) return;
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var row = await db.SystemSettings.FirstOrDefaultAsync(s => s.SettingKey == key, ct);
        if (row == null)
            db.SystemSettings.Add(new SystemSetting { SettingKey = key, SettingValue = value, Group = "FilmGpu" });
        else
        {
            row.SettingValue = value;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public bool IsConfigured => _current.IsConfigured;
    public string Name => _current.Name;

    public Task<string> SubmitAsync(string bundleDir, string batchId, CancellationToken ct) =>
        _current.SubmitAsync(bundleDir, batchId, ct);

    public Task<(GpuBatchState State, string Detail)> GetStateAsync(string reference, CancellationToken ct) =>
        For(reference).GetStateAsync(reference, ct);

    public Task DownloadAsync(string reference, string destDir, CancellationToken ct) =>
        For(reference).DownloadAsync(reference, destDir, ct);

    private IFilmGpuBackend For(string reference) =>
        reference.StartsWith("vastai:", StringComparison.OrdinalIgnoreCase) ? _vast
        : reference.StartsWith("runpod:", StringComparison.OrdinalIgnoreCase) ? _runpod
        : _current;
}

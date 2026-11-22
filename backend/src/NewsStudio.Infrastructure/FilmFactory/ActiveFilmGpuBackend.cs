namespace NewsStudio.Infrastructure.FilmFactory;

/// <summary>
/// The GPU backend new batches go to, switchable while the app runs (renting a Vast.ai machine from the app makes
/// Vast the active backend). Batches already submitted keep talking to the backend named in their reference
/// ("vastai:…", "runpod:…"), so switching never orphans a running batch. Starts from Film:Gpu:Backend.
/// </summary>
public class ActiveFilmGpuBackend : IFilmGpuBackend
{
    private readonly VastAiGpuBackend _vast;
    private readonly RunPodGpuBackend _runpod;
    private volatile IFilmGpuBackend _current;

    public ActiveFilmGpuBackend(IFilmGpuBackend initial, VastAiGpuBackend vast, RunPodGpuBackend runpod)
    {
        _current = initial;
        _vast = vast;
        _runpod = runpod;
    }

    public IFilmGpuBackend Current => _current;
    public bool IsVast => _current is VastAiGpuBackend;

    public void UseVast() => _current = _vast;

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

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NewsStudio.Infrastructure.FilmFactory;

public enum GpuBatchState { Queued, Running, Complete, Failed }

/// <summary>Where a batch of frame/clip jobs is executed. Kaggle (free) today; a rented GPU can implement the same contract.</summary>
public interface IFilmGpuBackend
{
    bool IsConfigured { get; }
    string Name { get; }
    /// <summary>Uploads the bundle folder (jobs.json + inputs) and starts the worker. Returns a reference for polling.</summary>
    Task<string> SubmitAsync(string bundleDir, string batchId, CancellationToken ct);
    Task<(GpuBatchState State, string Detail)> GetStateAsync(string reference, CancellationToken ct);
    /// <summary>Downloads the worker's output folder (results.json + files) into <paramref name="destDir"/>.</summary>
    Task DownloadAsync(string reference, string destDir, CancellationToken ct);
}

/// <summary>
/// Runs the GPU worker as a private Kaggle notebook on the free GPU quota (about 30 h/week).
/// The job bundle is uploaded as a new version of one private dataset, the notebook is pushed (which starts it),
/// and the notebook's output folder is downloaded when it completes. Uses the official <c>kaggle</c> CLI with
/// KAGGLE_USERNAME / KAGGLE_KEY (or ~/.kaggle/kaggle.json).
/// </summary>
public class KaggleGpuBackend : IFilmGpuBackend
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<KaggleGpuBackend> _logger;

    public KaggleGpuBackend(IConfiguration configuration, ILogger<KaggleGpuBackend> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public string Name => "Kaggle";

    private string Cli => _configuration["Film:Gpu:Kaggle:Cli"] ?? "kaggle";
    private string DatasetSlug => _configuration["Film:Gpu:Kaggle:Dataset"] ?? "film-gpu-jobs";
    private string KernelSlug => _configuration["Film:Gpu:Kaggle:Kernel"] ?? "film-gpu-worker";

    /// <summary>Kaggle user name from config/env or the CLI's kaggle.json.</summary>
    private string? Username
    {
        get
        {
            var u = _configuration["Film:Gpu:Kaggle:Username"] ?? Environment.GetEnvironmentVariable("KAGGLE_USERNAME");
            if (!string.IsNullOrWhiteSpace(u)) return u.Trim();
            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kaggle", "kaggle.json");
            if (!File.Exists(file)) return null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                return doc.RootElement.TryGetProperty("username", out var n) ? n.GetString() : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Username);

    public static string WorkerScriptPath => Path.Combine(AppContext.BaseDirectory, "FilmFactory", "GpuWorker", "film_gpu_worker.py");

    public async Task<string> SubmitAsync(string bundleDir, string batchId, CancellationToken ct)
    {
        var user = Username ?? throw new InvalidOperationException("Chưa cấu hình tài khoản Kaggle (KAGGLE_USERNAME / KAGGLE_KEY).");
        var datasetId = $"{user}/{DatasetSlug}";

        // 1. Upload the bundle as a new version of the private jobs dataset (created on first use).
        await File.WriteAllTextAsync(Path.Combine(bundleDir, "dataset-metadata.json"),
            JsonSerializer.Serialize(new { title = DatasetSlug, id = datasetId, licenses = new[] { new { name = "other" } } }), ct);
        var (code, output) = await RunAsync($"datasets version -p \"{bundleDir}\" -m \"{batchId}\" --dir-mode zip", ct);
        if (code != 0 || output.Contains("404") || output.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            (code, output) = await RunAsync($"datasets create -p \"{bundleDir}\" --dir-mode zip", ct);
            if (code != 0) throw new InvalidOperationException($"Kaggle: không tải được gói việc lên — {Tail(output)}");
        }

        // 2. Wait until Kaggle has processed the new version, so the notebook mounts this batch and not the previous one.
        for (var i = 0; ; i++)
        {
            var (_, status) = await RunAsync($"datasets status {datasetId}", ct);
            if (status.Contains("ready", StringComparison.OrdinalIgnoreCase)) break;
            if (i >= 60) throw new InvalidOperationException($"Kaggle: dataset chưa sẵn sàng sau 10 phút ({Tail(status)}).");
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }

        // 3. Push the worker notebook — pushing starts a new run on the free GPU.
        var kernelDir = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(bundleDir)!, $"kernel_{batchId}")).FullName;
        File.Copy(WorkerScriptPath, Path.Combine(kernelDir, "film_gpu_worker.py"), true);
        await File.WriteAllTextAsync(Path.Combine(kernelDir, "kernel-metadata.json"), JsonSerializer.Serialize(new
        {
            id = $"{user}/{KernelSlug}",
            title = KernelSlug,
            code_file = "film_gpu_worker.py",
            language = "python",
            kernel_type = "script",
            is_private = true,
            enable_gpu = true,
            enable_internet = true,
            dataset_sources = new[] { datasetId },
            competition_sources = Array.Empty<string>(),
            kernel_sources = Array.Empty<string>()
        }), ct);
        (code, output) = await RunAsync($"kernels push -p \"{kernelDir}\"", ct);
        var version = Regex.Match(output, @"version (\d+)");
        if (code != 0 || !version.Success) throw new InvalidOperationException($"Kaggle: không khởi động được notebook GPU — {Tail(output)}");

        _logger.LogInformation("Kaggle batch {Batch} started (kernel {User}/{Kernel} v{Version})", batchId, user, KernelSlug, version.Groups[1].Value);
        return $"kaggle:{batchId}:{version.Groups[1].Value}";
    }

    public async Task<(GpuBatchState State, string Detail)> GetStateAsync(string reference, CancellationToken ct)
    {
        var (_, output) = await RunAsync($"kernels status {Username}/{KernelSlug}", ct);
        var s = output.ToUpperInvariant();
        if (s.Contains("COMPLETE")) return (GpuBatchState.Complete, "complete");
        if (s.Contains("ERROR") || s.Contains("CANCEL")) return (GpuBatchState.Failed, Tail(output));
        if (s.Contains("RUNNING")) return (GpuBatchState.Running, "running");
        if (s.Contains("QUEUED")) return (GpuBatchState.Queued, "queued");
        return (GpuBatchState.Running, Tail(output)); // unknown answer (network blip): keep waiting
    }

    public async Task DownloadAsync(string reference, string destDir, CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);
        var (code, output) = await RunAsync($"kernels output {Username}/{KernelSlug} -p \"{destDir}\" --force", ct);
        if (code != 0) throw new InvalidOperationException($"Kaggle: không tải được kết quả — {Tail(output)}");
    }

    private async Task<(int Code, string Output)> RunAsync(string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Cli,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Không chạy được Kaggle CLI.");
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return (proc.ExitCode, (await stdout) + (await stderr));
    }

    private static string Tail(string s)
    {
        var t = (s ?? "").Trim();
        return t.Length > 400 ? "…" + t[^400..] : t;
    }
}

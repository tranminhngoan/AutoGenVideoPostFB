using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NewsStudio.Infrastructure.FilmFactory;

/// <summary>
/// Executes film generation batches on a rented GPU running on RunPod (e.g. RTX 3090 / RTX 4090 with 24 GB VRAM).
/// Communicates directly with the Python HTTP worker running inside the RunPod container.
/// </summary>
public class RunPodGpuBackend : IFilmGpuBackend
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<RunPodGpuBackend> _logger;

    public RunPodGpuBackend(IConfiguration configuration, HttpClient httpClient, ILogger<RunPodGpuBackend> logger)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _logger = logger;
    }

    public string Name => "RunPod";

    /// <summary>RunPod Pod ID (e.g. "p3k9xxxx") or environment variable RUNPOD_POD_ID.</summary>
    public string? PodId
    {
        get
        {
            var p = _configuration["Film:Gpu:RunPod:PodId"] ?? Environment.GetEnvironmentVariable("RUNPOD_POD_ID");
            return string.IsNullOrWhiteSpace(p) ? null : p.Trim();
        }
    }

    /// <summary>Optional RunPod API Key for authorization.</summary>
    public string? ApiKey
    {
        get
        {
            var k = _configuration["Film:Gpu:RunPod:ApiKey"] ?? Environment.GetEnvironmentVariable("RUNPOD_API_KEY");
            return string.IsNullOrWhiteSpace(k) ? null : k.Trim();
        }
    }

    /// <summary>Exposed port inside the RunPod pod (default 8000).</summary>
    public int Port => _configuration.GetValue("Film:Gpu:RunPod:Port", 8000);

    /// <summary>Explicit endpoint URL or automatic RunPod proxy URL.</summary>
    public string BaseUrl
    {
        get
        {
            var custom = _configuration["Film:Gpu:RunPod:EndpointUrl"] ?? Environment.GetEnvironmentVariable("RUNPOD_ENDPOINT_URL");
            if (!string.IsNullOrWhiteSpace(custom)) return custom.Trim().TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(PodId)) return $"https://{PodId}-{Port}.proxy.runpod.net";
            return string.Empty;
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);

    public async Task<string> SubmitAsync(string bundleDir, string batchId, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Chưa cấu hình thông tin GPU RunPod (cần RUNPOD_POD_ID hoặc Film:Gpu:RunPod:PodId).");

        var baseUrl = BaseUrl;
        _logger.LogInformation("Đang tải lô việc {BatchId} lên máy GPU RunPod ({Url})...", batchId, baseUrl);

        var tempZip = Path.Combine(Path.GetTempPath(), $"runpod_bundle_{batchId}_{Guid.NewGuid():N}.zip");
        try
        {
            if (File.Exists(tempZip)) File.Delete(tempZip);
            ZipFile.CreateFromDirectory(bundleDir, tempZip, CompressionLevel.Fastest, false);

            var fileInfo = new FileInfo(tempZip);
            _logger.LogInformation("Gói dữ liệu đã nén: {Size:N0} bytes. Đang truyền tải lên RunPod...", fileInfo.Length);

            await using var fs = File.OpenRead(tempZip);
            using var content = new StreamContent(fs);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

            var requestUri = $"{baseUrl}/submit?batch_id={Uri.EscapeDataString(batchId)}";
            using var req = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content };
            if (!string.IsNullOrWhiteSpace(ApiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            }

            var response = await _httpClient.SendAsync(req, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Máy GPU RunPod từ chối nhận việc (HTTP {(int)response.StatusCode}): {body}");
            }

            _logger.LogInformation("Lô việc {BatchId} đã được đưa vào hàng đợi máy GPU RunPod thành công.", batchId);
            return $"runpod:{batchId}";
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
        }
    }

    public async Task<(GpuBatchState State, string Detail)> GetStateAsync(string reference, CancellationToken ct)
    {
        var batchId = reference.StartsWith("runpod:", StringComparison.OrdinalIgnoreCase)
            ? reference["runpod:".Length..]
            : reference.Split(':').ElementAtOrDefault(1) ?? reference;

        var baseUrl = BaseUrl;
        var requestUri = $"{baseUrl}/status?batch_id={Uri.EscapeDataString(batchId)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        }

        try
        {
            var response = await _httpClient.SendAsync(req, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                return (GpuBatchState.Running, $"Chờ kết nối lại với RunPod (HTTP {(int)response.StatusCode}: {err})");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var stateStr = root.TryGetProperty("state", out var s) ? s.GetString()?.ToLowerInvariant() : "running";
            var detail = root.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "";

            return stateStr switch
            {
                "complete" or "done" or "success" => (GpuBatchState.Complete, detail),
                "failed" or "error" => (GpuBatchState.Failed, detail),
                "queued" => (GpuBatchState.Queued, detail),
                _ => (GpuBatchState.Running, detail)
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Mất kết nối tạm thời tới RunPod ({Url}), tiếp tục chờ...", baseUrl);
            return (GpuBatchState.Running, $"Mất kết nối tạm thời tới RunPod: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return (GpuBatchState.Running, $"RunPod phản hồi chậm (timeout): {ex.Message}");
        }
    }

    public async Task DownloadAsync(string reference, string destDir, CancellationToken ct)
    {
        var batchId = reference.StartsWith("runpod:", StringComparison.OrdinalIgnoreCase)
            ? reference["runpod:".Length..]
            : reference.Split(':').ElementAtOrDefault(1) ?? reference;

        var baseUrl = BaseUrl;
        Directory.CreateDirectory(destDir);
        var zipDest = Path.Combine(destDir, $"results_{batchId}.zip");

        var requestUri = $"{baseUrl}/download?batch_id={Uri.EscapeDataString(batchId)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        }

        _logger.LogInformation("Đang tải tệp kết quả từ RunPod cho lô {BatchId}...", batchId);
        using var response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Không thể tải kết quả từ RunPod (HTTP {(int)response.StatusCode}): {err}");
        }

        await using (var fs = File.Create(zipDest))
        {
            await response.Content.CopyToAsync(fs, ct);
        }

        // Giải nén gói kết quả vào destDir
        ZipFile.ExtractToDirectory(zipDest, destDir, true);
        try { File.Delete(zipDest); } catch { }

        _logger.LogInformation("Đã tải và giải nén thành công kết quả từ RunPod vào {DestDir}", destDir);
    }
}

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

    /// <summary>Exposed port inside the RunPod pod (defaults to 8888 for standard RunPod HTTP proxy).</summary>
    public int Port
    {
        get
        {
            var p = _configuration.GetValue<int?>("Film:Gpu:RunPod:Port") ??
                    (int.TryParse(Environment.GetEnvironmentVariable("RUNPOD_PORT"), out var envP) ? envP : 8888);
            if (p == 19123) return 8888; // 19123 là cổng của Web Terminal, tự động chuyển về 8888 để kết nối chính xác vào Film Worker
            return p;
        }
    }

    private static string? _runtimePodId;

    public static void SetRuntimePodId(string? podId)
    {
        if (string.IsNullOrWhiteSpace(podId))
        {
            _runtimePodId = null;
            return;
        }
        var trimmed = podId.Trim();
        if (trimmed.Contains(".proxy.runpod.net", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var host = trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? new Uri(trimmed).Host : trimmed;
                var parts = host.Split('.')[0].Split('-');
                if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                {
                    trimmed = parts[0];
                }
            }
            catch { }
        }
        _runtimePodId = trimmed;
    }

    public static string? GetRuntimePodId() => _runtimePodId;

    /// <summary>Explicit endpoint URL or automatic RunPod proxy URL.</summary>
    public string BaseUrl
    {
        get
        {
            var custom = _configuration["Film:Gpu:RunPod:EndpointUrl"] ?? Environment.GetEnvironmentVariable("RUNPOD_ENDPOINT_URL");
            if (!string.IsNullOrWhiteSpace(custom)) return custom.Trim().TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(_runtimePodId)) return $"https://{_runtimePodId}-{Port}.proxy.runpod.net";
            if (!string.IsNullOrWhiteSpace(_cachedDiscoveredPodId)) return $"https://{_cachedDiscoveredPodId}-{Port}.proxy.runpod.net";
            if (!string.IsNullOrWhiteSpace(PodId)) return $"https://{PodId}-{Port}.proxy.runpod.net";
            return string.Empty;
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(ApiKey) || !string.IsNullOrWhiteSpace(_runtimePodId);

    private string? _cachedDiscoveredPodId;
    private DateTimeOffset _cachedPodIdTime = DateTimeOffset.MinValue;

    public async Task<string> GetEffectiveBaseUrlAsync(CancellationToken ct = default)
    {
        var custom = _configuration["Film:Gpu:RunPod:EndpointUrl"] ?? Environment.GetEnvironmentVariable("RUNPOD_ENDPOINT_URL");
        if (!string.IsNullOrWhiteSpace(custom)) return custom.Trim().TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(_runtimePodId))
        {
            return $"https://{_runtimePodId}-{Port}.proxy.runpod.net";
        }

        // 1. Tự động phát hiện Pod đang RUNNING nếu có ApiKey (đặc biệt khi Pod bị đổi ID hoặc migrate)
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            if (!string.IsNullOrWhiteSpace(_cachedDiscoveredPodId) && DateTimeOffset.UtcNow - _cachedPodIdTime < TimeSpan.FromMinutes(2))
            {
                return $"https://{_cachedDiscoveredPodId}-{Port}.proxy.runpod.net";
            }

            var discovered = await DiscoverRunningPodIdAsync(ct);
            if (!string.IsNullOrWhiteSpace(discovered))
            {
                _cachedDiscoveredPodId = discovered;
                _cachedPodIdTime = DateTimeOffset.UtcNow;
                return $"https://{discovered}-{Port}.proxy.runpod.net";
            }
        }

        // 2. Dùng PodId cấu hình
        var podId = PodId;
        if (!string.IsNullOrWhiteSpace(podId))
        {
            return $"https://{podId}-{Port}.proxy.runpod.net";
        }

        return BaseUrl;
    }

    private async Task<string?> DiscoverRunningPodIdAsync(CancellationToken ct)
    {
        var apiKey = ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        try
        {
            var uri = $"https://api.runpod.io/graphql?api_key={Uri.EscapeDataString(apiKey)}";
            var req = new HttpRequestMessage(HttpMethod.Post, uri);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = System.Net.Http.Json.JsonContent.Create(new { query = "query { myself { pods { id name desiredStatus } } }" });

            var res = await _httpClient.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("RunPod GraphQL query failed with HTTP {StatusCode}", res.StatusCode);
                return null;
            }

            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("myself", out var myself) &&
                myself.TryGetProperty("pods", out var pods) &&
                pods.ValueKind == JsonValueKind.Array)
            {
                foreach (var pod in pods.EnumerateArray())
                {
                    var status = pod.TryGetProperty("desiredStatus", out var s) ? s.GetString() : null;
                    var id = pod.TryGetProperty("id", out var i) ? i.GetString() : null;
                    if (string.Equals(status, "RUNNING", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(id))
                    {
                        var podName = pod.TryGetProperty("name", out var n) ? n.GetString() : "";
                        _logger.LogInformation("Tự động phát hiện Pod đang chạy trên RunPod: {Id} ({Name})", id, podName);
                        return id;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể tự động truy vấn danh sách Pod từ RunPod GraphQL API.");
        }
        return null;
    }

    public async Task<string> SubmitAsync(string bundleDir, string batchId, CancellationToken ct)
    {
        var baseUrl = await GetEffectiveBaseUrlAsync(ct);
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Chưa phát hiện máy GPU RunPod nào đang chạy (cần bật RunPod hoặc cấu hình RUNPOD_POD_ID / RUNPOD_API_KEY).");

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
        if (string.IsNullOrWhiteSpace(reference))
        {
            return (GpuBatchState.Failed, "Reference batch rỗng.");
        }

        var batchId = reference.StartsWith("runpod:", StringComparison.OrdinalIgnoreCase)
            ? reference["runpod:".Length..]
            : reference.Split(':').ElementAtOrDefault(1) ?? reference;

        if (string.IsNullOrWhiteSpace(batchId))
        {
            return (GpuBatchState.Failed, "Batch ID rỗng.");
        }

        var baseUrl = await GetEffectiveBaseUrlAsync(ct);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Không tìm thấy kết nối tới máy GPU RunPod.");
        }
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
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return (GpuBatchState.Failed, $"Không tìm thấy batch trên GPU worker (HTTP 404): {err}");
                }
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
                "idle" or "not_found" => (GpuBatchState.Failed, string.IsNullOrWhiteSpace(detail) ? "Máy GPU RunPod đang rảnh (batch không tồn tại hoặc máy đã khởi động lại)" : detail),
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

        var baseUrl = await GetEffectiveBaseUrlAsync(ct);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Không tìm thấy kết nối tới máy GPU RunPod.");
        }
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

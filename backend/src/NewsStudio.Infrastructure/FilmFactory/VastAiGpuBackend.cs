using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NewsStudio.Infrastructure.FilmFactory;

/// <summary>
/// Executes film generation batches on rented GPUs running on Vast.ai (e.g. RTX 3090 / RTX 4090 / A100).
/// Vast.ai typically offers 50-80% lower hourly pricing than alternative cloud providers ($0.20 - $0.35/hr for 3090/4090).
/// Communicates directly with the Python HTTP worker running inside the Vast.ai container on port 8888.
/// </summary>
public partial class VastAiGpuBackend : IFilmGpuBackend
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<VastAiGpuBackend> _logger;

    public VastAiGpuBackend(IConfiguration configuration, HttpClient httpClient, ILogger<VastAiGpuBackend> logger)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _logger = logger;
    }

    public string Name => "VastAi";

    /// <summary>Vast.ai Instance ID (e.g. "1234567") or environment variable VAST_INSTANCE_ID.</summary>
    public string? InstanceId
    {
        get
        {
            var p = _configuration["Film:Gpu:VastAi:InstanceId"] ?? Environment.GetEnvironmentVariable("VAST_INSTANCE_ID");
            return string.IsNullOrWhiteSpace(p) ? null : p.Trim();
        }
    }

    /// <summary>Vast.ai API Key for instance lifecycle and auto-discovery.</summary>
    public string? ApiKey
    {
        get
        {
            var k = _configuration["Film:Gpu:VastAi:ApiKey"] ?? Environment.GetEnvironmentVariable("VAST_API_KEY");
            return string.IsNullOrWhiteSpace(k) ? null : k.Trim();
        }
    }

    /// <summary>Exposed internal container port (defaults to 8888 for Film Worker).</summary>
    public int Port
    {
        get
        {
            var p = _configuration.GetValue<int?>("Film:Gpu:VastAi:Port") ??
                    (int.TryParse(Environment.GetEnvironmentVariable("VAST_PORT"), out var envP) ? envP : 8888);
            return p;
        }
    }

    private static string? _runtimeInstance;

    public static void SetRuntimeInstance(string? instanceOrUrl)
    {
        if (string.IsNullOrWhiteSpace(instanceOrUrl))
        {
            _runtimeInstance = null;
            return;
        }
        _runtimeInstance = instanceOrUrl.Trim();
    }

    public static string? GetRuntimeInstance() => _runtimeInstance;

    /// <summary>Explicit endpoint URL configured manually (e.g. "http://185.123.45.67:32145").</summary>
    public string? ExplicitEndpointUrl
    {
        get
        {
            var custom = _configuration["Film:Gpu:VastAi:EndpointUrl"] ?? Environment.GetEnvironmentVariable("VAST_ENDPOINT_URL");
            return string.IsNullOrWhiteSpace(custom) ? null : custom.Trim().TrimEnd('/');
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ExplicitEndpointUrl) || !string.IsNullOrWhiteSpace(ApiKey) || !string.IsNullOrWhiteSpace(_runtimeInstance);

    private string? _cachedDiscoveredUrl;
    private DateTimeOffset _cachedUrlTime = DateTimeOffset.MinValue;

    public async Task<string> GetEffectiveBaseUrlAsync(CancellationToken ct = default)
    {
        // 1. Kiểm tra runtime override từ UI nếu người dùng nhập trực tiếp URL (http://...)
        if (!string.IsNullOrWhiteSpace(_runtimeInstance) && _runtimeInstance.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return _runtimeInstance.TrimEnd('/');
        }

        // 2. Kiểm tra endpoint cố định được cấu hình trong biến môi trường VAST_ENDPOINT_URL
        var explicitUrl = ExplicitEndpointUrl;
        if (!string.IsNullOrWhiteSpace(explicitUrl))
        {
            return explicitUrl;
        }

        // 3. Sử dụng cache URL nếu vừa phát hiện trong vòng 2 phút
        if (!string.IsNullOrWhiteSpace(_cachedDiscoveredUrl) && DateTimeOffset.UtcNow - _cachedUrlTime < TimeSpan.FromMinutes(2))
        {
            return _cachedDiscoveredUrl;
        }

        // 4. Tự động truy vấn Vast.ai REST API để lấy IP và Port được map ra ngoài
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            var discovered = await DiscoverInstanceEndpointAsync(ct);
            if (!string.IsNullOrWhiteSpace(discovered))
            {
                _cachedDiscoveredUrl = discovered;
                _cachedUrlTime = DateTimeOffset.UtcNow;
                return discovered;
            }
        }

        return _cachedDiscoveredUrl ?? string.Empty;
    }

    private async Task<string?> DiscoverInstanceEndpointAsync(CancellationToken ct)
    {
        var apiKey = ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var targetId = _runtimeInstance ?? InstanceId;

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "https://console.vast.ai/api/v0/instances/");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var res = await _httpClient.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Vast.ai API /instances/ trả về HTTP {StatusCode}", res.StatusCode);
                return null;
            }

            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            JsonElement instancesArray;
            if (doc.RootElement.TryGetProperty("instances", out var inst))
            {
                instancesArray = inst;
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                instancesArray = doc.RootElement;
            }
            else
            {
                return null;
            }

            foreach (var item in instancesArray.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idProp) ? idProp.ToString() : null;
                var actualStatus = item.TryGetProperty("actual_status", out var asProp) ? asProp.GetString() : null;
                var curState = item.TryGetProperty("cur_state", out var csProp) ? csProp.GetString() : null;

                bool isTarget = false;
                if (!string.IsNullOrWhiteSpace(targetId))
                {
                    isTarget = string.Equals(id, targetId, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    // Nếu không chỉ định ID cụ thể, chọn instance đầu tiên đang chạy
                    isTarget = string.Equals(actualStatus, "running", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(curState, "running", StringComparison.OrdinalIgnoreCase);
                }

                if (!isTarget) continue;

                var publicIp = item.TryGetProperty("public_ipaddr", out var ipProp) ? ipProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(publicIp)) continue;

                int hostPort = 0;
                // Trích xuất port mapping từ object "ports": {"8888/tcp": [{"HostIp": "0.0.0.0", "HostPort": "32145"}]}
                if (item.TryGetProperty("ports", out var portsObj) && portsObj.ValueKind == JsonValueKind.Object)
                {
                    var portKey = $"{Port}/tcp";
                    if (portsObj.TryGetProperty(portKey, out var mappingList) && mappingList.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in mappingList.EnumerateArray())
                        {
                            if (m.TryGetProperty("HostPort", out var hpProp))
                            {
                                if (hpProp.ValueKind == JsonValueKind.Number && hpProp.TryGetInt32(out var hpNum))
                                    hostPort = hpNum;
                                else if (int.TryParse(hpProp.GetString(), out var hpParsed))
                                    hostPort = hpParsed;
                                if (hostPort > 0) break;
                            }
                        }
                    }

                    // Nếu không tìm thấy đúng port 8888, lấy port đầu tiên được map
                    if (hostPort == 0)
                    {
                        foreach (var prop in portsObj.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var m in prop.Value.EnumerateArray())
                                {
                                    if (m.TryGetProperty("HostPort", out var hpProp))
                                    {
                                        if (hpProp.ValueKind == JsonValueKind.Number && hpProp.TryGetInt32(out var hpNum))
                                            hostPort = hpNum;
                                        else if (int.TryParse(hpProp.GetString(), out var hpParsed))
                                            hostPort = hpParsed;
                                        if (hostPort > 0) break;
                                    }
                                }
                            }
                            if (hostPort > 0) break;
                        }
                    }
                }

                // Fallback nếu không có ports map nhưng có direct_port_start
                if (hostPort == 0 && item.TryGetProperty("direct_port_start", out var dpStart) && dpStart.TryGetInt32(out var dps))
                {
                    hostPort = dps;
                }

                if (hostPort > 0)
                {
                    var endpoint = $"http://{publicIp}:{hostPort}";
                    _logger.LogInformation("Tự động phát hiện máy GPU Vast.ai: Instance {Id} ({Endpoint})", id, endpoint);
                    return endpoint;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi khi tự động truy vấn endpoint từ Vast.ai API.");
        }

        return null;
    }

    public async Task<(string? Id, string? Status, string? GpuName, double? DphTotal, string? PublicIp, int? HostPort)> GetInstanceLifecycleStatusAsync(
        string? targetInstanceId = null, CancellationToken ct = default)
    {
        var apiKey = ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return (null, null, null, null, null, null);

        var idToMatch = targetInstanceId ?? _runtimeInstance ?? InstanceId;

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "https://console.vast.ai/api/v0/instances/");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var res = await _httpClient.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return (null, null, null, null, null, null);

            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            JsonElement instancesArray;
            if (doc.RootElement.TryGetProperty("instances", out var inst))
                instancesArray = inst;
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
                instancesArray = doc.RootElement;
            else
                return (null, null, null, null, null, null);

            foreach (var item in instancesArray.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idProp) ? idProp.ToString() : null;
                var actualStatus = item.TryGetProperty("actual_status", out var asProp) ? asProp.GetString() : null;
                var curState = item.TryGetProperty("cur_state", out var csProp) ? csProp.GetString() : null;
                var gpuName = item.TryGetProperty("gpu_name", out var gnProp) ? gnProp.GetString() : null;
                var publicIp = item.TryGetProperty("public_ipaddr", out var ipProp) ? ipProp.GetString() : null;

                double? dphTotal = null;
                if (item.TryGetProperty("dph_total", out var dphProp) && dphProp.TryGetDouble(out var dph))
                {
                    dphTotal = dph;
                }

                int? hostPort = null;
                if (item.TryGetProperty("ports", out var portsObj) && portsObj.ValueKind == JsonValueKind.Object)
                {
                    var portKey = $"{Port}/tcp";
                    if (portsObj.TryGetProperty(portKey, out var mappingList) && mappingList.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in mappingList.EnumerateArray())
                        {
                            if (m.TryGetProperty("HostPort", out var hpProp))
                            {
                                if (hpProp.ValueKind == JsonValueKind.Number && hpProp.TryGetInt32(out var hpNum))
                                    hostPort = hpNum;
                                else if (int.TryParse(hpProp.GetString(), out var hpParsed))
                                    hostPort = hpParsed;
                                if (hostPort.HasValue) break;
                            }
                        }
                    }
                }

                var status = actualStatus ?? curState ?? "unknown";

                if (!string.IsNullOrWhiteSpace(idToMatch) && string.Equals(id, idToMatch, StringComparison.OrdinalIgnoreCase))
                {
                    return (id, status, gpuName, dphTotal, publicIp, hostPort);
                }

                if (string.IsNullOrWhiteSpace(idToMatch) && (string.Equals(status, "running", StringComparison.OrdinalIgnoreCase) || string.Equals(curState, "running", StringComparison.OrdinalIgnoreCase)))
                {
                    return (id, status, gpuName, dphTotal, publicIp, hostPort);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi kiểm tra trạng thái Instance từ Vast.ai API.");
        }

        return (null, null, null, null, null, null);
    }

    public async Task<(bool Success, string Message, string? DesiredStatus)> StopInstanceAsync(string? targetInstanceId = null, CancellationToken ct = default)
    {
        var apiKey = ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return (false, "Chưa cấu hình VAST_API_KEY.", null);

        var id = targetInstanceId ?? _runtimeInstance ?? InstanceId;
        if (string.IsNullOrWhiteSpace(id))
        {
            // Nếu chưa có ID cụ thể, tự tìm ID của instance đang chạy
            var (activeId, _, _, _, _, _) = await GetInstanceLifecycleStatusAsync(null, ct);
            id = activeId;
        }

        if (string.IsNullOrWhiteSpace(id)) return (false, "Chưa xác định được Instance ID trên Vast.ai để dừng.", null);

        try
        {
            var uri = $"https://console.vast.ai/api/v0/instances/{id}/";
            var req = new HttpRequestMessage(HttpMethod.Put, uri);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = System.Net.Http.Json.JsonContent.Create(new { state = "stopped" });

            var res = await _httpClient.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                return (false, $"Vast.ai API báo lỗi HTTP {(int)res.StatusCode}: {json}", null);
            }

            _logger.LogInformation("Đã gửi lệnh dừng Vast.ai Instance {Id} thành công.", id);
            return (true, $"Đã dừng máy GPU Vast.ai (#{id}) thành công. Tiền thuê máy đã ngừng tính!", "stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi dừng Vast.ai Instance {Id}", id);
            return (false, $"Lỗi kết nối tới Vast.ai: {ex.Message}", null);
        }
    }

    public async Task<(bool Success, string Message, string? DesiredStatus)> StartInstanceAsync(string? targetInstanceId = null, CancellationToken ct = default)
    {
        var apiKey = ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return (false, "Chưa cấu hình VAST_API_KEY.", null);

        var id = targetInstanceId ?? _runtimeInstance ?? InstanceId;
        if (string.IsNullOrWhiteSpace(id)) return (false, "Chưa xác định được Instance ID trên Vast.ai để bật.", null);

        try
        {
            var uri = $"https://console.vast.ai/api/v0/instances/{id}/";
            var req = new HttpRequestMessage(HttpMethod.Put, uri);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = System.Net.Http.Json.JsonContent.Create(new { state = "running" });

            var res = await _httpClient.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                return (false, $"Vast.ai API báo lỗi HTTP {(int)res.StatusCode}: {json}", null);
            }

            _logger.LogInformation("Đã gửi lệnh bật Vast.ai Instance {Id} thành công.", id);
            return (true, $"Đã bật lại máy GPU Vast.ai (#{id}) thành công. Máy đang khởi động...", "running");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi bật lại Vast.ai Instance {Id}", id);
            return (false, $"Lỗi kết nối tới Vast.ai: {ex.Message}", null);
        }
    }

    public async Task<string> SubmitAsync(string bundleDir, string batchId, CancellationToken ct)
    {
        var baseUrl = await GetEffectiveBaseUrlAsync(ct);
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Chưa phát hiện máy GPU Vast.ai nào đang chạy (cần bật Instance hoặc cấu hình VAST_API_KEY / VAST_ENDPOINT_URL).");

        _logger.LogInformation("Đang tải lô việc {BatchId} lên máy GPU Vast.ai ({Url})...", batchId, baseUrl);

        var tempZip = Path.Combine(Path.GetTempPath(), $"vast_bundle_{batchId}_{Guid.NewGuid():N}.zip");
        try
        {
            if (File.Exists(tempZip)) File.Delete(tempZip);
            ZipFile.CreateFromDirectory(bundleDir, tempZip, CompressionLevel.Fastest, false);

            var fileInfo = new FileInfo(tempZip);
            _logger.LogInformation("Gói dữ liệu đã nén: {Size:N0} bytes. Đang truyền tải lên Vast.ai...", fileInfo.Length);

            await using var fs = File.OpenRead(tempZip);
            using var content = new StreamContent(fs);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

            var requestUri = $"{baseUrl}/submit?batch_id={Uri.EscapeDataString(batchId)}";
            using var req = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content };

            var response = await _httpClient.SendAsync(req, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Máy GPU Vast.ai từ chối nhận việc (HTTP {(int)response.StatusCode}): {body}");
            }

            _logger.LogInformation("Lô việc {BatchId} đã được đưa vào hàng đợi máy GPU Vast.ai thành công.", batchId);
            return $"vastai:{batchId}";
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

        var batchId = reference.StartsWith("vastai:", StringComparison.OrdinalIgnoreCase)
            ? reference["vastai:".Length..]
            : reference.StartsWith("runpod:", StringComparison.OrdinalIgnoreCase)
                ? reference["runpod:".Length..]
                : reference.Split(':').ElementAtOrDefault(1) ?? reference;

        if (string.IsNullOrWhiteSpace(batchId))
        {
            return (GpuBatchState.Failed, "Batch ID rỗng.");
        }

        var baseUrl = await GetEffectiveBaseUrlAsync(ct);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Không tìm thấy kết nối tới máy GPU Vast.ai.");
        }
        var requestUri = $"{baseUrl}/status?batch_id={Uri.EscapeDataString(batchId)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);

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
                return (GpuBatchState.Running, $"Chờ kết nối lại với Vast.ai (HTTP {(int)response.StatusCode}: {err})");
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
                "idle" or "not_found" => (GpuBatchState.Failed, string.IsNullOrWhiteSpace(detail) ? "Máy GPU Vast.ai đang rảnh (batch không tồn tại hoặc máy đã khởi động lại)" : detail),
                _ => (GpuBatchState.Running, detail)
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Mất kết nối tạm thời tới Vast.ai ({Url}), tiếp tục chờ...", baseUrl);
            return (GpuBatchState.Running, $"Mất kết nối tạm thời tới Vast.ai: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return (GpuBatchState.Running, $"Vast.ai phản hồi chậm (timeout): {ex.Message}");
        }
    }

    public async Task DownloadAsync(string reference, string destDir, CancellationToken ct)
    {
        var batchId = reference.StartsWith("vastai:", StringComparison.OrdinalIgnoreCase)
            ? reference["vastai:".Length..]
            : reference.StartsWith("runpod:", StringComparison.OrdinalIgnoreCase)
                ? reference["runpod:".Length..]
                : reference.Split(':').ElementAtOrDefault(1) ?? reference;

        var baseUrl = await GetEffectiveBaseUrlAsync(ct);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Không tìm thấy kết nối tới máy GPU Vast.ai.");
        }
        Directory.CreateDirectory(destDir);
        var zipDest = Path.Combine(destDir, $"results_{batchId}.zip");

        var requestUri = $"{baseUrl}/download?batch_id={Uri.EscapeDataString(batchId)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);

        _logger.LogInformation("Đang tải tệp kết quả từ Vast.ai cho lô {BatchId}...", batchId);
        using var response = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Không thể tải kết quả từ Vast.ai (HTTP {(int)response.StatusCode}): {err}");
        }

        await using (var fs = File.Create(zipDest))
        {
            await response.Content.CopyToAsync(fs, ct);
        }

        // Giải nén gói kết quả vào destDir
        ZipFile.ExtractToDirectory(zipDest, destDir, true);
        try { File.Delete(zipDest); } catch { }

        _logger.LogInformation("Đã tải và giải nén thành công kết quả từ Vast.ai vào {DestDir}", destDir);
    }
}

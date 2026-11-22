using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NewsStudio.Infrastructure.FilmFactory;

/// <summary>What the user is looking for when renting a Vast.ai machine.</summary>
public record VastOfferQuery(
    IReadOnlyList<string> GpuNames,
    int DiskGb = 200,
    double? MaxPricePerHour = null,
    double? MaxDownloadCostPerGb = null,
    double MinReliability = 0.98,
    double MinCuda = 12.8,
    bool VerifiedOnly = true,
    int Limit = 10);

/// <summary>One rentable machine, priced for the requested disk size.</summary>
public record VastOffer(
    long Id, string GpuName, int NumGpus, double GpuRamGb, double CudaMax,
    double PricePerHour, double GpuPricePerHour, double StoragePerHour, double StoragePerMonth,
    double DownloadCostPerGb, double ModelDownloadCostUsd, double DownloadMbps, double UploadMbps,
    double Reliability, string? Location, string? CpuName, double CpuCores, double RamGb,
    double DiskAvailableGb, double DiskMbps, string? DiskName, double DlPerf, double MaxDurationDays,
    long MachineId, long HostId, string? Verification, int DirectPorts,
    /// <summary>One typical render session: SessionHours of GPU + downloading the models once.</summary>
    double SessionCostUsd);

/// <summary>An instance on the user's Vast.ai account.</summary>
public record VastInstanceInfo(
    string Id, string Status, string? IntendedStatus, string? GpuName, double? PricePerHour,
    double? StoragePerMonth, double? DiskGb, string? PublicIp, int? WorkerPort, string? SshCommand,
    string? Label, string? Location, DateTimeOffset? StartedAt);

/// <summary>Money on the Vast.ai account (only these numbers leave the server — never the key or email).</summary>
public record VastAccount(double? Credit, double? Balance, double? AutoBillingThreshold, bool AutoBillingEnabled);

public partial class VastAiGpuBackend
{
    /// <summary>Vast.ai's own page for adding credit (the user pays there; this app never charges anything).</summary>
    public const string BillingUrl = "https://cloud.vast.ai/billing/";

    /// <summary>GET /api/v0/users/current (no v1 yet). The response also holds api_key/email; only money fields are kept.</summary>
    public async Task<VastAccount> GetAccountAsync(CancellationToken ct = default)
    {
        var apiKey = ApiKey ?? throw new InvalidOperationException("Chưa cấu hình VAST_API_KEY trên server.");
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/users/current");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var res = await _httpClient.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Vast.ai không trả về số dư (HTTP {(int)res.StatusCode}).");
        return ParseAccount(json);
    }

    public static VastAccount ParseAccount(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var x = doc.RootElement;
        double? N(string name) => x.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.Number or JsonValueKind.String ? Num(x, name) : null;
        var enabled = x.TryGetProperty("balance_threshold_enabled", out var e) && e.ValueKind == JsonValueKind.True;
        return new VastAccount(N("credit"), N("balance"), N("balance_threshold"), enabled);
    }

    private const string ApiBase = "https://console.vast.ai/api/v0";

    /// <summary>
    /// What one fresh machine downloads before the first batch: Wan2.1-I2V-14B-480P-Diffusers 90.1 GB,
    /// RealVisXL fp16 6.9 GB, IP-Adapter ~3.4 GB, LatentSync 6.0 GB (Hugging Face sizes, 2026-11-22) + pip packages.
    /// </summary>
    public const double ModelDownloadGb = 110;

    /// <summary>Assumed GPU hours of one render session, used only to rank offers (price x hours + download).</summary>
    public const double SessionHours = 3;

    /// <summary>GPUs with enough VRAM for Wan 2.1 14B fully on the card (x86 only — GB10/GB200 are ARM).</summary>
    public static readonly IReadOnlyList<string> DefaultGpuNames = new[] { "RTX PRO 6000 WS", "RTX PRO 6000 Max-Q", "RTX PRO 6000 S" };

    /// <summary>Docker image for new machines; Vast picks the tag matching the host's CUDA driver.</summary>
    public string Image => _configuration["Film:Gpu:VastAi:Image"] ?? "vastai/pytorch:@vastai-automatic-tag";

    /// <summary>Public URL of this API, which new machines download start_vast.sh from.</summary>
    public string PublicApiBaseUrl => (_configuration["Film:Gpu:VastAi:PublicApiBaseUrl"] ?? "https://postnewsauto.up.railway.app").TrimEnd('/');

    /// <summary>Search body for POST /bundles/ (filters use Vast's {"field": {"op": value}} form).</summary>
    public static Dictionary<string, object> BuildSearchBody(VastOfferQuery q)
    {
        var body = new Dictionary<string, object>
        {
            ["gpu_name"] = new Dictionary<string, object> { ["in"] = (q.GpuNames.Count > 0 ? q.GpuNames : DefaultGpuNames).ToArray() },
            ["num_gpus"] = new Dictionary<string, object> { ["eq"] = 1 },
            ["rentable"] = new Dictionary<string, object> { ["eq"] = true },
            ["rented"] = new Dictionary<string, object> { ["eq"] = false },
            ["cpu_arch"] = new Dictionary<string, object> { ["eq"] = "amd64" },
            ["disk_space"] = new Dictionary<string, object> { ["gte"] = q.DiskGb },
            ["reliability2"] = new Dictionary<string, object> { ["gte"] = q.MinReliability },
            ["cuda_max_good"] = new Dictionary<string, object> { ["gte"] = q.MinCuda },
            // the worker's port 8888 is reached through a direct port mapping
            ["direct_port_count"] = new Dictionary<string, object> { ["gte"] = 2 },
            ["allocated_storage"] = q.DiskGb, // prices (dph_total, storage_total_cost) are returned for this disk size
            ["type"] = "ondemand",
            ["order"] = new[] { new[] { "dph_total", "asc" } },
            // fetch a wider pool; SearchOffersAsync re-ranks it by session cost and keeps q.Limit
            ["limit"] = Math.Clamp(q.Limit * 4, 20, 64)
        };
        if (q.VerifiedOnly) body["verified"] = new Dictionary<string, object> { ["eq"] = true };
        if (q.MaxPricePerHour is > 0) body["dph_total"] = new Dictionary<string, object> { ["lte"] = q.MaxPricePerHour.Value };
        if (q.MaxDownloadCostPerGb is >= 0) body["inet_down_cost"] = new Dictionary<string, object> { ["lte"] = q.MaxDownloadCostPerGb.Value };
        return body;
    }

    /// <summary>
    /// Maps the /bundles/ response. Units checked against live data: dph_total = $/h including storage for the
    /// requested disk; storage_cost = $/GB/month; inet_down_cost = $/GB; gpu_ram and cpu_ram in MB; duration in s.
    /// </summary>
    public static List<VastOffer> ParseOffers(string json, int diskGb)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var arr = root.ValueKind == JsonValueKind.Array ? root : root.TryGetProperty("offers", out var o) ? o : default;
        var list = new List<VastOffer>();
        if (arr.ValueKind != JsonValueKind.Array) return list;

        foreach (var x in arr.EnumerateArray())
        {
            var storageMonthPerGb = Num(x, "storage_cost");
            var downPerGb = Num(x, "inet_down_cost");
            list.Add(new VastOffer(
                Id: (long)Num(x, "id"),
                GpuName: Str(x, "gpu_name") ?? "?",
                NumGpus: (int)Num(x, "num_gpus"),
                GpuRamGb: Math.Round(Num(x, "gpu_ram") / 1024, 1),
                CudaMax: Num(x, "cuda_max_good"),
                PricePerHour: Math.Round(Num(x, "dph_total"), 4),
                GpuPricePerHour: Math.Round(Num(x, "dph_base"), 4),
                StoragePerHour: Math.Round(Num(x, "storage_total_cost"), 4),
                StoragePerMonth: Math.Round(storageMonthPerGb * diskGb, 2),
                DownloadCostPerGb: downPerGb,
                ModelDownloadCostUsd: Math.Round(downPerGb * ModelDownloadGb, 2),
                DownloadMbps: Math.Round(Num(x, "inet_down")),
                UploadMbps: Math.Round(Num(x, "inet_up")),
                Reliability: Math.Round(Num(x, "reliability2", Num(x, "reliability")), 4),
                Location: Str(x, "geolocation"),
                CpuName: Str(x, "cpu_name"),
                CpuCores: Num(x, "cpu_cores_effective"),
                RamGb: Math.Round(Num(x, "cpu_ram") / 1024),
                DiskAvailableGb: Math.Round(Num(x, "disk_space")),
                DiskMbps: Math.Round(Num(x, "disk_bw")),
                DiskName: Str(x, "disk_name"),
                DlPerf: Math.Round(Num(x, "dlperf"), 1),
                MaxDurationDays: Math.Round(Num(x, "duration") / 86400, 1),
                MachineId: (long)Num(x, "machine_id"),
                HostId: (long)Num(x, "host_id"),
                Verification: Str(x, "verification"),
                DirectPorts: (int)Num(x, "direct_port_count"),
                SessionCostUsd: Math.Round(Num(x, "dph_total") * SessionHours + downPerGb * ModelDownloadGb, 2)));
        }
        return list;
    }

    public async Task<List<VastOffer>> SearchOffersAsync(VastOfferQuery query, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/bundles/")
        {
            Content = System.Net.Http.Json.JsonContent.Create(BuildSearchBody(query))
        };
        if (!string.IsNullOrWhiteSpace(ApiKey)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        using var res = await _httpClient.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Vast.ai tìm máy lỗi HTTP {(int)res.StatusCode}: {Trim(json)}");
        // Cheapest per hour is not cheapest overall: some hosts charge ~$4 just to download the models.
        return ParseOffers(json, query.DiskGb)
            .OrderBy(o => o.SessionCostUsd).ThenByDescending(o => o.Reliability)
            .Take(Math.Clamp(query.Limit, 1, 30))
            .ToList();
    }

    /// <summary>
    /// Startup command for a new machine (Vast limit: 4048 chars): fetch start_vast.sh from this API, which installs
    /// the worker, registers it with supervisor and starts it on port 8888. Logs to /workspace/onstart.log.
    /// </summary>
    public string BuildOnStartScript() =>
        "env >> /etc/environment; mkdir -p /workspace; cd /workspace; " +
        $"curl -fsSL -m 60 \"{PublicApiBaseUrl}/api/film-factory/gpu/start-vast\" -o start_vast.sh " +
        "&& bash start_vast.sh 8888 > /workspace/onstart.log 2>&1";

    /// <summary>Rents an offer. Returns the new instance id. The account's SSH keys are added by Vast.</summary>
    public async Task<string> RentOfferAsync(long offerId, int diskGb, CancellationToken ct = default)
    {
        var apiKey = ApiKey ?? throw new InvalidOperationException("Chưa cấu hình VAST_API_KEY trên server — không thể thuê máy.");
        var body = new Dictionary<string, object>
        {
            ["client_id"] = "me",
            ["image"] = Image,
            ["disk"] = diskGb,
            ["label"] = "film-factory",
            ["runtype"] = "ssh_direct",
            ["env"] = new Dictionary<string, string> { [$"-p {Port}:{Port}"] = "1" },
            ["onstart"] = BuildOnStartScript(),
            ["cancel_unavail"] = true
        };
        using var req = new HttpRequestMessage(HttpMethod.Put, $"{ApiBase}/asks/{offerId}/")
        {
            Content = System.Net.Http.Json.JsonContent.Create(body)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var res = await _httpClient.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            if ((int)res.StatusCode == 410 || json.Contains("no_such_ask", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Máy này vừa bị người khác thuê mất — tìm lại và chọn máy khác.");
            throw new InvalidOperationException($"Vast.ai từ chối thuê máy (HTTP {(int)res.StatusCode}): {Trim(json)}");
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("new_contract", out var nc))
            throw new InvalidOperationException($"Vast.ai không trả về mã máy mới: {Trim(json)}");
        var id = nc.ToString();
        SetRuntimeInstance(id); // the worker of the new machine becomes the Film Factory's GPU
        _cachedDiscoveredUrl = null;
        _logger.LogInformation("Rented Vast.ai offer {Offer} as instance {Id} ({Disk} GB disk)", offerId, id, diskGb);
        return id;
    }

    public async Task<List<VastInstanceInfo>> ListInstancesAsync(CancellationToken ct = default)
    {
        var apiKey = ApiKey ?? throw new InvalidOperationException("Chưa cấu hình VAST_API_KEY trên server.");
        var (ok, json, status) = await FetchInstancesJsonAsync(apiKey, ct);
        if (!ok)
            throw new InvalidOperationException($"Vast.ai liệt kê máy lỗi HTTP {status}: {Trim(json)}");
        return ParseInstances(json, Port);
    }

    /// <summary>
    /// Lists the account's instances via GET /api/v1/instances/ (the v0 list answers 410 deprecated_endpoint),
    /// following next_token pages (25 per page), merged into {"instances": [...]} — the shape v0 returned.
    /// Single-instance calls (PUT state, DELETE) stay on /api/v0/instances/{id}/, as the official CLI does.
    /// </summary>
    private async Task<(bool Ok, string Json, int Status)> FetchInstancesJsonAsync(string apiKey, CancellationToken ct)
    {
        var items = new List<string>();
        string? token = null;
        for (var page = 0; page < 20; page++)
        {
            var url = "https://console.vast.ai/api/v1/instances/?limit=25" +
                      (token != null ? "&after_token=" + Uri.EscapeDataString(token) : "");
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var res = await _httpClient.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) return (false, body, (int)res.StatusCode);

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("instances", out var arr) && arr.ValueKind == JsonValueKind.Array)
                items.AddRange(arr.EnumerateArray().Select(x => x.GetRawText()));
            token = doc.RootElement.TryGetProperty("next_token", out var nt) && nt.ValueKind == JsonValueKind.String ? nt.GetString() : null;
            if (string.IsNullOrEmpty(token)) break;
        }
        return (true, "{\"instances\":[" + string.Join(",", items) + "]}", 200);
    }

    public static List<VastInstanceInfo> ParseInstances(string json, int workerPort)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var arr = root.ValueKind == JsonValueKind.Array ? root : root.TryGetProperty("instances", out var i) ? i : default;
        var list = new List<VastInstanceInfo>();
        if (arr.ValueKind != JsonValueKind.Array) return list;

        foreach (var x in arr.EnumerateArray())
        {
            var ip = Str(x, "public_ipaddr");
            int? MappedPort(int containerPort)
            {
                if (!x.TryGetProperty("ports", out var ports) || ports.ValueKind != JsonValueKind.Object) return null;
                if (!ports.TryGetProperty($"{containerPort}/tcp", out var maps) || maps.ValueKind != JsonValueKind.Array) return null;
                foreach (var m in maps.EnumerateArray())
                    if (m.TryGetProperty("HostPort", out var hp) && int.TryParse(hp.ToString(), out var p)) return p;
                return null;
            }

            var sshDirect = MappedPort(22);
            var sshHost = Str(x, "ssh_host");
            var sshPort = Num(x, "ssh_port");
            var ssh = sshDirect.HasValue && !string.IsNullOrWhiteSpace(ip) ? $"ssh -p {sshDirect} root@{ip}"
                : !string.IsNullOrWhiteSpace(sshHost) && sshPort > 0 ? $"ssh -p {sshPort:0} root@{sshHost}"
                : null;
            var disk = x.TryGetProperty("disk_space", out _) ? Num(x, "disk_space") : (double?)null;
            var storageGbMonth = x.TryGetProperty("storage_cost", out _) ? Num(x, "storage_cost") : (double?)null;
            var started = Num(x, "start_date");

            list.Add(new VastInstanceInfo(
                Id: x.TryGetProperty("id", out var id) ? id.ToString() : "?",
                Status: Str(x, "actual_status") ?? Str(x, "cur_state") ?? "unknown",
                IntendedStatus: Str(x, "intended_status"),
                GpuName: Str(x, "gpu_name"),
                PricePerHour: x.TryGetProperty("dph_total", out _) ? Math.Round(Num(x, "dph_total"), 4) : null,
                StoragePerMonth: disk.HasValue && storageGbMonth.HasValue ? Math.Round(disk.Value * storageGbMonth.Value, 2) : null,
                DiskGb: disk.HasValue ? Math.Round(disk.Value) : null,
                PublicIp: ip,
                WorkerPort: MappedPort(workerPort),
                SshCommand: ssh,
                Label: Str(x, "label"),
                Location: Str(x, "geolocation"),
                StartedAt: started > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)started) : null));
        }
        return list;
    }

    /// <summary>Destroys an instance (irreversible: its disk is deleted and storage billing stops).</summary>
    public async Task DestroyInstanceAsync(string instanceId, CancellationToken ct = default)
    {
        var apiKey = ApiKey ?? throw new InvalidOperationException("Chưa cấu hình VAST_API_KEY trên server.");
        if (!long.TryParse(instanceId, out _)) throw new ArgumentException("Mã máy Vast.ai không hợp lệ.");
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"{ApiBase}/instances/{instanceId}/");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var res = await _httpClient.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Vast.ai không xoá được máy #{instanceId} (HTTP {(int)res.StatusCode}): {Trim(json)}");

        if (string.Equals(GetRuntimeInstance(), instanceId, StringComparison.OrdinalIgnoreCase)) SetRuntimeInstance(null);
        _cachedDiscoveredUrl = null;
        _logger.LogInformation("Destroyed Vast.ai instance {Id}", instanceId);
    }

    private static bool ContainsInstance(JsonElement instances, string id) =>
        instances.EnumerateArray().Any(i => i.TryGetProperty("id", out var v) && string.Equals(v.ToString(), id, StringComparison.OrdinalIgnoreCase));

    private static double Num(JsonElement x, string name, double fallback = 0)
    {
        if (!x.TryGetProperty(name, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        return v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : fallback;
    }

    private static string? Str(JsonElement x, string name) =>
        x.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Trim(string s) => s.Length > 400 ? s[..400] + "…" : s;
}

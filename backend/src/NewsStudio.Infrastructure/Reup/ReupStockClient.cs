using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NewsStudio.Application.DTOs;

namespace NewsStudio.Infrastructure.Reup;

/// <summary>
/// Free stock search for the backdrop library — Pexels and Pixabay with the owner's own free API keys. Only searches;
/// a file is downloaded when the owner picks it (one at a time — no bulk downloading), with its author, page and
/// licence recorded. Every download (also "add from link") refuses private / local addresses.
/// </summary>
public class ReupStockClient
{
    public const string PexelsLicense = "Pexels License";
    public const string PexelsLicenseUrl = "https://www.pexels.com/license/";
    public const string PixabayLicense = "Pixabay Content License";
    public const string PixabayLicenseUrl = "https://pixabay.com/service/license-summary/";
    private const long MaxDownloadBytes = 400L * 1024 * 1024;

    private readonly HttpClient _http;

    public ReupStockClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<ReupStockItemDto>> SearchAsync(string provider, string kind, string query, string? pexelsKey, string? pixabayKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Nhập từ khoá tìm (tiếng Anh cho kết quả tốt nhất, ví dụ: beach, forest, cafe).");
        var video = string.Equals(kind, "Video", StringComparison.OrdinalIgnoreCase);
        var q = Uri.EscapeDataString(query.Trim());
        if (string.Equals(provider, "Pexels", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(pexelsKey)) throw new InvalidOperationException("Chưa có khoá Pexels (miễn phí tại pexels.com/api) — nhập trong Reup Video › Cài đặt.");
            var url = video ? $"https://api.pexels.com/videos/search?query={q}&orientation=portrait&per_page=24"
                            : $"https://api.pexels.com/v1/search?query={q}&orientation=portrait&per_page=24";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Authorization", pexelsKey.Trim());
            using var res = await _http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Pexels trả lỗi {(int)res.StatusCode} — kiểm tra khoá API.");
            return video ? ParsePexelsVideos(body) : ParsePexelsPhotos(body);
        }
        if (string.Equals(provider, "Pixabay", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(pixabayKey)) throw new InvalidOperationException("Chưa có khoá Pixabay (miễn phí tại pixabay.com/api/docs) — nhập trong Reup Video › Cài đặt.");
            var key = Uri.EscapeDataString(pixabayKey.Trim());
            var url = video ? $"https://pixabay.com/api/videos/?key={key}&q={q}&per_page=24&safesearch=true"
                            : $"https://pixabay.com/api/?key={key}&q={q}&image_type=photo&orientation=vertical&per_page=24&safesearch=true";
            using var res = await _http.GetAsync(url, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Pixabay trả lỗi {(int)res.StatusCode} — kiểm tra khoá API.");
            return video ? ParsePixabayVideos(body) : ParsePixabayPhotos(body);
        }
        throw new ArgumentException("Nguồn tìm kiếm phải là Pexels hoặc Pixabay.");
    }

    public static List<ReupStockItemDto> ParsePexelsPhotos(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ReupStockItemDto>();
        if (!doc.RootElement.TryGetProperty("photos", out var photos)) return list;
        foreach (var p in photos.EnumerateArray())
        {
            var src = p.GetProperty("src");
            list.Add(new ReupStockItemDto
            {
                Provider = "Pexels", Kind = "Image", Id = p.GetProperty("id").GetRawText(),
                PreviewUrl = Str(src, "medium") ?? Str(src, "small") ?? "",
                DownloadUrl = Str(src, "large2x") ?? Str(src, "original") ?? "",
                PageUrl = Str(p, "url") ?? "", Author = Str(p, "photographer"),
                Width = Int(p, "width"), Height = Int(p, "height")
            });
        }
        return list;
    }

    public static List<ReupStockItemDto> ParsePexelsVideos(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ReupStockItemDto>();
        if (!doc.RootElement.TryGetProperty("videos", out var videos)) return list;
        foreach (var v in videos.EnumerateArray())
        {
            // the file closest to 1080 px on its short side (never the 4K original: big and slow to process)
            var best = v.GetProperty("video_files").EnumerateArray()
                .Where(f => Str(f, "file_type") == "video/mp4" && Int(f, "width") > 0)
                .OrderBy(f => Math.Abs(Math.Min(Int(f, "width"), Int(f, "height")) - 1080))
                .FirstOrDefault();
            if (best.ValueKind == JsonValueKind.Undefined) continue;
            list.Add(new ReupStockItemDto
            {
                Provider = "Pexels", Kind = "Video", Id = v.GetProperty("id").GetRawText(),
                PreviewUrl = Str(v, "image") ?? "", DownloadUrl = Str(best, "link") ?? "",
                PageUrl = Str(v, "url") ?? "", Author = v.TryGetProperty("user", out var u) ? Str(u, "name") : null,
                Width = Int(best, "width"), Height = Int(best, "height"), DurationSeconds = Int(v, "duration")
            });
        }
        return list;
    }

    public static List<ReupStockItemDto> ParsePixabayPhotos(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ReupStockItemDto>();
        if (!doc.RootElement.TryGetProperty("hits", out var hits)) return list;
        foreach (var h in hits.EnumerateArray())
        {
            list.Add(new ReupStockItemDto
            {
                Provider = "Pixabay", Kind = "Image", Id = h.GetProperty("id").GetRawText(),
                PreviewUrl = Str(h, "webformatURL") ?? Str(h, "previewURL") ?? "",
                DownloadUrl = Str(h, "largeImageURL") ?? Str(h, "webformatURL") ?? "",
                PageUrl = Str(h, "pageURL") ?? "", Author = Str(h, "user"),
                Width = Int(h, "imageWidth"), Height = Int(h, "imageHeight")
            });
        }
        return list;
    }

    public static List<ReupStockItemDto> ParsePixabayVideos(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ReupStockItemDto>();
        if (!doc.RootElement.TryGetProperty("hits", out var hits)) return list;
        foreach (var h in hits.EnumerateArray())
        {
            if (!h.TryGetProperty("videos", out var vids)) continue;
            JsonElement? pick = null;
            foreach (var name in new[] { "large", "medium", "small" })
                if (vids.TryGetProperty(name, out var f) && !string.IsNullOrEmpty(Str(f, "url"))) { pick = f; break; }
            if (pick == null) continue;
            var f2 = pick.Value;
            list.Add(new ReupStockItemDto
            {
                Provider = "Pixabay", Kind = "Video", Id = h.GetProperty("id").GetRawText(),
                PreviewUrl = Str(f2, "thumbnail") ?? "", DownloadUrl = Str(f2, "url") ?? "",
                PageUrl = Str(h, "pageURL") ?? "", Author = Str(h, "user"),
                Width = Int(f2, "width"), Height = Int(f2, "height"), DurationSeconds = Int(h, "duration")
            });
        }
        return list;
    }

    /// <summary>The download link must point at the provider's own servers (the page can't slip in another address).</summary>
    public static bool IsProviderHost(string provider, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        var host = u.Host.ToLowerInvariant();
        bool Under(string domain) => host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
        return provider switch
        {
            "Pexels" => Under("pexels.com"),
            "Pixabay" => Under("pixabay.com"),
            _ => false
        };
    }

    /// <summary>Downloads one file to <paramref name="destination"/> (≤ 400 MB), following up to 3 redirects, never to a private / local address.</summary>
    public async Task DownloadAsync(string url, string destination, CancellationToken ct)
    {
        var current = await SafeUriAsync(url, ct);
        for (var hop = 0; hop < 4; hop++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, current);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; ReupVideo/1.0)");
            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)res.StatusCode is >= 300 and < 400 && res.Headers.Location is { } loc)
            {
                current = await SafeUriAsync(new Uri(current, loc).ToString(), ct);
                continue;
            }
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Không tải được file ({(int)res.StatusCode}).");
            if (res.Content.Headers.ContentLength > MaxDownloadBytes) throw new InvalidOperationException("File quá lớn (tối đa 400 MB).");
            await using var src = await res.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(destination);
            var buffer = new byte[1 << 16];
            long total = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                total += n;
                if (total > MaxDownloadBytes) throw new InvalidOperationException("File quá lớn (tối đa 400 MB).");
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            }
            return;
        }
        throw new InvalidOperationException("Link chuyển hướng quá nhiều lần.");
    }

    public static async Task<Uri> SafeUriAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("Link phải bắt đầu bằng http:// hoặc https://");
        var addresses = IPAddress.TryParse(u.Host, out var literal) ? new[] { literal } : await Dns.GetHostAddressesAsync(u.Host, ct);
        if (addresses.Length == 0 || addresses.Any(IsPrivate)) throw new ArgumentException("Link trỏ tới địa chỉ nội bộ — không được phép.");
        return u;
    }

    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6Multicast;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || b[0] == 127 || b[0] == 0
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168)
               || (b[0] == 169 && b[1] == 254)
               || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
               || b[0] >= 224;
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;
}

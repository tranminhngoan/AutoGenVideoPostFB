using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace NewsStudio.Infrastructure.Common;

/// <summary>
/// A small S3-compatible object store client (Cloudflare R2, Backblaze B2, AWS S3, MinIO…) with AWS Signature V4:
/// put / get / head / delete of whole objects in one bucket. It lets the cloud server and the owner's PC share the
/// studio's clips and videos, and keeps them across redeploys. Configured by Storage:S3:{ServiceUrl, Bucket,
/// AccessKey, SecretKey, Region} (Region "auto" for R2); absent → not used.
/// </summary>
public class S3Storage
{
    private readonly HttpClient _http;
    /// <summary>Big files (source videos up to ~600 MB over a home connection) get an hour, not the 5 minutes of small calls.</summary>
    private static readonly HttpClient FileHttp = new() { Timeout = TimeSpan.FromHours(1) };
    private readonly Uri _endpoint;
    private readonly string _bucket;
    private readonly string _accessKey;
    private readonly string _secretKey;
    private readonly string _region;

    private S3Storage(HttpClient http, Uri endpoint, string bucket, string accessKey, string secretKey, string region)
    {
        _http = http;
        _endpoint = endpoint;
        _bucket = bucket;
        _accessKey = accessKey;
        _secretKey = secretKey;
        _region = region;
    }

    /// <summary>The store from configuration, or null when it isn't configured.</summary>
    public static S3Storage? FromConfiguration(IConfiguration c, HttpClient? http = null)
    {
        string? Get(string key) => string.IsNullOrWhiteSpace(c[$"Storage:S3:{key}"]) ? null : c[$"Storage:S3:{key}"]!.Trim();
        var url = Get("ServiceUrl");
        var bucket = Get("Bucket");
        var access = Get("AccessKey");
        var secret = Get("SecretKey");
        if (url == null || bucket == null || access == null || secret == null) return null;
        return new S3Storage(http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) }, new Uri(url.TrimEnd('/') + "/"), bucket, access, secret, Get("Region") ?? "auto");
    }

    public async Task PutAsync(string key, byte[] data, string contentType, CancellationToken ct)
    {
        using var req = Request(HttpMethod.Put, key, data);
        req.Content = new ByteArrayContent(data);
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Kho file S3 không lưu được ({(int)res.StatusCode}): {await Short(res, ct)}");
    }

    /// <summary>
    /// Uploads a file straight from disk (big videos never sit whole in memory). The body is streamed with
    /// UNSIGNED-PAYLOAD (allowed over HTTPS by S3, R2 and B2), so the file is read only once.
    /// </summary>
    public async Task PutFileAsync(string key, string path, string contentType, CancellationToken ct)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var req = Request(HttpMethod.Put, key, null, UnsignedPayload);
        req.Content = new StreamContent(file, 1 << 16);
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        req.Content.Headers.ContentLength = file.Length;
        using var res = await FileHttp.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Kho file S3 không lưu được ({(int)res.StatusCode}): {await Short(res, ct)}");
    }

    /// <summary>Downloads an object into a file (streamed). False when it doesn't exist.</summary>
    public async Task<bool> GetToFileAsync(string key, string path, CancellationToken ct)
    {
        using var req = Request(HttpMethod.Get, key, null);
        using var res = await FileHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return false;
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Kho file S3 không đọc được ({(int)res.StatusCode}): {await Short(res, ct)}");
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..6] + ".part";
        try
        {
            await using (var src = await res.Content.ReadAsStreamAsync(ct))
            await using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous))
                await src.CopyToAsync(dst, 1 << 16, ct);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
        return true;
    }

    private const string UnsignedPayload = "UNSIGNED-PAYLOAD";

    /// <summary>The object's bytes, or null when it doesn't exist.</summary>
    public async Task<byte[]?> GetAsync(string key, CancellationToken ct)
    {
        using var req = Request(HttpMethod.Get, key, null);
        using var res = await _http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Kho file S3 không đọc được ({(int)res.StatusCode}): {await Short(res, ct)}");
        return await res.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        using var req = Request(HttpMethod.Head, key, null);
        using var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        using var req = Request(HttpMethod.Delete, key, null);
        using var res = await _http.SendAsync(req, ct);
    }

    private HttpRequestMessage Request(HttpMethod method, string key, byte[]? body, string? payloadHashOverride = null)
    {
        var path = "/" + Escape(_bucket) + "/" + string.Join("/", key.Split('/').Select(Escape));
        var uri = new Uri(_endpoint, path.TrimStart('/'));
        var req = new HttpRequestMessage(method, uri);
        var now = DateTime.UtcNow;
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var payloadHash = payloadHashOverride ?? Hex(SHA256.HashData(body ?? Array.Empty<byte>()));
        var host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = host,
            ["x-amz-content-sha256"] = payloadHash,
            ["x-amz-date"] = amzDate
        };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("AWS4-HMAC-SHA256",
            Sign(method.Method, uri.AbsolutePath, "", headers, payloadHash, now).Split(' ', 2)[1]);
        req.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        req.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        return req;
    }

    /// <summary>The Authorization header value (AWS Signature V4) for a request.</summary>
    public string Sign(string method, string canonicalPath, string canonicalQuery, IDictionary<string, string> headers, string payloadHash, DateTime utcNow)
    {
        var amzDate = utcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = utcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var sorted = headers.OrderBy(h => h.Key.ToLowerInvariant(), StringComparer.Ordinal).ToList();
        var canonicalHeaders = string.Concat(sorted.Select(h => $"{h.Key.ToLowerInvariant()}:{h.Value.Trim()}\n"));
        var signedHeaders = string.Join(";", sorted.Select(h => h.Key.ToLowerInvariant()));
        var canonicalRequest = $"{method}\n{canonicalPath}\n{canonicalQuery}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
        var scope = $"{date}/{_region}/s3/aws4_request";
        var stringToSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)))}";
        var key = Hmac(Hmac(Hmac(Hmac(Encoding.UTF8.GetBytes("AWS4" + _secretKey), date), _region), "s3"), "aws4_request");
        var signature = Hex(Hmac(key, stringToSign));
        return $"AWS4-HMAC-SHA256 Credential={_accessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}";
    }

    /// <summary>A store with fixed credentials (tests).</summary>
    public static S3Storage ForTest(string accessKey, string secretKey, string region) =>
        new(new HttpClient(), new Uri("https://example.com/"), "bucket", accessKey, secretKey, region);

    private static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private static string Escape(string segment) =>
        string.Concat(Encoding.UTF8.GetBytes(segment).Select(b =>
            (b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') || (b >= '0' && b <= '9') || b is (byte)'-' or (byte)'_' or (byte)'.' or (byte)'~'
                ? ((char)b).ToString() : "%" + b.ToString("X2")));

    private static async Task<string> Short(HttpResponseMessage res, CancellationToken ct)
    {
        var text = await res.Content.ReadAsStringAsync(ct);
        return text.Length > 300 ? text[..300] : text;
    }
}

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.DTOs;

namespace NewsStudio.Infrastructure.Rendering;

/// <summary>
/// Finds the picture of a named subject (person, product, place) the reliable way: the subject's
/// Wikidata item and its official image (P18) on Wikimedia Commons, with licence and author read from
/// Commons. Identity is decided by the Wikidata item (label/alias match; a person must be an item about a
/// human), never by searching pictures by name — so a different person can't slip in. Free, no API key.
/// </summary>
public class CommonsImageFinder
{
    private const string UserAgent = "TramTin247-NewsVideo/1.0 (editorial news videos; image credits shown on screen)";
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly ILogger<CommonsImageFinder> _logger;

    public CommonsImageFinder(HttpClient http, ILogger<CommonsImageFinder> logger)
    {
        _http = http;
        _logger = logger;
    }

    public sealed record CommonsImage(
        string Url, string FilePageUrl, string License, string? LicenseUrl, string? Author,
        string SubjectName, string WikidataId, double Confidence);

    public async Task<CommonsImage?> FindAsync(FactEntityDto entity, CancellationToken ct)
    {
        try
        {
            foreach (var lang in new[] { "vi", "en" })
            {
                var hit = await SearchAsync(entity.Name, lang, ct);
                if (hit == null) continue;
                var (id, label, confidence) = hit.Value;

                using var itemDoc = await GetJsonAsync($"https://www.wikidata.org/wiki/Special:EntityData/{id}.json", ct);
                if (itemDoc == null) continue;
                var claims = itemDoc.RootElement.GetProperty("entities").GetProperty(id).GetProperty("claims");
                // A person must be a Wikidata item about a human (Q5): no namesake buildings, films, ships…
                if (entity.Category == "A" && !ClaimIds(claims, "P31").Contains("Q5")) continue;
                var file = ClaimStrings(claims, "P18").FirstOrDefault();
                if (file == null) continue;

                var info = await FileInfoAsync(file, ct);
                if (info == null) continue;
                return info with { SubjectName = label, WikidataId = id, Confidence = confidence };
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException && !ct.IsCancellationRequested)
        {
            _logger.LogInformation("Commons lookup for {Name} failed: {Message}", entity.Name, ex.Message);
        }
        return null;
    }

    /// <summary>Best Wikidata item for the name: exact label (0.95), exact alias (0.85), or every word of a 2+-word name (0.75).</summary>
    private async Task<(string Id, string Label, double Confidence)?> SearchAsync(string name, string lang, CancellationToken ct)
    {
        var url = "https://www.wikidata.org/w/api.php?action=wbsearchentities&type=item&limit=5&format=json" +
                  $"&language={lang}&uselang={lang}&search={Uri.EscapeDataString(name)}";
        using var doc = await GetJsonAsync(url, ct);
        if (doc == null || !doc.RootElement.TryGetProperty("search", out var results)) return null;
        return BestMatch(name, results.EnumerateArray().Select(r => (
            Id: r.GetProperty("id").GetString()!,
            Label: r.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "",
            MatchType: r.TryGetProperty("match", out var m) && m.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
            MatchText: r.TryGetProperty("match", out var m2) && m2.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "")));
    }

    public static (string Id, string Label, double Confidence)? BestMatch(string name, IEnumerable<(string Id, string Label, string MatchType, string MatchText)> results)
    {
        var wanted = Fold(name);
        var words = wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var r in results)
        {
            var label = Fold(r.Label);
            if (label == wanted) return (r.Id, r.Label, 0.95);
            if (r.MatchType == "alias" && Fold(r.MatchText) == wanted) return (r.Id, r.Label, 0.85);
            if (words.Length >= 2 && words.All(w => label.Split(' ').Contains(w))) return (r.Id, r.Label, 0.75);
        }
        return null;
    }

    private async Task<CommonsImage?> FileInfoAsync(string file, CancellationToken ct)
    {
        var url = "https://commons.wikimedia.org/w/api.php?action=query&prop=imageinfo&iiprop=url|extmetadata&iiurlwidth=1280&format=json" +
                  $"&titles={Uri.EscapeDataString("File:" + file)}";
        using var doc = await GetJsonAsync(url, ct);
        if (doc == null) return null;
        foreach (var page in doc.RootElement.GetProperty("query").GetProperty("pages").EnumerateObject())
        {
            if (!page.Value.TryGetProperty("imageinfo", out var infos) || infos.GetArrayLength() == 0) continue;
            var info = infos[0];
            var meta = info.TryGetProperty("extmetadata", out var m) ? m : default;
            string? Meta(string key) => meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty(key, out var v) && v.TryGetProperty("value", out var val)
                ? WebUtility.HtmlDecode(Tags.Replace(val.GetString() ?? "", "")).Trim()
                : null;
            var license = Meta("LicenseShortName");
            // Only pictures Commons marks as free to reuse
            if (string.IsNullOrWhiteSpace(license) || !IsFreeLicense(license)) return null;
            var imageUrl = info.TryGetProperty("thumburl", out var thumb) ? thumb.GetString() : info.GetProperty("url").GetString();
            if (string.IsNullOrWhiteSpace(imageUrl)) return null;
            return new CommonsImage(imageUrl, info.GetProperty("descriptionurl").GetString() ?? "", license, Meta("LicenseUrl"), Meta("Artist"), "", "", 0);
        }
        return null;
    }

    public static bool IsFreeLicense(string license)
    {
        var l = license.ToLowerInvariant();
        return l.StartsWith("cc by") || l.StartsWith("cc0") || l.Contains("public domain") || l == "pd" || l.StartsWith("pd-");
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    private static IEnumerable<string> ClaimIds(JsonElement claims, string property) =>
        claims.TryGetProperty(property, out var list)
            ? list.EnumerateArray()
                .Select(c => c.GetProperty("mainsnak"))
                .Where(s => s.TryGetProperty("datavalue", out _))
                .Select(s => s.GetProperty("datavalue").GetProperty("value"))
                .Where(v => v.ValueKind == JsonValueKind.Object && v.TryGetProperty("id", out _))
                .Select(v => v.GetProperty("id").GetString()!)
            : Enumerable.Empty<string>();

    private static IEnumerable<string> ClaimStrings(JsonElement claims, string property) =>
        claims.TryGetProperty(property, out var list)
            ? list.EnumerateArray()
                .Select(c => c.GetProperty("mainsnak"))
                .Where(s => s.TryGetProperty("datavalue", out _))
                .Select(s => s.GetProperty("datavalue").GetProperty("value"))
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString()!)
            : Enumerable.Empty<string>();

    /// <summary>Lower case, no accents/punctuation: "Phó Đức Nam" → "pho duc nam".</summary>
    public static string Fold(string text)
    {
        var normalized = text.ToLowerInvariant().Replace('đ', 'd').Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return Regex.Replace(new string(chars.ToArray()), @"[^a-z0-9]+", " ").Trim();
    }
}

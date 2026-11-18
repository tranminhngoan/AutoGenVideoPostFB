using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using NewsStudio.Application.DTOs;

namespace NewsStudio.Infrastructure.Ai;

/// <summary>
/// Free (no AI call) checks of a script against its article:
/// <list type="bullet">
/// <item>every number spoken in the video must appear in the article;</item>
/// <item>every key claim must rest on a sentence that is really in the article;</item>
/// <item>a chart may only use numbers that are in the article.</item>
/// </list>
/// The subjects (A–E), claims and chart come from the same AI call that writes the script.
/// </summary>
public static class FactCheck
{
    private static readonly Regex NumberPattern = new(@"\d+(?:[.,]\d+)*", RegexOptions.Compiled);
    private static readonly Regex NonWord = new(@"[^\p{L}\p{Nd}]+", RegexOptions.Compiled);
    public static readonly string[] Categories = { "A", "B", "C", "D", "E" };

    /// <summary>Reads the "entities", "claims" and "chart" parts of the script JSON (missing parts → empty).</summary>
    public static FactSheetDto ParseFactSheet(JsonElement root)
    {
        var sheet = new FactSheetDto();
        if (root.TryGetProperty("entities", out var entities) && entities.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in entities.EnumerateArray())
            {
                var name = Str(e, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                var cat = (Str(e, "category") ?? "D").Trim().ToUpperInvariant();
                sheet.Entities.Add(new FactEntityDto
                {
                    Name = name.Trim(),
                    Category = Categories.Contains(cat) ? cat : "D",
                    Kind = Str(e, "kind")?.Trim()
                });
            }
        }
        if (root.TryGetProperty("claims", out var claims) && claims.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in claims.EnumerateArray())
            {
                var fact = Str(c, "fact");
                if (string.IsNullOrWhiteSpace(fact)) continue;
                sheet.Claims.Add(new FactClaimDto
                {
                    Fact = fact.Trim(),
                    Quote = Str(c, "quote")?.Trim(),
                    Certainty = string.Equals(Str(c, "certainty"), "unconfirmed", StringComparison.OrdinalIgnoreCase) ? "unconfirmed" : "confirmed"
                });
            }
        }
        if (root.TryGetProperty("imageQueries", out var queries) && queries.ValueKind == JsonValueKind.Array)
            sheet.ImageQueries = queries.EnumerateArray().Where(q => q.ValueKind == JsonValueKind.String)
                .Select(q => q.GetString()!.Trim()).Where(q => q.Length > 2).Take(4).ToList();
        if (root.TryGetProperty("chart", out var chart) && chart.ValueKind == JsonValueKind.Object
            && chart.TryGetProperty("points", out var pts) && pts.ValueKind == JsonValueKind.Array)
        {
            var dto = new FactChartDto { Title = Str(chart, "title") ?? "", Unit = Str(chart, "unit"), Source = Str(chart, "source") };
            foreach (var p in pts.EnumerateArray())
            {
                var label = Str(p, "label");
                if (label == null || !p.TryGetProperty("value", out var v)) continue;
                double value;
                if (v.ValueKind == JsonValueKind.Number) value = v.GetDouble();
                else if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString()?.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) value = parsed;
                else continue;
                dto.Points.Add(new FactChartPointDto { Label = label.Trim(), Value = value });
            }
            if (dto.Points.Count > 0) sheet.Chart = dto;
        }
        return sheet;
    }

    /// <summary>Runs the free checks and returns the completed fact sheet.</summary>
    public static FactSheetDto Check(FactSheetDto sheet, string voiceOver, string articleText)
    {
        var articleNumbers = NumberVariants(articleText);
        var normalizedArticle = Normalize(articleText);
        foreach (var claim in sheet.Claims)
            claim.QuoteFound = !string.IsNullOrWhiteSpace(claim.Quote) && QuoteInArticle(claim.Quote!, normalizedArticle);
        sheet.NumbersNotInArticle = NumbersNotIn(voiceOver, articleNumbers);
        if (sheet.Chart != null && !ChartIsFromArticle(sheet.Chart, articleNumbers)) sheet.Chart = null;
        sheet.CheckedAtUtc = DateTime.UtcNow;
        return sheet;
    }

    /// <summary>Numbers (10 and up) spoken in the script that the article doesn't contain.</summary>
    public static List<string> NumbersNotIn(string text, HashSet<string> articleNumbers)
    {
        var missing = new List<string>();
        foreach (Match m in NumberPattern.Matches(text))
        {
            var variants = Variants(m.Value);
            // Single digits are too common (and often written in words in the article) to judge
            if (variants.All(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d < 10)) continue;
            if (!variants.Any(articleNumbers.Contains) && !missing.Contains(m.Value)) missing.Add(m.Value);
        }
        return missing;
    }

    /// <summary>
    /// Numbers of the article. Besides each number as written, a number with several separators also gives
    /// its pieces and its decimal reading — "1.228,34 điểm" (Vietnamese decimal comma) is spoken as
    /// "1.228 phẩy 34", a date "17.11" as "ngày 17 tháng 11".
    /// </summary>
    public static HashSet<string> NumberVariants(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in NumberPattern.Matches(text))
        {
            foreach (var v in Variants(m.Value)) set.Add(v);
            var parts = m.Value.Split('.', ',');
            if (parts.Length < 2) continue;
            foreach (var part in parts)
                foreach (var v in Variants(part)) set.Add(v);
            // Mixed separators: the last one is the decimal mark ("1.228,34" → 1228.34, "1,228.34" → 1228.34)
            var lastSep = m.Value.LastIndexOfAny(new[] { '.', ',' });
            var whole = m.Value[..lastSep].Replace(".", "").Replace(",", "").TrimStart('0');
            var dec = m.Value[(lastSep + 1)..].TrimEnd('0');
            if (whole.Length > 0) set.Add(whole);
            if (whole.Length > 0 && dec.Length > 0) set.Add($"{whole}.{dec}");
        }
        return set;
    }

    private static readonly Regex MonthName = new(
        @"\b(Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sept?(?:ember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\b",
        RegexOptions.Compiled);

    private static readonly string[] MonthPrefixes = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    /// <summary>
    /// What the script's numbers are checked against: headline, lead and body, plus the publication date
    /// (day, month, year — the script may say "hôm nay 17/11") and English month names as numbers
    /// ("November" → 11, which a Vietnamese script says "tháng 11").
    /// </summary>
    public static string ReferenceText(string? title, string? description, string body, DateTime? publishedUtc)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(title).AppendLine(description).AppendLine(body);
        if (publishedUtc is { } p)
        {
            var vn = p.AddHours(7);
            sb.AppendLine($" {vn.Day} {vn.Month} {vn.Year} {p.Day} {p.Month} ");
        }
        var months = MonthName.Matches($"{title} {description} {body}")
            .Select(m => Array.FindIndex(MonthPrefixes, x => m.Value.StartsWith(x, StringComparison.Ordinal)) + 1)
            .Where(n => n > 0).Distinct();
        foreach (var n in months) sb.Append(' ').Append(n);
        return sb.ToString();
    }

    /// <summary>"1.000" / "1,000" → "1000"; "3,5" / "3.5" → "3.5" — both readings are kept when ambiguous.</summary>
    private static IEnumerable<string> Variants(string raw)
    {
        var digitsOnly = raw.Replace(".", "").Replace(",", "").TrimStart('0');
        if (digitsOnly.Length == 0) digitsOnly = "0";
        yield return digitsOnly;
        var parts = raw.Split('.', ',');
        if (parts.Length == 2)
        {
            var dec = $"{parts[0].TrimStart('0')}.{parts[1].TrimEnd('0')}".TrimEnd('.');
            if (dec.StartsWith('.')) dec = "0" + dec;
            yield return dec.Length == 0 ? "0" : dec;
        }
    }

    public static bool ChartIsFromArticle(FactChartDto chart, HashSet<string> articleNumbers)
    {
        if (chart.Points.Count < 3 || chart.Points.Count > 8) return false;
        return chart.Points.All(p =>
        {
            var plain = p.Value.ToString("0.####", CultureInfo.InvariantCulture);
            return articleNumbers.Contains(plain) || articleNumbers.Contains(plain.Replace(".", ""));
        });
    }

    public static string Normalize(string text) => NonWord.Replace(text.ToLowerInvariant(), " ").Trim();

    /// <summary>The quote is in the article (exactly, or 3/4 of its 4-word runs are — small edits by the AI are fine).</summary>
    public static bool QuoteInArticle(string quote, string normalizedArticle)
    {
        var q = Normalize(quote);
        if (q.Length == 0) return false;
        if (normalizedArticle.Contains(q, StringComparison.Ordinal)) return true;
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 4) return false;
        var grams = Enumerable.Range(0, words.Length - 3).Select(i => string.Join(' ', words, i, 4)).ToList();
        var found = grams.Count(g => normalizedArticle.Contains(g, StringComparison.Ordinal));
        return found >= Math.Ceiling(grams.Count * 0.75);
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

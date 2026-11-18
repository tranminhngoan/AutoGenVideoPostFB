using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace NewsStudio.Infrastructure.Common;

/// <summary>
/// Gemini API keys, used "free first, paid after":
/// <list type="bullet">
/// <item><c>Ai:Gemini:FreeApiKey</c> / <c>GEMINI_FREE_API_KEY</c> — key from a Google project WITHOUT billing (free tier, small quota).</item>
/// <item><c>Ai:Gemini:ApiKey</c> / <c>GEMINI_API_KEY</c> — key from the project with billing (charged per call).</item>
/// </list>
/// Every call tries the free key first; when Google refuses it (daily/minute quota used up, model not on the
/// free tier, key revoked) the same request is sent with the paid key. A free key that ran out "rests" until
/// its quota resets so later calls don't waste a round-trip on it.
/// </summary>
public static class GeminiKeys
{
    public sealed record Key(string Value, bool IsFree)
    {
        public string Label => IsFree ? "free" : "paid";
    }

    /// <summary>model (or "*" for the whole key) → time until which the free key is skipped.</summary>
    private static readonly ConcurrentDictionary<string, DateTimeOffset> FreeRestUntil = new();

    public static string? Paid(IConfiguration configuration) => Read(configuration, "Ai:Gemini:ApiKey", "GEMINI_API_KEY");

    public static string? Free(IConfiguration configuration) => Read(configuration, "Ai:Gemini:FreeApiKey", "GEMINI_FREE_API_KEY");

    /// <summary>
    /// A setting that is present but blank means "no key" and does NOT fall back to the machine-wide variable —
    /// the test host relies on this to keep billed Gemini calls out of test runs.
    /// </summary>
    private static string? Read(IConfiguration configuration, string settingKey, string environmentVariable)
    {
        var configured = configuration[settingKey];
        return configured != null ? NonEmpty(configured) : NonEmpty(Environment.GetEnvironmentVariable(environmentVariable));
    }

    public static bool HasAny(IConfiguration configuration) => Paid(configuration) != null || Free(configuration) != null;

    /// <summary>Key for read-only calls such as listing models (paid project sees every model).</summary>
    public static string? ForListing(IConfiguration configuration) => Paid(configuration) ?? Free(configuration);

    /// <summary>Keys to try, in order, for one request to <paramref name="model"/>.</summary>
    public static List<Key> InOrder(IConfiguration configuration, string model)
    {
        var free = Free(configuration);
        var paid = Paid(configuration);
        var keys = new List<Key>();
        if (free != null && free != paid && !IsResting(model)) keys.Add(new Key(free, true));
        if (paid != null) keys.Add(new Key(paid, false));
        // Only a free key configured: always try it — Google's own error then explains the limit.
        if (keys.Count == 0 && free != null) keys.Add(new Key(free, true));
        return keys;
    }

    /// <summary>
    /// Records that the free key was refused for <paramref name="model"/> so it is skipped for a while.
    /// Server errors (5xx) and timeouts don't rest the key — they are not quota problems.
    /// </summary>
    public static void MarkFreeRefused(string model, HttpStatusCode status, string reason)
    {
        var now = DateTimeOffset.UtcNow;
        var code = (int)status;
        if (code >= 500) return;

        if (code is 401 or 403)
        {
            FreeRestUntil["*"] = now.AddHours(1); // key invalid / project blocked
            return;
        }

        DateTimeOffset until;
        if (code == 429)
        {
            until = IsDailyQuota(reason) ? NextPacificMidnight(now) : now.Add(ParseRetryDelay(reason) ?? TimeSpan.FromMinutes(1));
        }
        else
        {
            until = now.AddMinutes(30); // e.g. model not offered on the free tier
        }
        FreeRestUntil[model] = until;
    }

    private static bool IsResting(string model)
    {
        var now = DateTimeOffset.UtcNow;
        return (FreeRestUntil.TryGetValue(model, out var m) && m > now)
               || (FreeRestUntil.TryGetValue("*", out var all) && all > now);
    }

    /// <summary>"retry in 37.5s" / "retryDelay": "37s" → 37.5 s.</summary>
    private static TimeSpan? ParseRetryDelay(string reason)
    {
        var match = Regex.Match(reason, @"(?:retry in|retryDelay""?\s*:\s*""?)\s*(\d+(?:\.\d+)?)s", RegexOptions.IgnoreCase);
        return match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 3600))
            : null;
    }

    /// <summary>
    /// A 429 that won't clear within minutes: the per-day quota ("PerDay" metric), or Google asking to retry
    /// hours later ("retry in 7h12m…").
    /// </summary>
    public static bool IsDailyQuota(string reason) =>
        Regex.IsMatch(reason, @"per[\s_-]?day|PerDay|daily|retry in \d+h", RegexOptions.IgnoreCase);

    /// <summary>Gemini daily quotas reset at midnight Pacific time.</summary>
    public static DateTimeOffset NextPacificMidnight(DateTimeOffset now)
    {
        TimeZoneInfo? zone = null;
        foreach (var id in new[] { "America/Los_Angeles", "Pacific Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out zone)) break;
        }
        var offset = zone?.GetUtcOffset(now) ?? TimeSpan.FromHours(-8);
        var local = now.ToOffset(offset);
        var midnight = new DateTimeOffset(local.Date.AddDays(1), offset);
        return midnight.AddMinutes(5);
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

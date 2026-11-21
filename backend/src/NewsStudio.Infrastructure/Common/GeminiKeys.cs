using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace NewsStudio.Infrastructure.Common;

/// <summary>
/// Gemini API keys, used "free first, paid after":
/// <list type="bullet">
/// <item><c>Ai:Gemini:FreeApiKey</c> / <c>GEMINI_FREE_API_KEY</c> — key from a Google project WITHOUT billing (free tier, small quota).
/// More free keys: <c>Ai:Gemini:FreeApiKey2</c>…<c>FreeApiKey9</c> / <c>GEMINI_FREE_API_KEY_2</c>…<c>_9</c>, or several keys in
/// one setting separated by commas.</item>
/// <item><c>Ai:Gemini:ApiKey</c> / <c>GEMINI_API_KEY</c> — key from the project with billing (charged per call).</item>
/// </list>
/// Every call tries the free keys first, one after another; when Google refuses one (daily/minute quota used
/// up, model not on the free tier, key revoked) the same request goes to the next free key, and only when
/// all of them are refused to the paid key. A free key that ran out "rests" until its quota resets so later
/// calls don't waste a round-trip on it.
/// </summary>
public static class GeminiKeys
{
    public const int MaxFreeKeys = 9;

    public sealed record Key(string Value, bool IsFree, int Slot = 1)
    {
        public string Label => IsFree ? (Slot > 1 ? $"free #{Slot}" : "free") : "paid";

        /// <summary>Identifies the key in the rest table without keeping the secret itself as the lookup key.</summary>
        internal string Id => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Value)))[..16];
    }

    /// <summary>"keyId|model" (or "keyId|*" for the whole key) → time until which that free key is skipped.</summary>
    private static readonly ConcurrentDictionary<string, DateTimeOffset> FreeRestUntil = new();

    public static string? Paid(IConfiguration configuration) => Read(configuration, "Ai:Gemini:ApiKey", "GEMINI_API_KEY");

    /// <summary>The first free key (or null).</summary>
    public static string? Free(IConfiguration configuration) => FreeKeys(configuration).FirstOrDefault();

    /// <summary>
    /// All free keys, in the order they are tried. When the main setting is present but blank (test host),
    /// the extra keys are only read from configuration, never from the machine's environment variables.
    /// </summary>
    public static List<string> FreeKeys(IConfiguration configuration)
    {
        var mainBlank = configuration["Ai:Gemini:FreeApiKey"] is { } m && string.IsNullOrWhiteSpace(m);
        var values = new List<string?> { Read(configuration, "Ai:Gemini:FreeApiKey", "GEMINI_FREE_API_KEY") };
        for (var i = 2; i <= MaxFreeKeys; i++)
        {
            values.Add(mainBlank
                ? NonEmpty(configuration[$"Ai:Gemini:FreeApiKey{i}"])
                : Read(configuration, $"Ai:Gemini:FreeApiKey{i}", $"GEMINI_FREE_API_KEY_{i}"));
        }
        var paid = Paid(configuration);
        return values
            .Where(v => v != null)
            .SelectMany(v => v!.Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(v => v != paid)
            .Distinct()
            .Take(MaxFreeKeys)
            .ToList();
    }

    /// <summary>
    /// A setting that is present but blank means "no key" and does NOT fall back to the machine-wide variable —
    /// the test host relies on this to keep billed Gemini calls out of test runs.
    /// </summary>
    private static string? Read(IConfiguration configuration, string settingKey, string environmentVariable)
    {
        var configured = configuration[settingKey];
        if (configured != null) return NonEmpty(configured);
        var env = Environment.GetEnvironmentVariable(environmentVariable);
        if (env != null) return NonEmpty(env);
        var altEnv = Environment.GetEnvironmentVariable(settingKey.Replace(":", "__"));
        return altEnv != null ? NonEmpty(altEnv) : null;
    }

    public static bool HasAny(IConfiguration configuration) => Paid(configuration) != null || FreeKeys(configuration).Count > 0;

    /// <summary>Key for read-only calls such as listing models (paid project sees every model).</summary>
    public static string? ForListing(IConfiguration configuration) => Paid(configuration) ?? Free(configuration);

    /// <summary>Keys to try, in order, for one request to <paramref name="model"/>: free keys that aren't resting, then the paid key.</summary>
    public static List<Key> InOrder(IConfiguration configuration, string model)
    {
        var free = FreeKeys(configuration).Select((v, i) => new Key(v, true, i + 1)).ToList();
        var paid = Paid(configuration);
        var keys = free.Where(k => !IsResting(k, model)).ToList();
        if (paid != null) keys.Add(new Key(paid, false));
        // Only free keys configured and all resting: still try the first — Google's own error then explains the limit.
        if (keys.Count == 0 && free.Count > 0) keys.Add(free[0]);
        return keys;
    }

    /// <summary>
    /// Records that the free key was refused for <paramref name="model"/> so it is skipped for a while.
    /// Server errors (5xx) and timeouts don't rest the key — they are not quota problems.
    /// </summary>
    public static void MarkFreeRefused(Key key, string model, HttpStatusCode status, string reason)
    {
        if (key.IsFree) MarkRefused(key.Id, model, status, reason);
    }

    /// <summary>
    /// Older form without the key: the rest applies to every free key (fine with a single free key).
    /// Prefer the overload taking the <see cref="Key"/>, which rests only the key Google refused.
    /// </summary>
    public static void MarkFreeRefused(string model, HttpStatusCode status, string reason) =>
        MarkRefused(LegacyId, model, status, reason);

    /// <summary>Rest entries recorded without knowing the key apply to every free key.</summary>
    private const string LegacyId = "any";

    private static void MarkRefused(string keyId, string model, HttpStatusCode status, string reason)
    {
        var now = DateTimeOffset.UtcNow;
        var code = (int)status;
        if (code >= 500) return;

        if (code is 401 or 403)
        {
            FreeRestUntil[$"{keyId}|*"] = now.AddHours(1); // key invalid / project blocked
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
        FreeRestUntil[$"{keyId}|{model}"] = until;
    }

    private static bool IsResting(Key key, string model)
    {
        var now = DateTimeOffset.UtcNow;
        bool Rest(string id) => (FreeRestUntil.TryGetValue($"{id}|{model}", out var m) && m > now)
                                || (FreeRestUntil.TryGetValue($"{id}|*", out var all) && all > now);
        return Rest(key.Id) || Rest(LegacyId);
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

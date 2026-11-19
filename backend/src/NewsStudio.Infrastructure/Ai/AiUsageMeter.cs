using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Infrastructure.Ai;

/// <summary>
/// Real AI spend: every Gemini call records the token counts Google returns (usageMetadata) and their price,
/// so the dashboard shows what the month actually costs, and the monthly budget can be enforced.
/// Calls made with the free key cost 0 (still counted). Prices are configurable (Ai:Pricing:*), USD per
/// 1M tokens.
/// </summary>
public class AiUsageMeter
{
    public const string BudgetKey = "ai-budget";
    public const string Warn = "warn";
    public const string EdgeVoice = "edge";
    public const string Pause = "pause";

    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiUsageMeter> _logger;
    private (DateTime At, AiBudgetStateDto State)? _cache;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public AiUsageMeter(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<AiUsageMeter> logger)
    {
        _scopes = scopes;
        _configuration = configuration;
        _logger = logger;
    }

    private decimal Price(string name, decimal fallback) =>
        decimal.TryParse(_configuration[$"Ai:Pricing:{name}"], NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>Cost of one call. Text: input + output (incl. thinking). Speech: text input + audio output.</summary>
    public decimal Cost(bool speech, long input, long output, bool free)
    {
        if (free) return 0;
        var perIn = speech ? Price("TtsInputPerM", 0.50m) : Price("TextInputPerM", 0.30m);
        var perOut = speech ? Price("TtsAudioPerM", 10.00m) : Price("TextOutputPerM", 2.50m);
        return Math.Round((input * perIn + output * perOut) / 1_000_000m, 6);
    }

    /// <summary>Records one call from Gemini's usageMetadata; never throws (metering must not break a video).</summary>
    public async Task RecordAsync(string kind, string model, bool freeKey, JsonElement? usage, bool speech = false, Guid? projectId = null, decimal? costOverrideUsd = null)
    {
        try
        {
            long Read(string name) => usage is { ValueKind: JsonValueKind.Object } u && u.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
            var input = Read("promptTokenCount");
            var output = Read("candidatesTokenCount") + Read("thoughtsTokenCount");
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.AiUsages.Add(new AiUsage
            {
                Kind = kind,
                Model = model.Length > 100 ? model[..100] : model,
                FreeKey = freeKey,
                InputTokens = input,
                OutputTokens = output,
                CostUsd = costOverrideUsd ?? Cost(speech, input, output, freeKey),
                VideoProjectId = projectId
            });
            await db.SaveChangesAsync();
            _cache = null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Recording AI usage failed: {Message}", ex.Message);
        }
    }

    public static DateTime MonthStartUtc(DateTime utc)
    {
        var vn = utc.AddHours(7);
        return new DateTime(vn.Year, vn.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(-7);
    }

    /// <summary>This month's spend against the budget (cached a few minutes).</summary>
    public async Task<AiBudgetStateDto> GetStateAsync(CancellationToken ct = default)
    {
        if (_cache is { } c && DateTime.UtcNow - c.At < TimeSpan.FromMinutes(3)) return c.State;
        await _cacheLock.WaitAsync(ct);
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var budget = await ReadBudgetAsync(db, ct);
            var since = MonthStartUtc(DateTime.UtcNow);
            var spent = await db.AiUsages.AsNoTracking().Where(u => u.CreatedAtUtc >= since).SumAsync(u => (decimal?)u.CostUsd, ct) ?? 0;
            var state = new AiBudgetStateDto
            {
                MonthlyUsd = budget.MonthlyUsd,
                Mode = budget.Mode,
                SpentUsd = Math.Round(spent, 4),
                OverBudget = budget.MonthlyUsd > 0 && spent >= budget.MonthlyUsd
            };
            _cache = (DateTime.UtcNow, state);
            return state;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<bool> ShouldPauseAsync(CancellationToken ct = default)
    {
        var s = await GetStateAsync(ct);
        return s.OverBudget && s.Mode == Pause;
    }

    public async Task<bool> ShouldUseFreeVoiceAsync(CancellationToken ct = default)
    {
        var s = await GetStateAsync(ct);
        return s.OverBudget && s.Mode == EdgeVoice;
    }

    public static async Task<AiBudgetSettingsDto> ReadBudgetAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var json = await db.SystemSettings.AsNoTracking().Where(s => s.SettingKey == BudgetKey).Select(s => s.SettingValue).FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var b = JsonSerializer.Deserialize<AiBudgetSettingsDto>(json);
                if (b != null) return Normalize(b);
            }
            catch (JsonException) { }
        }
        return new AiBudgetSettingsDto();
    }

    public static AiBudgetSettingsDto Normalize(AiBudgetSettingsDto b) => new()
    {
        MonthlyUsd = Math.Clamp(b.MonthlyUsd, 0, 10_000),
        Mode = b.Mode is Warn or EdgeVoice or Pause ? b.Mode : Warn
    };

    public void Invalidate() => _cache = null;
}

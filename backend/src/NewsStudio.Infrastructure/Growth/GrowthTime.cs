using System.Globalization;
using System.Text.RegularExpressions;

namespace NewsStudio.Infrastructure.Growth;

/// <summary>
/// Campaign time is Asia/Ho_Chi_Minh (UTC+7 all year, no daylight saving): days, posting slots and "today" are Vietnam
/// time; everything stored is UTC.
/// </summary>
public static partial class GrowthTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    public static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc) + Offset;

    public static DateOnly Today(DateTime utcNow) => DateOnly.FromDateTime(ToLocal(utcNow));

    public static DateOnly DateOf(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    /// <summary>Midnight (Vietnam) at the start of <paramref name="date"/>, as UTC.</summary>
    public static DateTime DayStartUtc(DateOnly date) => DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue) - Offset, DateTimeKind.Utc);

    /// <summary>The Vietnam time <paramref name="slot"/> ("HH:mm") on <paramref name="date"/>, as UTC.</summary>
    public static DateTime SlotUtc(DateOnly date, string slot)
    {
        var t = TimeOnly.ParseExact(slot, "HH:mm", CultureInfo.InvariantCulture);
        return DateTime.SpecifyKind(date.ToDateTime(t) - Offset, DateTimeKind.Utc);
    }

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex SlotPattern();

    public static bool IsSlot(string? s) => s != null && SlotPattern().IsMatch(s.Trim());

    /// <summary>Valid "HH:mm" slots, sorted, without duplicates; throws on a bad one.</summary>
    public static List<string> NormalizeSlots(IEnumerable<string>? slots)
    {
        var list = new List<string>();
        foreach (var raw in slots ?? Array.Empty<string>())
        {
            var s = (raw ?? "").Trim();
            if (s.Length == 0) continue;
            if (s.Length == 4 && s[1] == ':') s = "0" + s; // 7:30 → 07:30
            if (!IsSlot(s)) throw new ArgumentException($"Khung giờ \"{raw}\" không hợp lệ (dạng HH:mm, ví dụ 07:30).");
            if (!list.Contains(s)) list.Add(s);
        }
        if (list.Count > 12) throw new ArgumentException("Tối đa 12 khung giờ đăng mỗi ngày.");
        return list.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    public static List<string> ParseSlots(string? stored) =>
        (stored ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(IsSlot).ToList();

    /// <summary>1-based day of a campaign on <paramref name="today"/> (0 before it starts, total+1 after it ends).</summary>
    public static int DayNumber(DateOnly start, DateOnly end, DateOnly today)
    {
        if (today < start) return 0;
        var total = end.DayNumber - start.DayNumber + 1;
        var n = today.DayNumber - start.DayNumber + 1;
        return n > total ? total + 1 : n;
    }

    /// <summary>UTC instant after which nothing of the campaign may be posted (end of its last Vietnam day).</summary>
    public static DateTime EndUtc(DateOnly end) => DayStartUtc(end.AddDays(1));
}

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NewsStudio.Application.Common.Interfaces;
using NewsStudio.Application.DTOs;
using NewsStudio.Domain.Entities;

namespace NewsStudio.Infrastructure.Outreach;

/// <summary>
/// The campaign menu's own lock, checked on the server:
/// - password kept only as a PBKDF2-SHA256 hash (database row after a change, else configuration "Outreach:PasswordHash");
/// - a correct password gives a signed session token that expires (HMAC over expiry + version; the key is derived
///   from the stored hash, so changing the password ends every session);
/// - 5 wrong tries from one address in 15 minutes lock that address for 15 minutes (20 from anywhere lock everyone).
/// </summary>
public class OutreachAuth
{
    public const int MaxAttempts = 5;
    public const int LockMinutes = 15;
    public const int GlobalMaxFailures = 20;
    private const int Iterations = 210_000;
    public const string SessionHeader = "X-Outreach-Session";

    private readonly IApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<OutreachAuth> _logger;

    public OutreachAuth(IApplicationDbContext db, IConfiguration config, ILogger<OutreachAuth> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    // ───────────── hashing ─────────────

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string? stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(stored)) return false;
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations) || iterations < 10_000) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // ───────────── settings ─────────────

    private async Task<OutreachSettings?> RowAsync(CancellationToken ct) =>
        await _db.OutreachSettings.OrderBy(s => s.CreatedAtUtc).FirstOrDefaultAsync(ct);

    private async Task<(string? Hash, string Source, int Hours, int Version)> CurrentAsync(CancellationToken ct)
    {
        var row = await RowAsync(ct);
        var configHash = _config["Outreach:PasswordHash"];
        var hours = Math.Clamp(row?.SessionHours ?? _config.GetValue("Outreach:SessionHours", 8), 1, 72);
        if (!string.IsNullOrWhiteSpace(row?.PasswordHash)) return (row.PasswordHash, "database", hours, row.SessionVersion);
        return (string.IsNullOrWhiteSpace(configHash) ? null : configHash.Trim(), "config", hours, row?.SessionVersion ?? 1);
    }

    public async Task<OutreachSecurityStatusDto> StatusAsync(bool withLog, CancellationToken ct)
    {
        var (hash, source, hours, _) = await CurrentAsync(ct);
        var dto = new OutreachSecurityStatusDto
        {
            PasswordConfigured = hash != null,
            PasswordSource = source,
            SessionHours = hours,
            MaxAttempts = MaxAttempts,
            LockMinutes = LockMinutes,
        };
        if (withLog)
            dto.RecentAttempts = await _db.OutreachUnlockAttempts.AsNoTracking()
                .OrderByDescending(a => a.CreatedAtUtc).Take(30)
                .Select(a => new OutreachAttemptDto { AtUtc = a.CreatedAtUtc, Ip = a.Ip, Success = a.Success })
                .ToListAsync(ct);
        return dto;
    }

    // ───────────── unlock / sessions ─────────────

    public async Task<OutreachUnlockResultDto> UnlockAsync(string? password, string ip, CancellationToken ct)
    {
        ip = string.IsNullOrWhiteSpace(ip) ? "unknown" : ip[..Math.Min(ip.Length, 64)];
        var now = DateTime.UtcNow;
        var window = now.AddMinutes(-LockMinutes);

        var recent = await _db.OutreachUnlockAttempts.AsNoTracking()
            .Where(a => a.CreatedAtUtc >= window)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(ct);
        // Failures since this address' last success
        var lastOk = recent.LastOrDefault(a => a.Ip == ip && a.Success)?.CreatedAtUtc ?? DateTime.MinValue;
        var mine = recent.Where(a => a.Ip == ip && !a.Success && a.CreatedAtUtc > lastOk).ToList();
        var everyone = recent.Count(a => !a.Success);
        if (mine.Count >= MaxAttempts || everyone >= GlobalMaxFailures)
        {
            var from = mine.Count >= MaxAttempts ? mine[^MaxAttempts].CreatedAtUtc : recent.Where(a => !a.Success).Skip(everyone - GlobalMaxFailures).First().CreatedAtUtc;
            _logger.LogWarning("Outreach unlock refused for {Ip}: locked ({Mine} / {All} failures)", ip, mine.Count, everyone);
            return new OutreachUnlockResultDto { Ok = false, Error = "Nhập sai quá nhiều lần. Tạm khoá, thử lại sau.", LockedUntilUtc = from.AddMinutes(LockMinutes) };
        }

        var (hash, _, hours, version) = await CurrentAsync(ct);
        if (hash == null)
            return new OutreachUnlockResultDto { Ok = false, Error = "Chưa cấu hình mật khẩu chiến dịch (Outreach__PasswordHash)." };

        var ok = VerifyPassword(password ?? "", hash);
        _db.OutreachUnlockAttempts.Add(new OutreachUnlockAttempt { Ip = ip, Success = ok });
        await _db.SaveChangesAsync(ct);

        if (!ok)
        {
            var left = MaxAttempts - mine.Count - 1;
            _logger.LogWarning("Outreach unlock: wrong password from {Ip} ({Left} tries left)", ip, left);
            return left <= 0
                ? new OutreachUnlockResultDto { Ok = false, Error = "Nhập sai quá nhiều lần. Tạm khoá, thử lại sau.", LockedUntilUtc = now.AddMinutes(LockMinutes) }
                : new OutreachUnlockResultDto { Ok = false, Error = "Sai mật khẩu chiến dịch.", AttemptsLeft = left };
        }

        var expires = now.AddHours(hours);
        _logger.LogInformation("Outreach unlocked from {Ip} until {Expires:u}", ip, expires);
        return new OutreachUnlockResultDto { Ok = true, Token = Sign(hash, expires, version), ExpiresAtUtc = expires };
    }

    public async Task<bool> ValidateAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var parts = token.Split('.');
        if (parts.Length != 3 || !long.TryParse(parts[0], out var expUnix) || !int.TryParse(parts[1], out var version)) return false;
        if (DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime <= DateTime.UtcNow) return false;
        var (hash, _, _, currentVersion) = await CurrentAsync(ct);
        if (hash == null || version != currentVersion) return false;
        var expected = Sign(hash, DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime, version);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(token));
    }

    private string Sign(string hash, DateTime expiresUtc, int version)
    {
        var secret = _config["Outreach:SessionSecret"];
        var key = SHA256.HashData(Encoding.UTF8.GetBytes("outreach-session|" + hash + "|" + secret));
        var payload = $"{new DateTimeOffset(expiresUtc).ToUnixTimeSeconds()}.{version}";
        var sig = Convert.ToHexString(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(payload))).ToLowerInvariant();
        return $"{payload}.{sig}";
    }

    // ───────────── changes ─────────────

    public async Task<string?> ChangePasswordAsync(OutreachChangePasswordDto input, CancellationToken ct)
    {
        var (hash, _, _, _) = await CurrentAsync(ct);
        if (hash == null || !VerifyPassword(input.CurrentPassword ?? "", hash)) return "Mật khẩu hiện tại không đúng.";
        var next = input.NewPassword ?? "";
        var changePassword = next.Length > 0;
        if (changePassword && (next.Length < 10 || !next.Any(char.IsLetter) || !next.Any(char.IsDigit)))
            return "Mật khẩu mới cần ít nhất 10 ký tự, có cả chữ và số.";

        var row = await RowAsync(ct);
        if (row == null)
        {
            row = new OutreachSettings();
            _db.OutreachSettings.Add(row);
        }
        if (changePassword)
        {
            row.PasswordHash = HashPassword(next);
            row.SessionVersion++;
        }
        if (input.SessionHours is { } h) row.SessionHours = Math.Clamp(h, 1, 72);
        row.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Outreach security changed (password: {Changed}, session hours: {Hours})", changePassword, row.SessionHours);
        return null;
    }

    /// <summary>Ends every open session (all browsers must type the password again).</summary>
    public async Task LockEverywhereAsync(CancellationToken ct)
    {
        var row = await RowAsync(ct);
        if (row == null)
        {
            row = new OutreachSettings { SessionVersion = 2 };
            _db.OutreachSettings.Add(row);
        }
        else row.SessionVersion++;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Outreach: all sessions ended");
    }
}

using Cable.Core;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;

namespace Application.Workers.WorkerAuth;

/// <summary>
/// The worker-login knobs (AppSetting rows, every one optional) and the small pure helpers around them.
/// Defaults are the WORKER_TRACKER_FEATURE decisions 2b, 2c and the §3.2.3 protection rules.
/// </summary>
public record WorkerAuthSettings(
    /// <summary>Workers.TempPasswordHours — how long a temporary password stays valid (default 72).</summary>
    int TempPasswordHours,
    /// <summary>Workers.MaxFailedLogins — wrong passwords before the login locks (default 5).</summary>
    int MaxFailedLogins,
    /// <summary>Workers.LockMinutes — how long the lock lasts (default 15).</summary>
    int LockMinutes,
    /// <summary>Workers.TrustedDeviceDays — how long a device skips the OTP after a successful one (default 90).</summary>
    int TrustedDeviceDays,
    /// <summary>Workers.PartnerLoginUrl — the link in the share message.</summary>
    string PartnerLoginUrl)
{
    public const string TempPasswordHoursKey = "Workers.TempPasswordHours";
    public const string MaxFailedLoginsKey = "Workers.MaxFailedLogins";
    public const string LockMinutesKey = "Workers.LockMinutes";
    public const string TrustedDeviceDaysKey = "Workers.TrustedDeviceDays";
    public const string PartnerLoginUrlKey = "Workers.PartnerLoginUrl";

    public const string DefaultPartnerLoginUrl = "https://cable-app.com/partner/login";

    public static readonly WorkerAuthSettings Defaults = new(72, 5, 15, 90, DefaultPartnerLoginUrl);

    public static async Task<WorkerAuthSettings> ReadAsync(IApplicationDbContext db, CancellationToken ct)
    {
        string[] keys = [TempPasswordHoursKey, MaxFailedLoginsKey, LockMinutesKey, TrustedDeviceDaysKey, PartnerLoginUrlKey];
        var rows = await db.AppSettings.AsNoTracking()
            .Where(s => keys.Contains(s.Key) && !s.IsDeleted)
            .Select(s => new { s.Key, s.Value })
            .ToListAsync(ct);

        int Int(string key, int fallback, int min, int max) =>
            int.TryParse(rows.FirstOrDefault(r => r.Key == key)?.Value, out var v) && v >= min && v <= max ? v : fallback;

        var url = rows.FirstOrDefault(r => r.Key == PartnerLoginUrlKey)?.Value;
        return new WorkerAuthSettings(
            Int(TempPasswordHoursKey, Defaults.TempPasswordHours, 1, 24 * 30),
            Int(MaxFailedLoginsKey, Defaults.MaxFailedLogins, 1, 100),
            Int(LockMinutesKey, Defaults.LockMinutes, 1, 24 * 60),
            Int(TrustedDeviceDaysKey, Defaults.TrustedDeviceDays, 1, 365),
            string.IsNullOrWhiteSpace(url) ? Defaults.PartnerLoginUrl : url.Trim());
    }
}

/// <summary>Temporary passwords: 8 readable characters, no 0 O 1 I L, no dashes (§3.2.1 step 5).</summary>
public static class TempPasswords
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 8;

    public static string Generate()
    {
        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }
}

/// <summary>The rules for a worker's own password (§3.2.3 step 4): 8+ characters, not the temp one, not the phone.</summary>
public static class WorkerPasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 64;

    public static void Validate(string? newPassword, string? phone, string field = "NewPassword")
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < MinLength)
            throw new DataValidationException(field, Lang.Pick($"كلمة السر لازم تكون {MinLength} خانات على الأقل", $"The password must be at least {MinLength} characters"));
        if (newPassword.Length > MaxLength)
            throw new DataValidationException(field, Lang.Pick($"كلمة السر طويلة (الحد {MaxLength})", $"The password is too long (max {MaxLength})"));

        var digits = new string(newPassword.Where(char.IsDigit).ToArray());
        if (digits.Length == newPassword.Length && !string.IsNullOrEmpty(phone))
        {
            var phoneDigits = new string(phone.Where(char.IsDigit).ToArray());
            // 0791234567, 791234567, 962791234567 — all the same number.
            if (phoneDigits.EndsWith(digits.TrimStart('0'), StringComparison.Ordinal) || digits.EndsWith(phoneDigits.TrimStart('0'), StringComparison.Ordinal))
                throw new DataValidationException(field, Lang.Pick("كلمة السر ما بصير تكون رقم موبايلك", "The password cannot be your phone number"));
        }
    }
}

/// <summary>Trusted-device tokens: 32 random bytes to the app, SHA-256 in the database.</summary>
public static class TrustedDeviceTokens
{
    public static string New() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public static class WorkerPhones
{
    /// <summary>"079****967" for display.</summary>
    public static string Mask(string? phone)
    {
        var local = ToLocal(phone);
        return local.Length > 6 ? local[..3] + new string('*', local.Length - 6) + local[^3..] : local;
    }

    /// <summary>962791234567 / +962791234567 → 0791234567 (how the owner and the worker write it).</summary>
    public static string ToLocal(string? phone)
    {
        if (string.IsNullOrEmpty(phone)) return "";
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("962", StringComparison.Ordinal) && digits.Length > 9) return "0" + digits[3..];
        if (!digits.StartsWith('0') && digits.Length == 9) return "0" + digits;
        return digits;
    }

    /// <summary>The stored form (962…), or null when it is not a phone number.</summary>
    public static string? Normalize(string? phone) => PhoneNumberUtility.NormalizePhoneNumber(phone);
}

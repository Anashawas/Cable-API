using Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Cable.Core.Enums;

namespace Application.Settings;

/// <summary>Typed readers for AppSetting rows, with safe defaults.</summary>
public static class AppSettingsProvider
{
    public const string NearbyRadiusKey = "NearbyRadiusKm";
    public const double DefaultNearbyRadiusKm = 30;

    public const string WelcomeBonusMultiplierKey = "WelcomeBonusMultiplier";

    public const string SubscriptionGraceModeKey = "Subscription.GraceMode";   // "Manual" | "AfterDays"
    public const string SubscriptionGraceDaysKey = "Subscription.GraceDays";
    /// <summary>
    /// Manual: an expired subscription stays on until an admin switches it off.
    /// That is the shipped behaviour — nothing turns a paying partner off
    /// automatically unless an admin has opted into AfterDays.
    /// </summary>
    public const SubscriptionGraceMode DefaultSubscriptionGraceMode = SubscriptionGraceMode.Manual;
    public const int DefaultSubscriptionGraceDays = 0;

    /// <summary>The global post-expiry rule; per-subscription overrides sit on the row.</summary>
    public static async Task<(SubscriptionGraceMode Mode, int Days)> GetSubscriptionGraceAsync(
        IApplicationDbContext db, CancellationToken ct)
    {
        var rows = await db.AppSettings.AsNoTracking()
            .Where(s => (s.Key == SubscriptionGraceModeKey || s.Key == SubscriptionGraceDaysKey) && !s.IsDeleted)
            .Select(s => new { s.Key, s.Value })
            .ToListAsync(ct);

        var modeRaw = rows.FirstOrDefault(r => r.Key == SubscriptionGraceModeKey)?.Value;
        var daysRaw = rows.FirstOrDefault(r => r.Key == SubscriptionGraceDaysKey)?.Value;

        var mode = Enum.TryParse<SubscriptionGraceMode>(modeRaw, true, out var m) && Enum.IsDefined(m)
            ? m : DefaultSubscriptionGraceMode;
        var days = int.TryParse(daysRaw, out var d) && d >= 0 ? d : DefaultSubscriptionGraceDays;
        return (mode, days);
    }

    /// <summary>
    /// Double points on a customer's first ever charge. This is the shipped
    /// behaviour, not a starting suggestion — the welcome bonus is part of the
    /// app, like the signup gift it represents, so it must work with no row in
    /// AppSetting and nothing configured anywhere.
    /// </summary>
    public const double DefaultWelcomeBonusMultiplier = 2.0;

    /// <summary>The global "how far is nearby" cap (km) for home-screen serving.</summary>
    public static async Task<double> GetNearbyRadiusKmAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var raw = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == NearbyRadiusKey && !s.IsDeleted)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        return raw is not null && double.TryParse(raw, out var km) && km > 0
            ? km
            : DefaultNearbyRadiusKm;
    }

    /// <summary>
    /// What a customer's first ever charge is multiplied by. 1.0 switches the
    /// welcome bonus off; anything below 1 would take points away, so it is
    /// treated as invalid and the default stands.
    /// </summary>
    public static async Task<double> GetWelcomeBonusMultiplierAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var raw = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == WelcomeBonusMultiplierKey && !s.IsDeleted)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        return raw is not null && double.TryParse(raw, out var multiplier) && multiplier >= 1.0
            ? multiplier
            : DefaultWelcomeBonusMultiplier;
    }

    /// <summary>
    /// Whether an admin has set the multiplier explicitly, as opposed to the
    /// shipped default being in force. Only for display — the resolution path
    /// does not care which it is.
    /// </summary>
    public static async Task<bool> HasWelcomeBonusOverrideAsync(IApplicationDbContext db, CancellationToken ct)
        => await db.AppSettings.AsNoTracking()
            .AnyAsync(s => s.Key == WelcomeBonusMultiplierKey && !s.IsDeleted, ct);
}

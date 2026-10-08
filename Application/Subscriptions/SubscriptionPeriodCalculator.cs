using Cable.Core.Constants;
using Cable.Core.Enums;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Subscriptions;

/// <summary>Computed, never stored. See <see cref="SubscriptionPeriodCalculator.StatusOf"/>.</summary>
public static class SubscriptionStatus
{
    public const string Active = "Active";
    public const string ExpiringSoon = "ExpiringSoon";
    /// <summary>Past expiry but still on — manual grace, or inside the AfterDays window.</summary>
    public const string InGrace = "InGrace";
    /// <summary>Past expiry and off.</summary>
    public const string Expired = "Expired";
    /// <summary>An admin switched it off before expiry.</summary>
    public const string SwitchedOff = "SwitchedOff";
}

/// <summary>
/// The period and status rules, in one place so the recorder, the history
/// query, the renewals dashboard and the daily grace job cannot drift apart.
/// </summary>
public static class SubscriptionPeriodCalculator
{
    public const int ExpiringSoonDays = 7;

    /// <summary>
    /// The days a new payment buys. Renewing while the current period is still
    /// running extends FROM ITS EXPIRY — the customer keeps the days already
    /// paid for. Anything else (first payment, lapsed, switched off) starts on
    /// the day the money arrived.
    /// </summary>
    public static (DateTime Start, DateTime End) NextPeriod(
        Subscription? existing, DateTime paidDateUtc, int planMonths, DateTime nowUtc)
    {
        var stillRunning = existing is { IsSwitchedOff: false } && existing.ExpiresAt > nowUtc;
        var start = stillRunning ? existing!.ExpiresAt : paidDateUtc;
        return (start, start.AddMonths(planMonths));
    }

    public static (SubscriptionGraceMode Mode, int Days) EffectiveGrace(
        Subscription s, (SubscriptionGraceMode Mode, int Days) global)
        => s.GraceMode is int m && Enum.IsDefined(typeof(SubscriptionGraceMode), m)
            ? ((SubscriptionGraceMode)m, Math.Max(0, s.GraceDays ?? 0))
            : global;

    /// <summary>When the AfterDays rule would switch this off; null under manual grace.</summary>
    public static DateTime? AutoOffAt(Subscription s, (SubscriptionGraceMode Mode, int Days) grace)
        => grace.Mode == SubscriptionGraceMode.AfterDays ? s.ExpiresAt.AddDays(grace.Days) : null;

    /// <summary>Is the paid-for thing currently on.</summary>
    public static bool IsOn(Subscription s, (SubscriptionGraceMode Mode, int Days) grace, DateTime nowUtc)
    {
        if (s.IsSwitchedOff) return false;
        if (nowUtc <= s.ExpiresAt) return true;
        var autoOff = AutoOffAt(s, grace);
        return autoOff is null || nowUtc <= autoOff.Value;
    }

    public static string StatusOf(Subscription s, (SubscriptionGraceMode Mode, int Days) grace, DateTime nowUtc)
    {
        if (s.IsSwitchedOff) return nowUtc > s.ExpiresAt ? SubscriptionStatus.Expired : SubscriptionStatus.SwitchedOff;
        if (nowUtc > s.ExpiresAt) return IsOn(s, grace, nowUtc) ? SubscriptionStatus.InGrace : SubscriptionStatus.Expired;
        return s.ExpiresAt <= nowUtc.AddDays(ExpiringSoonDays) ? SubscriptionStatus.ExpiringSoon : SubscriptionStatus.Active;
    }
}

/// <summary>
/// Pushes a subscription's on/off state into the thing it pays for, so the
/// existing consumer-facing reads keep working untouched:
///  - station premium → ChargingPoint.PremiumPaymentDate / PremiumExpiresAt / StationTypeId
///  - banner          → a BannerDuration row linked by SubscriptionId
///  - service-provider premium → nothing yet (no premium fields on the entity)
///  - Cable Connect (OCPP)     → nothing: gating is computed from the Subscription row
///                               (OcppSubscriptionGate); the charger is never cut off
/// Static so the Hangfire job can call it without a service graph.
/// </summary>
public static class SubscriptionEntitySync
{
    // StationType seed: 1 = Normal, 2 = Premium, 3 = Charge Point
    private const int PremiumStationTypeId = 2;
    private const int NormalStationTypeId = 1;

    public static async Task ApplyAsync(IApplicationDbContext db, Subscription s, bool on,
        DateTime nowUtc, CancellationToken ct)
    {
        switch (s.EntityType)
        {
            case SubscriptionEntityTypes.StationPremium:
            {
                var cp = await db.ChargingPoints.FirstOrDefaultAsync(x => x.Id == s.EntityId && !x.IsDeleted, ct);
                if (cp == null) return;
                if (on)
                {
                    var latest = await db.Payments.AsNoTracking()
                        .Where(p => p.SubscriptionId == s.Id && !p.IsVoid && !p.IsDeleted)
                        .OrderByDescending(p => p.PaidDate)
                        .Select(p => (DateTime?)p.PaidDate)
                        .FirstOrDefaultAsync(ct);
                    cp.PremiumPaymentDate = latest ?? s.StartDate;
                    cp.PremiumExpiresAt = s.ExpiresAt;
                    cp.StationTypeId = PremiumStationTypeId;
                }
                else
                {
                    cp.PremiumPaymentDate = null;
                    cp.PremiumExpiresAt = null;
                    if (cp.StationTypeId == PremiumStationTypeId) cp.StationTypeId = NormalStationTypeId;
                }
                break;
            }
            case SubscriptionEntityTypes.OcppConnect:
            {
                // N-2: sharing live data with drivers is the product. It turns ON with the first
                // activation unless the owner has already made a decision; it is never turned
                // off here — lapsing hides the data through the subscription gate instead.
                if (!on) break;
                var cp = await db.ChargingPoints.FirstOrDefaultAsync(x => x.Id == s.EntityId && !x.IsDeleted, ct);
                if (cp == null) break;
                if (cp.ShareLiveStatusSetAt == null && !cp.ShareLiveStatus)
                    cp.ShareLiveStatus = true;
                break;
            }
            case SubscriptionEntityTypes.Banner:
            {
                var todayLocal = DateOnly.FromDateTime(JordanTime.FromUtc(nowUtc));
                var runs = await db.BannerDurations
                    .Where(d => d.SubscriptionId == s.Id && !d.IsDeleted)
                    .ToListAsync(ct);
                if (on)
                {
                    var start = DateOnly.FromDateTime(JordanTime.FromUtc(s.StartDate));
                    var end = DateOnly.FromDateTime(JordanTime.FromUtc(s.ExpiresAt));
                    var current = runs.FirstOrDefault(r => r.EndDate >= todayLocal);
                    if (current != null) { current.EndDate = end; }
                    else db.BannerDurations.Add(new BannerDuration
                    {
                        BannerId = s.EntityId,
                        StartDate = start > todayLocal ? start : todayLocal,
                        EndDate = end,
                        SubscriptionId = s.Id
                    });
                }
                else
                {
                    // Truncate rather than delete: the banner queries do not all
                    // filter IsDeleted, and history should show the run happened.
                    foreach (var r in runs.Where(r => r.EndDate >= todayLocal))
                        r.EndDate = todayLocal.AddDays(-1) < r.StartDate ? r.StartDate : todayLocal.AddDays(-1);
                }
                break;
            }
        }
    }
}

using Application.Subscriptions;
using Cable.Core.Enums;

namespace Application.Billing;

public record RecordState(string Status, bool IsOn, int? ScansRemaining, DateTime? GraceEndsAt);

/// <summary>
/// The computed status of a subscription record. Stored statuses (Cancelled / Upgraded / Renewed) win;
/// records sold through the existing engine take its status (grace rules unchanged) while they are the
/// latest period of that Subscription row; everything else is computed from dates and scans. Pure.
/// </summary>
public static class PlanSubscriptionState
{
    public static RecordState Compute(
        PlanSubscription r,
        Subscription? legacy,
        (SubscriptionGraceMode Mode, int Days) globalGrace,
        bool isLatestOnLegacy,
        DateTime nowUtc)
    {
        int? remaining = r.ScansIncluded is int inc ? Math.Max(0, inc - r.ScansUsed) : null;

        if (PlanSubscriptionStatuses.IsTerminal(r.Status))
            return new RecordState(r.Status, false, remaining, null);

        if (r.StartsAt > nowUtc)
            return new RecordState(PlanSubscriptionStatuses.Scheduled, false, remaining, null);

        if (legacy is not null && isLatestOnLegacy && !r.IsTrial)
        {
            var grace = SubscriptionPeriodCalculator.EffectiveGrace(legacy, globalGrace);
            var on = SubscriptionPeriodCalculator.IsOn(legacy, grace, nowUtc);
            var status = SubscriptionPeriodCalculator.StatusOf(legacy, grace, nowUtc);
            var graceEnds = status == SubscriptionStatus.InGrace ? SubscriptionPeriodCalculator.AutoOffAt(legacy, grace) : null;
            return new RecordState(status, on, remaining, graceEnds);
        }

        if (remaining == 0)
            return new RecordState(PlanSubscriptionStatuses.Exhausted, false, 0, null);

        // Trials end without grace (decision 17); plan-only records have no grace engine.
        if (r.EndsAt is { } end && nowUtc > end)
            return new RecordState(PlanSubscriptionStatuses.Expired, false, remaining, null);

        if (legacy is not null && !isLatestOnLegacy && !r.IsTrial)
            // An earlier period of a renewal chain that still covers today.
            return new RecordState(PlanSubscriptionStatuses.Active, !legacy.IsSwitchedOff, remaining, null);

        var soon = r.EndsAt is { } e && e <= nowUtc.AddDays(SubscriptionPeriodCalculator.ExpiringSoonDays);
        return new RecordState(soon ? PlanSubscriptionStatuses.ExpiringSoon : PlanSubscriptionStatuses.Active, true, remaining, null);
    }

    /// <summary>camelCase for the partner-facing state field (active | expiringSoon | inGrace | expired | switchedOff | none).</summary>
    public static string ToPartnerState(string status) => status switch
    {
        SubscriptionStatus.Active => "active",
        SubscriptionStatus.ExpiringSoon => "expiringSoon",
        SubscriptionStatus.InGrace => "inGrace",
        SubscriptionStatus.Expired => "expired",
        SubscriptionStatus.SwitchedOff => "switchedOff",
        PlanSubscriptionStatuses.Exhausted => "exhausted",
        PlanSubscriptionStatuses.Scheduled => "scheduled",
        PlanSubscriptionStatuses.Cancelled or PlanSubscriptionStatuses.Upgraded or PlanSubscriptionStatuses.Renewed => "expired",
        _ => "none"
    };
}

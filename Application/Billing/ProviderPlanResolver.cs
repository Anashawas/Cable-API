using Application.Settings;
using Application.Subscriptions;
using Cable.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Billing;

/// <summary>Everything the server knows about one provider's plans, computed once.</summary>
public sealed class ProviderPlanSnapshot
{
    public required string ProviderType { get; init; }
    public required int ProviderId { get; init; }
    public required List<BillingPlan> Plans { get; init; }
    public required List<Subscription> LegacyRows { get; init; }
    public required List<PlanSubscription> Records { get; init; }
    public required (SubscriptionGraceMode Mode, int Days) Grace { get; init; }
    public required DateTime Now { get; init; }

    public BillingPlan? FreePlan => Plans.FirstOrDefault(p => p.Key == PlanKeys.Free);

    /// <summary>The Subscription row of the existing engine this plan runs on for this provider, if any.</summary>
    public Subscription? LegacyRowFor(BillingPlan plan) =>
        PlanCatalog.LegacyEntityTypeFor(plan, ProviderType) is { } et ? LegacyRows.FirstOrDefault(s => s.EntityType == et) : null;

    public bool IsLatestOnLegacy(PlanSubscription r) =>
        r.SubscriptionId is int sid &&
        Records.Where(x => x.SubscriptionId == sid && !x.IsTrial && !PlanSubscriptionStatuses.IsTerminal(x.Status))
            .MaxBy(x => x.EndsAt ?? DateTime.MinValue)?.Id == r.Id;

    public RecordState StateOf(PlanSubscription r)
    {
        var legacy = r.SubscriptionId is int sid ? LegacyRows.FirstOrDefault(s => s.Id == sid) : null;
        return PlanSubscriptionState.Compute(r, legacy, Grace, IsLatestOnLegacy(r), Now);
    }

    /// <summary>
    /// Is a Subscription-category plan on for this provider right now? Plans on the existing engine
    /// follow that engine (grace included); plan-only plans follow their records.
    /// </summary>
    public (bool On, string Status, DateTime? ExpiresAt, DateTime? GraceEndsAt, bool IsTrial) PlanState(BillingPlan plan)
    {
        var records = Records.Where(r => r.BillingPlanId == plan.Id).ToList();
        var activeTrial = records.FirstOrDefault(r => r.IsTrial && StateOf(r).IsOn);

        var legacy = LegacyRowFor(plan);
        if (legacy is not null)
        {
            var g = SubscriptionPeriodCalculator.EffectiveGrace(legacy, Grace);
            var status = SubscriptionPeriodCalculator.StatusOf(legacy, g, Now);
            var on = SubscriptionPeriodCalculator.IsOn(legacy, g, Now);
            var graceEnds = status == SubscriptionStatus.InGrace ? SubscriptionPeriodCalculator.AutoOffAt(legacy, g) : null;
            var trialOnly = activeTrial is not null && legacy.ExpiresAt <= (activeTrial.EndsAt ?? legacy.ExpiresAt);
            return (on, status, legacy.ExpiresAt, graceEnds, trialOnly);
        }

        var current = records.Where(r => StateOf(r).IsOn).MaxBy(r => r.EndsAt ?? DateTime.MaxValue);
        if (current is not null)
        {
            var st = StateOf(current);
            return (true, st.Status, current.EndsAt, null, current.IsTrial);
        }
        var last = records.Where(r => r.StartsAt <= Now).MaxBy(r => r.EndsAt ?? DateTime.MinValue);
        return last is null
            ? (false, "None", null, null, false)
            : (false, SubscriptionStatus.Expired, last.EndsAt, null, last.IsTrial);
    }

    public List<BillingPlan> OnSubscriptionPlans() =>
        Plans.Where(p => p.Category == PlanCategories.Subscription && p.Key != PlanKeys.Free && PlanState(p).On)
            .OrderByDescending(p => p.SortOrder).ToList();

    /// <summary>Highest plan that is on, else the free plan (decision 18).</summary>
    public BillingPlan? TierPlan() => OnSubscriptionPlans().FirstOrDefault() ?? FreePlan;

    public List<string> Features()
    {
        var set = new HashSet<string>();
        if (FreePlan is { } free) set.UnionWith(PlanCatalog.Features(free));
        foreach (var p in OnSubscriptionPlans()) set.UnionWith(PlanCatalog.EffectiveFeatures(p, Plans));
        // Catalog order, so the apps get a stable list.
        return PlanFeatureCatalog.All.Select(f => f.Key).Where(set.Contains).ToList();
    }

    public bool TrialUsed(BillingPlan plan) => Records.Any(r => r.BillingPlanId == plan.Id && r.IsTrial);
}

public static class ProviderPlanResolver
{
    public static async Task<ProviderPlanSnapshot> LoadAsync(IApplicationDbContext db, string providerType, int providerId, CancellationToken ct)
    {
        var entityTypes = BillingProviderTypes.EntityTypesFor(providerType);
        var plans = await PlanCatalog.LoadAllAsync(db, ct);
        var legacy = await db.Subscriptions.AsNoTracking()
            .Where(s => entityTypes.Contains(s.EntityType) && s.EntityId == providerId && !s.IsDeleted)
            .ToListAsync(ct);
        var records = await db.PlanSubscriptions.AsNoTracking()
            .Where(r => r.ProviderType == providerType && r.ProviderId == providerId && !r.IsDeleted)
            .OrderBy(r => r.StartsAt)
            .ToListAsync(ct);
        var grace = await AppSettingsProvider.GetSubscriptionGraceAsync(db, ct);

        return new ProviderPlanSnapshot
        {
            ProviderType = providerType,
            ProviderId = providerId,
            Plans = plans,
            LegacyRows = legacy,
            Records = records,
            Grace = grace,
            Now = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Could the partner start a trial of this plan themselves (POST /api/provider/trials)?
    /// Returns null when yes, else the error code.
    /// </summary>
    public static string? TrialBlocker(ProviderPlanSnapshot s, BillingPlan plan, string? billingModel)
    {
        if (!plan.IsActive) return BillingErrorCodes.PlanArchived;
        if (!PlanCatalog.AppliesToProvider(plan, s.ProviderType)) return BillingErrorCodes.PlanNotApplicable;
        var hasTrial = plan.Category == PlanCategories.Loyalty ? plan.TrialScans > 0 || plan.TrialDays > 0 : plan.TrialDays > 0;
        if (!hasTrial || plan.Key == PlanKeys.Free) return BillingErrorCodes.TrialNotAvailable;
        if (s.TrialUsed(plan)) return BillingErrorCodes.TrialAlreadyUsed;

        if (plan.Category == PlanCategories.Subscription)
            return s.OnSubscriptionPlans().Count > 0 ? BillingErrorCodes.ActiveSubscriptionExists : null;

        // Loyalty: only for agreements already billed on bundles — a self-service trial must never
        // move a partner off the percentage model (that is an admin decision).
        if (billingModel != BillingModels.Bundle) return BillingErrorCodes.TrialNotAvailable;
        var now = s.Now;
        return s.Records.Any(r => r.PlanCategory == PlanCategories.Loyalty && LoyaltyBundleService.IsUsable(r, now))
            ? BillingErrorCodes.ActiveSubscriptionExists : null;
    }
}

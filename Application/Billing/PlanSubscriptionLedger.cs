using Application.Subscriptions;
using Cable.Core.Constants;
using Microsoft.EntityFrameworkCore;

namespace Application.Billing;

/// <summary>
/// Optional plan details passed with a payment so the subscription record knows which plan and
/// price option it bought. Every field is optional: the existing RecordPayment flow keeps working
/// without any of them, and the plan is then inferred from the entity type.
/// </summary>
public record PlanPaymentLink(
    int? PlanId = null,
    int? PriceOptionId = null,
    int? UpgradeRequestId = null,
    decimal CreditAppliedJod = 0,
    int? UpgradedFromId = null,
    string? Source = null);

/// <summary>
/// Turns events of the existing subscription engine into subscription records (§2b.7) without
/// changing that engine: a recorded payment opens a record and fulfils the pending upgrade request,
/// a voided payment cancels its record.
/// </summary>
public static class PlanSubscriptionLedger
{
    public static async Task<PlanSubscription?> OnPaymentRecordedAsync(
        IApplicationDbContext db, Subscription sub, Payment payment, PlanPaymentLink? link, int? userId, CancellationToken ct)
    {
        var providerType = BillingProviderTypes.OfEntityType(sub.EntityType);
        if (providerType is null) return null;   // banners are not partner plans

        var plans = await PlanCatalog.LoadAllAsync(db, ct);
        var plan = link?.PlanId is int pid
            ? plans.FirstOrDefault(p => p.Id == pid)
            : PlanCatalog.PlanForEntityType(plans, sub.EntityType);
        if (plan is null) return null;

        var months = sub.PlanMonths;
        var option = link?.PriceOptionId is int oid
            ? plan.PriceOptions.FirstOrDefault(o => o.Id == oid && !o.IsDeleted)
            : plan.PriceOptions.Where(o => !o.IsDeleted && o.IsActive && o.Months == months && o.Scans == null).OrderBy(o => o.SortOrder).FirstOrDefault();

        var record = new PlanSubscription
        {
            ProviderType = providerType,
            ProviderId = sub.EntityId,
            BillingPlanId = plan.Id,
            PlanKey = plan.Key,
            PlanCategory = plan.Category,
            PlanNameAr = plan.NameAr,
            PlanNameEn = plan.NameEn,
            PriceOptionId = option?.Id,
            PriceOptionSnapshot = option is not null
                ? PlanCatalog.SnapshotOf(option)
                : BillingJson.Write(new PriceOptionSnapshot(months, payment.Amount, null)),
            PointsRuleSnapshot = plan.PointsRule,
            IsTrial = false,
            StartsAt = payment.PeriodStart,
            EndsAt = payment.PeriodEnd,
            Status = PlanSubscriptionStatuses.Active,
            PricePaidJod = payment.Amount,
            CreditAppliedJod = link?.CreditAppliedJod ?? 0,
            PaymentId = payment.Id,
            SubscriptionId = sub.Id,
            UpgradedFromId = link?.UpgradedFromId,
            Source = link?.Source ?? PlanSubscriptionSources.Payment,
            CreatedByUserId = userId
        };
        db.PlanSubscriptions.Add(record);
        await db.SaveChanges(ct);

        await FulfilRequestsAsync(db, providerType, sub.EntityId, link?.UpgradeRequestId,
            r => r.BillingPlanId == plan.Id || plans.FirstOrDefault(p => p.Id == r.BillingPlanId) is { } rp
                 && PlanCatalog.LegacyEntityTypeFor(rp, providerType) == sub.EntityType,
            payment.Id, record.Id, userId, ct);

        return record;
    }

    /// <summary>A voided payment cancels the record it opened; the engine already rolled the expiry back.</summary>
    public static async Task OnPaymentVoidedAsync(IApplicationDbContext db, int paymentId, string reason, int? userId, CancellationToken ct)
    {
        var records = await db.PlanSubscriptions
            .Where(r => r.PaymentId == paymentId && !r.IsDeleted && r.Status == PlanSubscriptionStatuses.Active)
            .ToListAsync(ct);
        foreach (var r in records)
        {
            r.Status = PlanSubscriptionStatuses.Cancelled;
            r.CancelledAt = DateTime.UtcNow;
            r.CancelledByUserId = userId;
            r.CancelReason = $"Payment voided: {reason}";
        }
    }

    /// <summary>Marks the given request — or the provider's matching pending requests — fulfilled.</summary>
    public static async Task FulfilRequestsAsync(IApplicationDbContext db, string providerType, int providerId, int? requestId,
        Func<PlanUpgradeRequest, bool> matches, int? paymentId, int planSubscriptionId, int? userId, CancellationToken ct)
    {
        var pending = await db.PlanUpgradeRequests
            .Where(r => r.ProviderType == providerType && r.ProviderId == providerId
                        && r.Status == UpgradeRequestStatuses.Pending && !r.IsDeleted)
            .ToListAsync(ct);
        var targets = requestId is int id ? pending.Where(r => r.Id == id).ToList() : pending.Where(matches).ToList();
        if (targets.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var r in targets)
        {
            r.Status = UpgradeRequestStatuses.Fulfilled;
            r.HandledAt = now;
            r.HandledByUserId = userId;
            r.FulfilledPaymentId = paymentId;
            r.FulfilledPlanSubscriptionId = planSubscriptionId;
        }
        await db.SaveChanges(ct);
    }
}

/// <summary>
/// Turns a period on in the existing engine without a payment (trials, plans included in another
/// plan, complimentary periods): get-or-create the Subscription row, extend it, sync the entity.
/// Payments still go through PaymentRecorder; this is only for periods nobody paid for directly.
/// </summary>
public static class LegacySubscriptionBridge
{
    public static async Task<Subscription> ExtendAsync(IApplicationDbContext db, string entityType, int entityId,
        DateTime startUtc, DateTime untilUtc, string? note, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.EntityType == entityType && s.EntityId == entityId && !s.IsDeleted, ct);
        if (sub is null)
        {
            sub = new Subscription
            {
                EntityType = entityType,
                EntityId = entityId,
                StartDate = startUtc,
                ExpiresAt = untilUtc,
                Note = note
            };
            db.Subscriptions.Add(sub);
        }
        else
        {
            var lapsed = sub.IsSwitchedOff || sub.ExpiresAt < now;
            sub.ExpiresAt = lapsed ? untilUtc : (untilUtc > sub.ExpiresAt ? untilUtc : sub.ExpiresAt);
            sub.IsSwitchedOff = false;
            sub.SwitchedOffAt = null;
            sub.SwitchedOffByUserId = null;
        }

        await SubscriptionEntitySync.ApplyAsync(db, sub, on: true, now, ct);
        await db.SaveChanges(ct);
        return sub;
    }

    public static async Task SwitchOffAsync(IApplicationDbContext db, Subscription sub, int? userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        sub.IsSwitchedOff = true;
        sub.SwitchedOffAt = now;
        sub.SwitchedOffByUserId = userId;
        await SubscriptionEntitySync.ApplyAsync(db, sub, on: false, now, ct);
    }

    /// <summary>Extends the existing-engine rows of the plans <paramref name="plan"/> includes, to the same end (connect ⊇ premium).</summary>
    public static async Task ApplyIncludedPlansAsync(IApplicationDbContext db, BillingPlan plan, string providerType, int providerId,
        DateTime startUtc, DateTime untilUtc, CancellationToken ct)
    {
        var ids = PlanCatalog.Includes(plan);
        if (ids.Count == 0) return;
        var included = await db.BillingPlans.AsNoTracking().Where(p => ids.Contains(p.Id) && !p.IsDeleted).ToListAsync(ct);
        foreach (var inc in included)
            if (PlanCatalog.LegacyEntityTypeFor(inc, providerType) is { } et)
                await ExtendAsync(db, et, providerId, startUtc, untilUtc, $"Included in plan '{plan.Key}'", ct);
    }

    public static bool IsBillable(string entityType) => entityType != SubscriptionEntityTypes.Banner && SubscriptionEntityTypes.IsValid(entityType);
}

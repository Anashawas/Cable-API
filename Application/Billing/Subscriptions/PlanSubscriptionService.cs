using Application.Subscriptions;
using Cable.Core;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Billing.Subscriptions;

/// <summary>Offline payment details for the admin flows; recorded through the existing PaymentRecorder.</summary>
public record AdminPaymentInput(
    decimal? Amount,
    PaymentMethod Method,
    /// <summary>Jordan local date/time the money was received.</summary>
    DateTime PaidDate,
    PaymentRecorder.PayerInput Payer,
    string? Currency = null,
    string? Note = null,
    string? ReceiptImageFileName = null);

/// <summary>
/// Starting subscriptions, trials and upgrades on top of the existing engine. Payments always go through
/// <see cref="PaymentRecorder"/> (same receipts, same period rules); periods nobody paid for directly
/// (trials, included plans, complimentary) go through <see cref="LegacySubscriptionBridge"/>.
/// </summary>
public sealed class PlanSubscriptionService(IApplicationDbContext db, PaymentRecorder recorder)
{
    public async Task<PlanSubscription> SubscribeAsync(
        int? userId, string providerType, int providerId, BillingPlan plan, BillingPlanPriceOption option,
        AdminPaymentInput? payment, DateTime? startsAt, PlanPaymentLink link, string? note, CancellationToken ct)
    {
        var et = PlanCatalog.LegacyEntityTypeFor(plan, providerType);
        var now = DateTime.UtcNow;

        PlanSubscription record;
        if (et is not null && payment is not null)
        {
            var recorded = await recorder.RecordAsync(et, providerId, option.Months, null,
                payment.Amount ?? Math.Max(0m, option.PriceJod - link.CreditAppliedJod), payment.Currency, payment.Method,
                JordanTime.ToUtc(payment.PaidDate), payment.Payer, payment.Note ?? note, payment.ReceiptImageFileName, ct,
                link with { PlanId = plan.Id, PriceOptionId = option.Id });
            record = recorded.PlanRecord
                     ?? throw new InvalidOperationException("The payment was recorded but its subscription record was not created");
            if (note is not null) { record.Note = note; await db.SaveChanges(ct); }
            await LegacySubscriptionBridge.ApplyIncludedPlansAsync(db, plan, providerType, providerId, record.StartsAt, recorded.Subscription.ExpiresAt, ct);
            return record;
        }

        var months = option.Months ?? 1;
        DateTime start, end;
        Subscription? row = null;
        if (et is not null)
        {
            // Complimentary period on the existing engine: same "extend from expiry while running" rule as a payment.
            var existing = await db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.EntityType == et && s.EntityId == providerId && !s.IsDeleted, ct);
            (start, end) = SubscriptionPeriodCalculator.NextPeriod(existing, startsAt ?? now, months, now);
            row = await LegacySubscriptionBridge.ExtendAsync(db, et, providerId, start, end, note, ct);
        }
        else
        {
            var running = await db.PlanSubscriptions.AsNoTracking()
                .Where(r => r.ProviderType == providerType && r.ProviderId == providerId && r.BillingPlanId == plan.Id
                            && r.Status == PlanSubscriptionStatuses.Active && !r.IsDeleted && r.EndsAt > now && !r.IsTrial)
                .MaxAsync(r => (DateTime?)r.EndsAt, ct);
            start = running ?? startsAt ?? now;
            end = start.AddMonths(months);
        }

        record = new PlanSubscription
        {
            ProviderType = providerType,
            ProviderId = providerId,
            BillingPlanId = plan.Id,
            PlanKey = plan.Key,
            PlanCategory = plan.Category,
            PlanNameAr = plan.NameAr,
            PlanNameEn = plan.NameEn,
            PriceOptionId = option.Id,
            PriceOptionSnapshot = PlanCatalog.SnapshotOf(option),
            PointsRuleSnapshot = plan.PointsRule,
            StartsAt = start,
            EndsAt = end,
            Status = PlanSubscriptionStatuses.Active,
            PricePaidJod = payment?.Amount ?? 0m,
            CreditAppliedJod = link.CreditAppliedJod,
            SubscriptionId = row?.Id,
            UpgradedFromId = link.UpgradedFromId,
            Source = link.Source ?? PlanSubscriptionSources.Admin,
            CreatedByUserId = userId,
            Note = note
        };
        db.PlanSubscriptions.Add(record);
        await db.SaveChanges(ct);

        await LegacySubscriptionBridge.ApplyIncludedPlansAsync(db, plan, providerType, providerId, start, end, ct);
        await PlanSubscriptionLedger.FulfilRequestsAsync(db, providerType, providerId, link.UpgradeRequestId,
            r => r.BillingPlanId == plan.Id, null, record.Id, userId, ct);
        return record;
    }

    /// <summary>
    /// Free trial (§2b.6): once per provider per plan, no payment, no grace afterwards. Subscription plans
    /// switch the existing engine on until the trial ends (the billing job switches it off again unless a
    /// payment extended it); loyalty plans grant trialScans (and/or trialDays) as a bundle.
    /// </summary>
    public async Task<PlanSubscription> StartTrialAsync(int? userId, string providerType, int providerId, BillingPlan plan,
        bool byPartner, CancellationToken ct)
    {
        var snapshot = await ProviderPlanResolver.LoadAsync(db, providerType, providerId, ct);
        var agreement = await db.PartnerAgreements.AsNoTracking()
            .Where(a => a.ProviderType == providerType && a.ProviderId == providerId && a.IsActive && !a.IsDeleted)
            .Select(a => a.BillingModel).FirstOrDefaultAsync(ct);

        // Admins may trial a loyalty plan on a percentage agreement (they decide the billing model);
        // a partner may not move themselves off percentage.
        var blocker = ProviderPlanResolver.TrialBlocker(snapshot, plan, byPartner ? BillingModels.Normalize(agreement) : BillingModels.Bundle);
        ThrowFor(blocker);

        if (plan.Category == PlanCategories.Loyalty)
            return await LoyaltyBundleService.PurchaseAsync(db, userId, providerType, providerId, plan, null, true, null, 0, null,
                byPartner ? "Trial started by the partner" : "Trial started by an admin", ct);

        var now = DateTime.UtcNow;
        var end = now.AddDays(plan.TrialDays);
        var et = PlanCatalog.LegacyEntityTypeFor(plan, providerType);
        Subscription? row = null;
        if (et is not null)
            row = await LegacySubscriptionBridge.ExtendAsync(db, et, providerId, now, end, "Free trial", ct);

        var record = new PlanSubscription
        {
            ProviderType = providerType,
            ProviderId = providerId,
            BillingPlanId = plan.Id,
            PlanKey = plan.Key,
            PlanCategory = plan.Category,
            PlanNameAr = plan.NameAr,
            PlanNameEn = plan.NameEn,
            PriceOptionSnapshot = BillingJson.Write(new PriceOptionSnapshot(null, 0, null)),
            IsTrial = true,
            StartsAt = now,
            EndsAt = end,
            Status = PlanSubscriptionStatuses.Active,
            SubscriptionId = row?.Id,
            Source = PlanSubscriptionSources.Trial,
            CreatedByUserId = userId,
            Note = byPartner ? "Trial started by the partner" : "Trial started by an admin"
        };
        db.PlanSubscriptions.Add(record);
        await db.SaveChanges(ct);
        await LegacySubscriptionBridge.ApplyIncludedPlansAsync(db, plan, providerType, providerId, now, end, ct);
        return record;
    }

    /// <summary>Runs several saves as one unit (record + engine row + payment), so a failure leaves nothing half-done.</summary>
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public static void ThrowFor(string? code)
    {
        switch (code)
        {
            case null: return;
            case BillingErrorCodes.TrialAlreadyUsed:
                throw new ConflictException(code, Lang.Pick("استعملت التجربة المجانية لهذه الباقة من قبل", "The free trial of this plan was already used"));
            case BillingErrorCodes.ActiveSubscriptionExists:
                throw new ConflictException(code, Lang.Pick("عندك اشتراك فعّال بنفس الفئة — استعمل الترقية", "There is an active subscription in this category — use an upgrade instead"));
            case BillingErrorCodes.PlanArchived:
                throw new UnprocessableException(code, Lang.Pick("هذه الباقة مؤرشفة ولا تُباع حالياً", "This plan is archived and is no longer sold"));
            case BillingErrorCodes.PlanNotApplicable:
                throw new UnprocessableException(code, Lang.Pick("هذه الباقة غير متاحة لهذا النوع", "This plan is not offered for this provider type"));
            default:
                throw new UnprocessableException(BillingErrorCodes.TrialNotAvailable, Lang.Pick("هذه الباقة بدون تجربة مجانية", "This plan has no free trial"));
        }
    }

    /// <summary>
    /// ACTIVE_SUBSCRIPTION_EXISTS when another Subscription-category plan is on and neither plan includes
    /// the other (renewing the same plan, or premium under connect, is fine).
    /// </summary>
    public static void EnsureNoConflictingPlan(ProviderPlanSnapshot s, BillingPlan target)
    {
        foreach (var on in s.OnSubscriptionPlans())
        {
            if (on.Id == target.Id) continue;
            if (PlanCatalog.Includes(on).Contains(target.Id) || PlanCatalog.Includes(target).Contains(on.Id)) continue;
            ThrowFor(BillingErrorCodes.ActiveSubscriptionExists);
        }
    }
}

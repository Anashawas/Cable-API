using Application.Common.Extensions;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Billing;

/// <summary>The partner-facing scan counter (home, wallet, my-assets, "My plan").</summary>
public record LoyaltyBundleDto(
    int PlanSubscriptionId,
    int PlanId,
    string PlanKey,
    string PlanName,
    /// <summary>Null = unlimited scanning.</summary>
    int? ScansTotal,
    int ScansUsed,
    int? ScansRemaining,
    int? PercentRemaining,
    bool IsUnlimited,
    bool IsTrial,
    DateTime StartsAt,
    DateTime? EndsAt,
    /// <summary>active | low (≤ 20% left) | exhausted | expired.</summary>
    string State);

public record ProviderLoyaltyBundleDto(string ProviderType, int ProviderId, string BillingModel, LoyaltyBundleDto? Bundle);

/// <summary>What a bundle-billed code was created under.</summary>
public record BundleTerms(int PlanSubscriptionId, int Points, PointsRule? Rule);

/// <summary>PreviewPartnerCode's view of a bundle-billed code.</summary>
public record BundlePreview(bool QuotaReached, decimal? MinimumAmountForPoints)
{
    /// <summary>Why the code earns no points: partnerQuotaReached (vague on purpose), belowMinimumAmount, or nothing.</summary>
    public (string? Reason, decimal? Minimum) Explain(int pointsToBeAwarded) =>
        QuotaReached ? ("partnerQuotaReached", null)
        : pointsToBeAwarded > 0 ? (null, null)
        : MinimumAmountForPoints is not null ? ("belowMinimumAmount", MinimumAmountForPoints)
        : ("partnerInactive", null);
}

/// <summary>Scan bundles (§3): the current bundle, buying one, the counter.</summary>
public static class LoyaltyBundleService
{
    /// <summary>The latest non-terminal loyalty record that has started (exhausted / expired ones included, for display).</summary>
    public static Task<PlanSubscription?> LatestAsync(IApplicationDbContext db, string providerType, int providerId, CancellationToken ct, bool track = false)
    {
        var now = DateTime.UtcNow;
        var q = track ? db.PlanSubscriptions : db.PlanSubscriptions.AsNoTracking();
        return q.Where(r => r.ProviderType == providerType && r.ProviderId == providerId && !r.IsDeleted
                            && r.PlanCategory == PlanCategories.Loyalty
                            && r.Status == PlanSubscriptionStatuses.Active && r.StartsAt <= now)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>A bundle that can still give points right now.</summary>
    public static bool IsUsable(PlanSubscription r, DateTime nowUtc) =>
        r.Status == PlanSubscriptionStatuses.Active && r.StartsAt <= nowUtc
        && (r.EndsAt is null || r.EndsAt > nowUtc)
        && (r.ScansIncluded is null || r.ScansUsed < r.ScansIncluded);

    public static LoyaltyBundleDto ToDto(PlanSubscription r, DateTime nowUtc)
    {
        var remaining = r.ScansIncluded is int inc ? Math.Max(0, inc - r.ScansUsed) : (int?)null;
        var pct = BillingMath.PercentRemaining(r.ScansIncluded, r.ScansUsed);
        var state = r.EndsAt is { } e && e <= nowUtc ? "expired"
            : remaining == 0 ? "exhausted"
            : pct is <= 20 ? "low"
            : "active";
        return new LoyaltyBundleDto(r.Id, r.BillingPlanId, r.PlanKey, Lang.Pick(r.PlanNameAr, r.PlanNameEn),
            r.ScansIncluded, r.ScansUsed, remaining, pct, r.ScansIncluded is null, r.IsTrial, r.StartsAt, r.EndsAt, state);
    }

    /// <summary>Null when the partner is on the percentage model and has never had a bundle.</summary>
    public static async Task<LoyaltyBundleDto?> GetDtoAsync(IApplicationDbContext db, string providerType, int providerId, CancellationToken ct)
    {
        var latest = await LatestAsync(db, providerType, providerId, ct);
        return latest is null ? null : ToDto(latest, DateTime.UtcNow);
    }

    public static async Task<List<ProviderLoyaltyBundleDto>> GetForProvidersAsync(IApplicationDbContext db,
        IReadOnlyCollection<(string ProviderType, int ProviderId)> providers, CancellationToken ct)
    {
        if (providers.Count == 0) return [];
        var cpIds = providers.Where(p => p.ProviderType == BillingProviderTypes.ChargingPoint).Select(p => p.ProviderId).ToList();
        var spIds = providers.Where(p => p.ProviderType == BillingProviderTypes.ServiceProvider).Select(p => p.ProviderId).ToList();
        var now = DateTime.UtcNow;

        var agreements = await db.PartnerAgreements.AsNoTracking()
            .Where(a => a.IsActive && !a.IsDeleted
                        && ((a.ProviderType == BillingProviderTypes.ChargingPoint && cpIds.Contains(a.ProviderId))
                            || (a.ProviderType == BillingProviderTypes.ServiceProvider && spIds.Contains(a.ProviderId))))
            .Select(a => new { a.ProviderType, a.ProviderId, a.BillingModel })
            .ToListAsync(ct);
        var records = await db.PlanSubscriptions.AsNoTracking()
            .Where(r => !r.IsDeleted && r.PlanCategory == PlanCategories.Loyalty && r.Status == PlanSubscriptionStatuses.Active && r.StartsAt <= now
                        && ((r.ProviderType == BillingProviderTypes.ChargingPoint && cpIds.Contains(r.ProviderId))
                            || (r.ProviderType == BillingProviderTypes.ServiceProvider && spIds.Contains(r.ProviderId))))
            .ToListAsync(ct);

        var result = new List<ProviderLoyaltyBundleDto>();
        foreach (var (type, id) in providers)
        {
            var a = agreements.FirstOrDefault(x => x.ProviderType == type && x.ProviderId == id);
            var r = records.Where(x => x.ProviderType == type && x.ProviderId == id).MaxBy(x => x.Id);
            if (a is null && r is null) continue;
            result.Add(new ProviderLoyaltyBundleDto(type, id, BillingModels.Normalize(a?.BillingModel), r is null ? null : ToDto(r, now)));
        }
        return result;
    }

    /// <summary>
    /// Buys (or trials) a loyalty plan for a provider. Rules (§3.2, decision 11):
    ///  - the price is debited from the provider wallet as <see cref="WalletTransactionType.BundlePurchase"/>,
    ///    so it flows through the existing settlement;
    ///  - renewal adds to what is left: the previous bundle's unused scans are carried over (even if it expired),
    ///    and a still-running period is extended rather than cut short;
    ///  - an upgrade instead credits the unused value in JOD and carries nothing.
    /// </summary>
    public static async Task<PlanSubscription> PurchaseAsync(
        IApplicationDbContext db, int? userId, string providerType, int providerId,
        BillingPlan plan, BillingPlanPriceOption? option, bool isTrial,
        PlanSubscription? upgradeFrom, decimal credit, int? upgradeRequestId, string? note, CancellationToken ct)
    {
        if (plan.Category != PlanCategories.Loyalty)
            throw new DataValidationException("PlanId", Lang.Pick("هذه ليست باقة ولاء", "This is not a loyalty plan"));
        PlanCatalog.EnsureSellable(plan, providerType);
        if (!isTrial && option is null)
            throw new UnprocessableException(BillingErrorCodes.InvalidPriceOption, Lang.Pick("اختر خيار سعر", "Choose a price option"));

        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var previous = upgradeFrom ?? await LatestAsync(db, providerType, providerId, ct, track: true);
            if (upgradeFrom is not null && previous is not null && !db.PlanSubscriptions.Local.Contains(previous))
                db.PlanSubscriptions.Attach(previous);

            var carry = 0;
            DateTime? extendFrom = null;
            if (previous is not null)
            {
                if (upgradeFrom is null)
                {
                    if (previous.ScansIncluded is int inc) carry = Math.Max(0, inc - previous.ScansUsed);
                    if (previous.EndsAt is { } pe && pe > now && previous.BillingPlanId == plan.Id) extendFrom = pe;
                    previous.Status = PlanSubscriptionStatuses.Renewed;
                }
                else previous.Status = PlanSubscriptionStatuses.Upgraded;
                previous.ModifiedAt = now;
                previous.ModifiedBy = userId;
            }

            int? scans;
            DateTime? endsAt;
            if (isTrial)
            {
                scans = plan.TrialScans > 0 ? plan.TrialScans : null;
                endsAt = plan.TrialDays > 0 ? now.AddDays(plan.TrialDays) : null;
                if (scans is null && endsAt is null)
                    throw new UnprocessableException(BillingErrorCodes.TrialNotAvailable, Lang.Pick("هذه الباقة بدون تجربة مجانية", "This plan has no free trial"));
                if (scans is not null) scans += carry;
            }
            else
            {
                scans = option!.Scans is int s ? s + carry : null;
                endsAt = option.Months is int m ? (extendFrom ?? now).AddMonths(m) : null;
            }

            var price = isTrial ? 0m : Math.Max(0m, option!.PriceJod - credit);
            var record = new PlanSubscription
            {
                ProviderType = providerType,
                ProviderId = providerId,
                BillingPlanId = plan.Id,
                PlanKey = plan.Key,
                PlanCategory = plan.Category,
                PlanNameAr = plan.NameAr,
                PlanNameEn = plan.NameEn,
                PriceOptionId = option?.Id,
                PriceOptionSnapshot = option is null
                    ? BillingJson.Write(new PriceOptionSnapshot(null, 0, plan.TrialScans > 0 ? plan.TrialScans : null))
                    : PlanCatalog.SnapshotOf(option),
                PointsRuleSnapshot = plan.PointsRule,
                IsTrial = isTrial,
                StartsAt = now,
                EndsAt = endsAt,
                ScansIncluded = scans,
                ScansCarriedOver = scans is null ? 0 : carry,
                Status = PlanSubscriptionStatuses.Active,
                PricePaidJod = price,
                CreditAppliedJod = credit,
                UpgradedFromId = upgradeFrom?.Id,
                Source = isTrial ? PlanSubscriptionSources.Trial : upgradeFrom is not null ? PlanSubscriptionSources.Upgrade : PlanSubscriptionSources.Bundle,
                CreatedByUserId = userId,
                Note = note
            };
            db.PlanSubscriptions.Add(record);
            await db.SaveChanges(ct);

            if (price > 0)
            {
                decimal balanceAfter;
                if (providerType == BillingProviderTypes.ChargingPoint)
                {
                    var cp = await db.ChargingPoints.FindWithLockAsync("ChargingPoint", providerId, ct)
                             ?? throw new NotFoundException($"ChargingPoint with id {providerId} not found");
                    cp.WalletBalance -= price;
                    balanceAfter = cp.WalletBalance;
                }
                else
                {
                    var sp = await db.ServiceProviders.FindWithLockAsync("ServiceProvider", providerId, ct)
                             ?? throw new NotFoundException($"ServiceProvider with id {providerId} not found");
                    sp.WalletBalance -= price;
                    balanceAfter = sp.WalletBalance;
                }

                var walletTx = new ProviderWalletTransaction
                {
                    ProviderType = providerType,
                    ProviderId = providerId,
                    TransactionType = (int)WalletTransactionType.BundlePurchase,
                    Amount = -price,
                    BalanceAfter = balanceAfter,
                    ReferenceType = "PlanSubscription",
                    ReferenceId = record.Id,
                    Note = $"Loyalty plan '{plan.Key}' — {(scans is null ? "unlimited scans" : $"{scans} scans")}{(credit > 0 ? $", upgrade credit {credit:F3}" : "")}",
                    RecordedByUserId = userId
                };
                db.ProviderWalletTransactions.Add(walletTx);
                await db.SaveChanges(ct);
                record.WalletTransactionId = walletTx.Id;
            }

            await db.SaveChanges(ct);
            await PlanSubscriptionLedger.FulfilRequestsAsync(db, providerType, providerId, upgradeRequestId,
                r => r.Kind == UpgradeRequestKinds.Bundle || r.BillingPlanId == plan.Id, null, record.Id, userId, ct);

            await tx.CommitAsync(ct);
            return record;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}

/// <summary>
/// The scan bundle's hooks into the partner transaction pipeline. Kept here so the pipeline
/// handlers only gain one call each:
///  - creating a code  → <see cref="ResolveForNewCodeAsync"/> (409 bundleExhausted at 0 scans, mandatory minimum amount, points from the plan's rule) + <see cref="Track"/>;
///  - completing it    → <see cref="ConsumeOnCompletionAsync"/> (exactly one scan, atomically);
///  - reversing it     → <see cref="RefundScanAsync"/>;
///  - previewing it    → <see cref="PreviewAsync"/> (partnerQuotaReached / belowMinimumAmount).
/// Expired and cancelled codes never consumed anything, so they need no hook.
/// </summary>
public static class LoyaltyBundleScans
{
    /// <summary>Null for a percentage-billed agreement — the caller then behaves exactly as before.</summary>
    public static async Task<BundleTerms?> ResolveForNewCodeAsync(IApplicationDbContext db, PartnerAgreement agreement,
        decimal transactionAmount, CancellationToken ct)
    {
        if (BillingModels.Normalize(agreement.BillingModel) != BillingModels.Bundle) return null;

        var now = DateTime.UtcNow;
        var bundle = await LoyaltyBundleService.LatestAsync(db, agreement.ProviderType, agreement.ProviderId, ct);
        if (bundle is null || !LoyaltyBundleService.IsUsable(bundle, now))
            throw new ConflictException(BillingErrorCodes.BundleExhausted,
                Lang.Pick("خلصت حزمة المسحات — جدّد لتكمل تعطي نقاط لزباينك", "Your scan bundle is used up — renew it to keep giving your customers points"));

        // Decision 12: a 1-JOD scan costs the bundle the same as a 50-JOD one, so a minimum is mandatory.
        var minimum = agreement.MinimumTransactionAmount is > 0
            ? agreement.MinimumTransactionAmount.Value
            : await BillingSettingsProvider.GetBundleMinimumAmountAsync(db, ct);
        if (transactionAmount < minimum)
            throw new DataValidationException("TransactionAmount",
                Lang.Pick($"أقل مبلغ للمعاملة {minimum:F3} دينار", $"Transaction amount must be at least {minimum:F3} JOD"));

        var rule = BillingJson.ReadRule(bundle.PointsRuleSnapshot);
        return new BundleTerms(bundle.Id, PointsRuleCalculator.PointsFor(rule, transactionAmount), rule);
    }

    /// <summary>
    /// For a promo discount on a bundle-billed code: the points the bundle's rule gives on the lower final amount,
    /// and 0 once the code completed with no scan left. Null for percentage-billed codes (the promo then uses the
    /// agreement's percentage, as before). Tracked query on purpose: inside the scan the mark was just updated.
    /// </summary>
    public static async Task<Func<decimal, int>?> PointsForAmountAsync(IApplicationDbContext db, PartnerTransaction transaction, CancellationToken ct)
    {
        var mark = await db.PartnerTransactionBundleScans.FirstOrDefaultAsync(x => x.PartnerTransactionId == transaction.Id, ct);
        if (mark is null) return null;
        if (mark.QuotaReached) return _ => 0;

        var recordId = mark.ConsumedPlanSubscriptionId ?? mark.PlanSubscriptionId;
        var snapshot = recordId is null ? null
            : await db.PlanSubscriptions.AsNoTracking().Where(r => r.Id == recordId).Select(r => r.PointsRuleSnapshot).FirstOrDefaultAsync(ct);
        var rule = BillingJson.ReadRule(snapshot);
        return rule is null ? _ => 0 : amount => PointsRuleCalculator.PointsFor(rule, amount);
    }

    /// <summary>Marks the new transaction as bundle-billed (saved with it).</summary>
    public static void Track(IApplicationDbContext db, PartnerTransaction transaction, BundleTerms terms) =>
        db.PartnerTransactionBundleScans.Add(new PartnerTransactionBundleScan
        {
            PartnerTransaction = transaction,
            PlanSubscriptionId = terms.PlanSubscriptionId,
            CreatedAt = DateTime.UtcNow
        });

    /// <summary>
    /// Called when a code completes, inside the completion transaction. Takes exactly one scan from the
    /// provider's current bundle with a single conditional UPDATE, so two codes can never both take the
    /// last scan. With no scan left the code still completes (codes created earlier stay confirmable)
    /// but earns no points — the customer is told partnerQuotaReached at preview.
    /// </summary>
    public static async Task ConsumeOnCompletionAsync(IApplicationDbContext db, PartnerTransaction transaction, CancellationToken ct)
    {
        var mark = await db.PartnerTransactionBundleScans.FirstOrDefaultAsync(x => x.PartnerTransactionId == transaction.Id, ct);
        if (mark is null || mark.ConsumedAt is not null) return;

        var now = DateTime.UtcNow;
        var bundle = await LoyaltyBundleService.LatestAsync(db, transaction.ProviderType, transaction.ProviderId, ct);
        var taken = 0;
        if (bundle is not null && (bundle.EndsAt is null || bundle.EndsAt > now))
        {
            var id = bundle.Id;
            taken = await db.PlanSubscriptions
                .Where(r => r.Id == id && r.Status == PlanSubscriptionStatuses.Active
                            && (r.ScansIncluded == null || r.ScansUsed < r.ScansIncluded))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ScansUsed, r => r.ScansUsed + 1), ct);
        }

        if (taken == 1)
        {
            mark.ConsumedPlanSubscriptionId = bundle!.Id;
            mark.ConsumedAt = now;
            // A renewal bought after the code was created may carry a different rule; the code keeps
            // the points it showed when it was created (points are fixed at transaction time).
        }
        else
        {
            mark.QuotaReached = true;
            transaction.PointsAwarded = 0;
            transaction.BasePoints = 0;
        }
    }

    /// <summary>A reversed completed transaction gives its scan back.</summary>
    public static async Task RefundScanAsync(IApplicationDbContext db, int partnerTransactionId, CancellationToken ct)
    {
        var mark = await db.PartnerTransactionBundleScans.FirstOrDefaultAsync(x => x.PartnerTransactionId == partnerTransactionId, ct);
        if (mark?.ConsumedPlanSubscriptionId is not int bundleId || mark.RefundedAt is not null) return;

        await db.PlanSubscriptions
            .Where(r => r.Id == bundleId && r.ScansUsed > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ScansUsed, r => r.ScansUsed - 1), ct);
        mark.RefundedAt = DateTime.UtcNow;
    }

    /// <summary>Null when the code is percentage-billed.</summary>
    public static async Task<BundlePreview?> PreviewAsync(IApplicationDbContext db, PartnerTransaction transaction, CancellationToken ct)
    {
        var isBundle = await db.PartnerTransactionBundleScans.AsNoTracking().AnyAsync(x => x.PartnerTransactionId == transaction.Id, ct);
        if (!isBundle) return null;

        var bundle = await LoyaltyBundleService.LatestAsync(db, transaction.ProviderType, transaction.ProviderId, ct);
        var quota = bundle is null || !LoyaltyBundleService.IsUsable(bundle, DateTime.UtcNow);
        var minimum = PointsRuleCalculator.MinimumAmountForPoints(BillingJson.ReadRule(bundle?.PointsRuleSnapshot));
        return new BundlePreview(quota, minimum);
    }
}

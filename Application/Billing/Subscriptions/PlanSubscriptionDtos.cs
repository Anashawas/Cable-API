using Application.Billing.Plans;
using Application.Settings;
using Application.Subscriptions;
using Application.Subscriptions.Queries;
using Cable.Core.Enums;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Billing.Subscriptions;

/// <summary>A subscription record (§2b.7) with its computed status.</summary>
public record PlanSubscriptionDto(
    int Id,
    string ProviderType,
    int ProviderId,
    string? ProviderName,
    int PlanId,
    string PlanKey,
    string PlanCategory,
    string PlanName,
    string PlanNameAr,
    string PlanNameEn,
    int? PriceOptionId,
    PriceOptionSnapshot? PriceOptionSnapshot,
    PointsRule? PointsRuleSnapshot,
    bool IsTrial,
    DateTime StartsAt,
    DateTime? EndsAt,
    int? ScansIncluded,
    int ScansUsed,
    int? ScansRemaining,
    int ScansCarriedOver,
    /// <summary>Active | ExpiringSoon | InGrace | Expired | SwitchedOff | Scheduled | Exhausted | Cancelled | Upgraded | Renewed.</summary>
    string Status,
    bool IsOn,
    DateTime? GraceEndsAt,
    decimal PricePaidJod,
    decimal CreditAppliedJod,
    int? PaymentId,
    string? PaymentReferenceNo,
    string? ReceiptDownloadPath,
    int? SubscriptionId,
    int? WalletTransactionId,
    int? UpgradedFromId,
    string Source,
    int? CreatedByUserId,
    DateTime CreatedAt,
    DateTime? CancelledAt,
    string? CancelReason,
    string? Note);

public static class PlanSubscriptionMapper
{
    public static async Task<List<PlanSubscriptionDto>> MapAsync(IApplicationDbContext db, IReadOnlyCollection<PlanSubscription> records, CancellationToken ct)
    {
        if (records.Count == 0) return [];
        var now = DateTime.UtcNow;
        var grace = await AppSettingsProvider.GetSubscriptionGraceAsync(db, ct);

        var subIds = records.Where(r => r.SubscriptionId != null).Select(r => r.SubscriptionId!.Value).Distinct().ToList();
        var legacy = subIds.Count == 0 ? new List<Subscription>() : await db.Subscriptions.AsNoTracking().Where(s => subIds.Contains(s.Id)).ToListAsync(ct);
        // "Latest period on the engine row" must look at every record of that row, not only this page.
        var chain = subIds.Count == 0 ? new List<PlanSubscription>() : await db.PlanSubscriptions.AsNoTracking()
            .Where(r => r.SubscriptionId != null && subIds.Contains(r.SubscriptionId.Value) && !r.IsDeleted && !r.IsTrial)
            .ToListAsync(ct);
        var latestBySub = chain.Where(r => !PlanSubscriptionStatuses.IsTerminal(r.Status))
            .GroupBy(r => r.SubscriptionId!.Value)
            .ToDictionary(g => g.Key, g => g.MaxBy(r => r.EndsAt ?? DateTime.MinValue)!.Id);

        var paymentIds = records.Where(r => r.PaymentId != null).Select(r => r.PaymentId!.Value).Distinct().ToList();
        var payments = paymentIds.Count == 0 ? new Dictionary<int, (string Ref, bool HasReceipt)>() : (await db.Payments.AsNoTracking()
                .Where(p => paymentIds.Contains(p.Id))
                .Select(p => new { p.Id, p.ReferenceNo, p.GeneratedReceiptFileName })
                .ToListAsync(ct))
            .ToDictionary(p => p.Id, p => (Ref: p.ReferenceNo, HasReceipt: !string.IsNullOrEmpty(p.GeneratedReceiptFileName)));

        var names = await ProviderNames.ResolveAsync(db, records.Select(r => (r.ProviderType, r.ProviderId)), ct);

        return records.Select(r =>
        {
            var row = r.SubscriptionId is int sid ? legacy.FirstOrDefault(s => s.Id == sid) : null;
            var latest = r.SubscriptionId is int sid2 && latestBySub.TryGetValue(sid2, out var lid) && lid == r.Id;
            var st = PlanSubscriptionState.Compute(r, row, grace, latest, now);
            var pay = r.PaymentId is int pid && payments.TryGetValue(pid, out var p) ? p : default;
            return new PlanSubscriptionDto(
                r.Id, r.ProviderType, r.ProviderId, names.GetValueOrDefault((r.ProviderType, r.ProviderId)),
                r.BillingPlanId, r.PlanKey, r.PlanCategory, Lang.Pick(r.PlanNameAr, r.PlanNameEn), r.PlanNameAr, r.PlanNameEn,
                r.PriceOptionId, BillingJson.Read<PriceOptionSnapshot>(r.PriceOptionSnapshot), BillingJson.ReadRule(r.PointsRuleSnapshot),
                r.IsTrial, r.StartsAt, r.EndsAt, r.ScansIncluded, r.ScansUsed, st.ScansRemaining, r.ScansCarriedOver,
                st.Status, st.IsOn, st.GraceEndsAt, r.PricePaidJod, r.CreditAppliedJod,
                r.PaymentId, pay.Ref, pay.HasReceipt ? string.Format(SubscriptionDtoMapper.ReceiptPathFormat, r.PaymentId) : null,
                r.SubscriptionId, r.WalletTransactionId, r.UpgradedFromId, r.Source, r.CreatedByUserId, r.CreatedAt,
                r.CancelledAt, r.CancelReason, r.Note);
        }).ToList();
    }
}

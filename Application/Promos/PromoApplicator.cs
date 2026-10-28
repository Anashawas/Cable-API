using System.Globalization;
using Application.Common.Extensions;
using Application.Common.Utilities;
using Application.Partners.Queries.PreviewPartnerCode;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Application.Promos;

/// <summary>
/// Promo codes at partner-scan time (document §2.8–2.9). The partner handlers only call in here, so a scan without
/// a promo is untouched:
/// <list type="bullet">
/// <item>PreviewPartnerCode → <see cref="DecoratePreviewAsync"/> (eligiblePromos, and the computed promo for userPromoId);</item>
/// <item>ScanPartnerCode → <see cref="ApplyAtScanAsync"/> before the settlement upsert, <see cref="RecordInSettlementAsync"/>
/// after its SaveChanges — both inside the completion transaction, so a failed scan leaves the promo Available.</item>
/// </list>
/// </summary>
public class PromoApplicator(IApplicationDbContext db, IBackgroundJobClient jobs)
{
    // ---------------------------------------------------------------- preview (read-only)

    /// <summary>
    /// Adds eligiblePromos to the preview; with <paramref name="userPromoId"/> also the computed promo, with the
    /// top-level points (basePoints, multiplier, pointsToBeAwarded) recomputed on the final amount. Writes nothing.
    /// </summary>
    public async Task<PreviewPartnerCodeResult> DecoratePreviewAsync(PreviewPartnerCodeResult result,
        PartnerTransaction transaction, int userId, int? userPromoId, double? boostMultiplier,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var mine = await db.UserPromos.AsNoTracking()
            .Include(u => u.Campaign)
            .Where(u => u.UserId == userId && u.Status == UserPromoStatus.Available && !u.IsDeleted
                        && (u.ExpiresAt == null || u.ExpiresAt > now)
                        && u.Campaign.IsActive && !u.Campaign.IsDeleted)
            .ToListAsync(cancellationToken);

        // No promos and none asked for: the preview stays exactly as it was (plus an empty list).
        if (mine.Count == 0 && userPromoId is null)
            return result with { EligiblePromos = [] };

        var context = await ContextAsync(transaction, cancellationToken);
        var eligible = new List<EligiblePromoDto>();
        foreach (var up in mine)
        {
            var q = QuoteFor(up.Campaign, transaction, boostMultiplier);
            if (PromoRules.WhyNotApplicable(up.Campaign, up.Status, up.ExpiresAt, context, q.CableShareJod, now) is null)
                eligible.Add(new EligiblePromoDto(up.Id, PromoRules.Title(up.Campaign), up.Campaign.EffectType,
                    up.Campaign.EffectValue, up.Campaign.MaxDiscountJod, up.ExpiresAt, q.DiscountJod, q.FinalAmountJod, q.BonusPoints));
        }

        var decorated = result with
        {
            EligiblePromos = eligible.OrderBy(e => e.ExpiresAt ?? DateTime.MaxValue).ThenBy(e => e.UserPromoId).ToList()
        };
        if (userPromoId is null)
            return decorated;

        var chosen = mine.FirstOrDefault(u => u.Id == userPromoId.Value)
                     ?? await db.UserPromos.AsNoTracking().Include(u => u.Campaign)
                         .FirstOrDefaultAsync(u => u.Id == userPromoId.Value && u.UserId == userId && !u.IsDeleted, cancellationToken)
                     ?? throw PromoErrorCodes.Error(PromoErrorCodes.NotFound);

        var quote = QuoteFor(chosen.Campaign, transaction, boostMultiplier);
        var reason = PromoRules.WhyNotApplicable(chosen.Campaign, chosen.Status, chosen.ExpiresAt, context, quote.CableShareJod, now);
        if (reason == PromoErrorCodes.BudgetExhausted)
            EnqueueBudgetCheck(chosen.PromoCampaignId, quote.CableShareJod);
        if (reason is not null)
            throw PromoErrorCodes.Error(reason);

        var (canConfirm, blockReason) = (result.CanConfirm, result.BlockReason);
        if (quote.FinalPoints <= 0)
            (canConfirm, blockReason) = (true, null);               // nothing to award → a loyalty block can't fail the scan
        else if (result.PointsToBeAwarded <= 0)
            (canConfirm, blockReason) = await BlockAsync(userId, now, cancellationToken); // never checked for a 0-point code

        var (noPointsReason, minimum) = WhyNoPoints(transaction, quote, result.PointsUnavailableReason, result.MinimumAmountForPoints);

        return decorated with
        {
            Promo = new PreviewPromoDto(chosen.Id, PromoRules.Title(chosen.Campaign), chosen.Campaign.EffectType,
                quote.DiscountJod, quote.FinalAmountJod, quote.BonusPoints),
            BasePoints = quote.BasePoints,
            PointsToBeAwarded = quote.FinalPoints,
            Multiplier = quote.Multiplier,
            IsWelcomeBonus = result.IsWelcomeBonus && !quote.PromoMultiplierWins,
            CanConfirm = canConfirm,
            BlockReason = blockReason,
            PointsAvailable = quote.FinalPoints > 0,
            PointsUnavailableReason = noPointsReason,
            MinimumAmountForPoints = minimum
        };
    }

    // ---------------------------------------------------------------- scan (inside the completion transaction)

    /// <summary>
    /// Applies the chosen promo to the transaction being completed: points on the final amount (the higher of the
    /// promo multiplier and the boost), commission on what the partner received (the difference goes back to his
    /// wallet), the promo marked Used, the budget spent, a <see cref="PromoUsage"/> row. Does not SaveChanges — the
    /// handler's SaveChanges after the settlement upsert does. Returns null (and changes nothing) without a promo.
    /// </summary>
    public async Task<AppliedPromo?> ApplyAtScanAsync(PartnerTransaction transaction, int? userPromoId, int userId,
        double? boostMultiplier, CancellationToken cancellationToken)
    {
        if (userPromoId is null)
            return null;

        var now = DateTime.UtcNow;

        // Locks: the user promo first (two scans with the same promo → the second sees Used), then the campaign
        // (budget and counters). Always in this order.
        var userPromo = await db.UserPromos.FindWithLockAsync("UserPromo", userPromoId.Value, cancellationToken);
        if (userPromo is null || userPromo.UserId != userId)
            throw PromoErrorCodes.Error(PromoErrorCodes.NotFound);

        var campaign = await db.PromoCampaigns.FindWithLockAsync("PromoCampaign", userPromo.PromoCampaignId, cancellationToken)
                       ?? throw PromoErrorCodes.Error(PromoErrorCodes.NotApplicable);

        var context = await ContextAsync(transaction, cancellationToken);
        var quote = QuoteFor(campaign, transaction, boostMultiplier);

        var reason = PromoRules.WhyNotApplicable(campaign, userPromo.Status, userPromo.ExpiresAt, context, quote.CableShareJod, now);
        if (reason == PromoErrorCodes.BudgetExhausted)
            EnqueueBudgetCheck(campaign.Id, quote.CableShareJod);   // outlives this request's rollback
        if (reason is not null)
            throw PromoErrorCodes.Error(reason);

        // Points: on the final amount; a winning promo multiplier replaces the boost (never multiplied together).
        transaction.BasePoints = quote.BasePoints;
        transaction.PointsAwarded = quote.FinalPoints;
        if (quote.PromoMultiplierWins)
        {
            transaction.AppliedBoostId = null;
            transaction.BoostMultiplier = null;
            transaction.IsWelcomeBonus = false;
        }

        // Commission: on final + Cable's share; refund the difference of what was reserved at InitiatePartnerTransaction.
        if (quote.CommissionRefundJod > 0)
        {
            transaction.CommissionAmount = quote.CommissionAfterJod;
            transaction.WalletCoveredAmount = Math.Min(transaction.WalletCoveredAmount, quote.CommissionAfterJod);
            await RefundCommissionAsync(transaction, quote, userId, cancellationToken);
        }

        userPromo.Status = UserPromoStatus.Used;
        userPromo.UsedAt = now;

        campaign.CableSpentJod += quote.CableShareJod;
        campaign.UsesCount += 1;
        if (PromoRules.IsBudgetExhausted(campaign.CableBudgetJod, campaign.CableSpentJod))
        {
            campaign.IsActive = false;
            campaign.DeactivatedReason = PromoDeactivationReasons.BudgetExhausted;
        }

        var usage = new PromoUsage
        {
            UserPromoId = userPromo.Id,
            PromoCampaignId = campaign.Id,
            UserId = userId,
            TransactionType = PromoRules.PartnerScan,
            TransactionId = transaction.Id,
            ProviderType = transaction.ProviderType,
            ProviderId = transaction.ProviderId,
            OriginalAmountJod = quote.OriginalAmountJod,
            DiscountJod = quote.DiscountJod,
            FinalAmountJod = quote.FinalAmountJod,
            CableShareJod = quote.CableShareJod,
            ProviderShareJod = quote.ProviderShareJod,
            CommissionBeforeJod = quote.CommissionBeforeJod,
            CommissionAfterJod = quote.CommissionAfterJod,
            BasePoints = quote.BasePoints,
            BonusPoints = quote.BonusPoints,
            FinalPoints = quote.FinalPoints,
            CreatedAt = now
        };
        db.PromoUsages.Add(usage);

        return new AppliedPromo(usage, PromoRules.Title(campaign));
    }

    /// <summary>
    /// After the settlement upsert is saved: adds Cable's share to the week's settlement ("discounts paid by Cable",
    /// owed to the partner, so NetBalance goes up by it) and links the usage to that settlement. No-op without a promo.
    /// </summary>
    public async Task RecordInSettlementAsync(PartnerTransaction transaction, AppliedPromo? promo, CancellationToken cancellationToken)
    {
        if (promo is null)
            return;

        var (year, week) = SettlementWeek(transaction.CompletedAt ?? DateTime.UtcNow);
        var settlement = db.ProviderSettlements.Where(x => x.ProviderType == transaction.ProviderType
                                                           && x.ProviderId == transaction.ProviderId
                                                           && x.PeriodType == (int)SettlementPeriodType.Weekly
                                                           && x.PeriodYear == year
                                                           && x.PeriodWeek == week
                                                           && !x.IsDeleted);

        var share = promo.Usage.CableShareJod;
        if (share > 0)
            await settlement.ExecuteUpdateAsync(s => s
                .SetProperty(x => x.PromoCableShareAmount, x => x.PromoCableShareAmount + share)
                .SetProperty(x => x.NetBalance, x => x.NetBalance + share)
                .SetProperty(x => x.ModifiedAt, DateTime.UtcNow), cancellationToken);

        promo.Usage.SettledInSettlementId = await settlement.Select(x => (int?)x.Id).FirstOrDefaultAsync(cancellationToken);
        await db.SaveChanges(cancellationToken);
    }

    // ---------------------------------------------------------------- helpers

    public static PromoQuote QuoteFor(PromoCampaign c, PartnerTransaction t, double? boostMultiplier) =>
        PromoMath.Quote(c.EffectType, c.EffectValue, c.MaxDiscountJod, c.CableSharePercent,
            t.TransactionAmount ?? 0, t.CommissionAmount ?? 0, t.CommissionPercentage,
            t.PointsRewardPercentage, t.PointsConversionRate, t.BasePoints ?? t.PointsAwarded ?? 0, boostMultiplier);

    private async Task<PromoTransactionContext> ContextAsync(PartnerTransaction t, CancellationToken ct)
    {
        int? categoryId = null;
        string? city = null;
        if (t.ProviderType == PromoRules.ChargingPoint)
        {
            city = await db.ChargingPoints.AsNoTracking().Where(x => x.Id == t.ProviderId)
                .Select(x => x.CityName).FirstOrDefaultAsync(ct);
        }
        else if (t.ProviderType == PromoRules.ServiceProvider)
        {
            var sp = await db.ServiceProviders.AsNoTracking().Where(x => x.Id == t.ProviderId)
                .Select(x => new { x.ServiceCategoryId, x.CityName }).FirstOrDefaultAsync(ct);
            categoryId = sp?.ServiceCategoryId;
            city = sp?.CityName;
        }

        return new PromoTransactionContext(PromoRules.PartnerScan, t.ProviderType, t.ProviderId, categoryId,
            JordanCityResolver.Resolve(city) ?? city?.Trim(), t.TransactionAmount ?? 0);
    }

    private async Task RefundCommissionAsync(PartnerTransaction t, PromoQuote quote, int userId, CancellationToken ct)
    {
        var refund = quote.CommissionRefundJod;
        decimal? balanceAfter = null;
        if (t.ProviderType == PromoRules.ChargingPoint)
        {
            var cp = await db.ChargingPoints.FindWithLockAsync("ChargingPoint", t.ProviderId, ct);
            if (cp is not null)
            {
                cp.WalletBalance += refund;
                balanceAfter = cp.WalletBalance;
            }
        }
        else if (t.ProviderType == PromoRules.ServiceProvider)
        {
            var sp = await db.ServiceProviders.FindWithLockAsync("ServiceProvider", t.ProviderId, ct);
            if (sp is not null)
            {
                sp.WalletBalance += refund;
                balanceAfter = sp.WalletBalance;
            }
        }

        if (balanceAfter is null)
            return;

        db.ProviderWalletTransactions.Add(new ProviderWalletTransaction
        {
            ProviderType = t.ProviderType,
            ProviderId = t.ProviderId,
            TransactionType = (int)WalletTransactionType.PromoCommissionRefund,
            Amount = refund,
            BalanceAfter = balanceAfter.Value,
            ReferenceType = "PartnerTransaction",
            ReferenceId = t.Id,
            Note = string.Create(CultureInfo.InvariantCulture,
                $"Commission lowered by a promo: partner received {quote.FinalAmountJod + quote.CableShareJod:F3} of {quote.OriginalAmountJod:F3} ({t.TransactionCode})"),
            RecordedByUserId = userId
        });
    }

    private void EnqueueBudgetCheck(int campaignId, decimal neededJod) =>
        jobs.Enqueue<IGiftsAndPromosJobs>(j => j.DeactivateCampaignIfBudgetShortAsync(campaignId, neededJod, CancellationToken.None));

    private async Task<(bool CanConfirm, string? BlockReason)> BlockAsync(int userId, DateTime now, CancellationToken ct)
    {
        var wallet = await db.UserLoyaltyAccounts.AsNoTracking()
            .Where(w => w.UserId == userId && !w.IsDeleted)
            .Select(w => new { w.IsBlocked, w.BlockedUntil })
            .FirstOrDefaultAsync(ct);
        var blocked = wallet is { IsBlocked: true } && (!wallet.BlockedUntil.HasValue || wallet.BlockedUntil.Value >= now);
        return blocked ? (false, "Your loyalty account is currently blocked. Please contact support.") : (true, null);
    }

    private static (string? Reason, decimal? Minimum) WhyNoPoints(PartnerTransaction t, PromoQuote quote,
        string? originalReason, decimal? originalMinimum)
    {
        if (quote.FinalPoints > 0) return (null, null);
        if (originalReason == PointsUnavailableReasons.PartnerInactive) return (originalReason, null);
        if (quote.FinalAmountJod <= 0) return (PromoPointsReasons.FullyDiscounted, null);

        var pointsPerJod = t.PointsConversionRate * t.PointsRewardPercentage / 100.0;
        if (pointsPerJod <= 0) return (PointsUnavailableReasons.PartnerInactive, null);
        var minimum = originalMinimum ?? Math.Ceiling((decimal)(1.0 / pointsPerJod) * 1000m) / 1000m;
        return (PointsUnavailableReasons.BelowMinimumAmount, minimum);
    }

    /// <summary>Same Sunday–Saturday rule as SettlementService.</summary>
    private static (int Year, int Week) SettlementWeek(DateTime date) =>
        (date.Year, CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(date, CalendarWeekRule.FirstDay, DayOfWeek.Sunday));
}

/// <summary>The promo of completed partner transactions, for the partner's transaction list / detail.</summary>
public static class PartnerPromoView
{
    public static async Task<Dictionary<int, PartnerTransactionPromoDto>> ForTransactionsAsync(
        IApplicationDbContext db, IReadOnlyCollection<int> transactionIds, CancellationToken ct)
    {
        if (transactionIds.Count == 0)
            return new Dictionary<int, PartnerTransactionPromoDto>();

        var rows = await db.PromoUsages.AsNoTracking()
            .Where(u => u.TransactionType == PromoRules.PartnerScan && transactionIds.Contains(u.TransactionId))
            .Select(u => new
            {
                u.TransactionId, u.Campaign.TitleAr, u.Campaign.TitleEn, u.OriginalAmountJod, u.DiscountJod,
                u.FinalAmountJod, u.CableShareJod, u.ProviderShareJod
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.TransactionId)
            .ToDictionary(g => g.Key, g =>
            {
                var r = g.First();
                return new PartnerTransactionPromoDto(Cable.Core.Utilities.Lang.Pick(r.TitleAr, r.TitleEn),
                    r.OriginalAmountJod, r.DiscountJod, r.FinalAmountJod, r.CableShareJod, r.ProviderShareJod);
            });
    }
}

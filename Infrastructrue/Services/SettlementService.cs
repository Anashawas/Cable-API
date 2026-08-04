using System.Globalization;
using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructrue.Services;

public class SettlementService(
    IApplicationDbContext applicationDbContext,
    ILogger<SettlementService> logger) : ISettlementService
{
    public async Task UpsertSettlementForPartnerTransactionAsync(
        PartnerTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var now = transaction.CompletedAt ?? DateTime.UtcNow;
        var (year, week) = GetSundaySaturdayWeek(now);

        var ownerId = await ResolveProviderOwnerId(
            transaction.ProviderType, transaction.ProviderId, cancellationToken);

        await UpsertSettlement(
            transaction.ProviderType,
            transaction.ProviderId,
            ownerId,
            year,
            week,
            partnerTransactionCount: 1,
            partnerTransactionAmount: transaction.TransactionAmount ?? 0,
            partnerCommissionAmount: transaction.CommissionAmount ?? 0,
            totalPointsAwarded: transaction.PointsAwarded ?? 0,
            offerTransactionCount: 0,
            offerPaymentAmount: 0m,
            totalPointsDeducted: 0,
            walletCoveredAmount: transaction.WalletCoveredAmount,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Settlement upserted for partner transaction {TransactionId}, provider {ProviderType}/{ProviderId}, period {Year}-W{Week:D2}",
            transaction.Id, transaction.ProviderType, transaction.ProviderId, year, week);
    }

    public async Task UpsertSettlementForOfferTransactionAsync(
        OfferTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var now = transaction.CompletedAt ?? DateTime.UtcNow;
        var (year, week) = GetSundaySaturdayWeek(now);

        var ownerId = await applicationDbContext.ProviderOffers
            .AsNoTracking()
            .Where(x => x.Id == transaction.ProviderOfferId)
            .Select(x => x.ProposedByUserId)
            .FirstOrDefaultAsync(cancellationToken);

        await UpsertSettlement(
            transaction.ProviderType,
            transaction.ProviderId,
            ownerId,
            year,
            week,
            partnerTransactionCount: 0,
            partnerTransactionAmount: 0m,
            partnerCommissionAmount: 0m,
            totalPointsAwarded: 0,
            offerTransactionCount: 1,
            offerPaymentAmount: transaction.MonetaryValue,
            totalPointsDeducted: transaction.PointsDeducted,
            walletCoveredAmount: 0,  // Offer payments credit the provider — no wallet was "used" to cover debt
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Settlement upserted for offer transaction {TransactionId}, provider {ProviderType}/{ProviderId}, period {Year}-W{Week:D2}, monetaryValue {MonetaryValue}",
            transaction.Id, transaction.ProviderType, transaction.ProviderId, year, week, transaction.MonetaryValue);
    }

    private async Task UpsertSettlement(
        string providerType, int providerId, int ownerId,
        int year, int week,
        int partnerTransactionCount, decimal partnerTransactionAmount,
        decimal partnerCommissionAmount, int totalPointsAwarded,
        int offerTransactionCount, decimal offerPaymentAmount,
        int totalPointsDeducted,
        decimal walletCoveredAmount = 0,
        CancellationToken cancellationToken = default)
    {
        // Use ExecuteUpdateAsync for atomic SQL-level increment (prevents lost updates under concurrency).
        // NetBalance = OfferPaymentAmount - PartnerCommissionAmount
        // Positive = Cable owes provider, Negative = Provider owes Cable
        var updated = await applicationDbContext.ProviderSettlements
            .Where(x => x.ProviderType == providerType
                         && x.ProviderId == providerId
                         && x.PeriodType == (int)SettlementPeriodType.Weekly
                         && x.PeriodYear == year
                         && x.PeriodWeek == week
                         && !x.IsDeleted)
            .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.PartnerTransactionCount, x => x.PartnerTransactionCount + partnerTransactionCount)
                    .SetProperty(x => x.PartnerTransactionAmount, x => x.PartnerTransactionAmount + partnerTransactionAmount)
                    .SetProperty(x => x.PartnerCommissionAmount, x => x.PartnerCommissionAmount + partnerCommissionAmount)
                    .SetProperty(x => x.TotalPointsAwarded, x => x.TotalPointsAwarded + totalPointsAwarded)
                    .SetProperty(x => x.OfferTransactionCount, x => x.OfferTransactionCount + offerTransactionCount)
                    .SetProperty(x => x.OfferPaymentAmount, x => x.OfferPaymentAmount + offerPaymentAmount)
                    .SetProperty(x => x.TotalPointsDeducted, x => x.TotalPointsDeducted + totalPointsDeducted)
                    .SetProperty(x => x.NetBalance, x =>
                        (x.OfferPaymentAmount + offerPaymentAmount)
                        - (x.PartnerCommissionAmount + partnerCommissionAmount))
                    .SetProperty(x => x.WalletApplied, x => x.WalletApplied + walletCoveredAmount)
                    .SetProperty(x => x.ModifiedAt, DateTime.UtcNow),
                cancellationToken);

        if (updated > 0)
            return;

        // No settlement exists for this provider/period — create a new one.
        // NetBalance = OfferPaymentAmount - PartnerCommissionAmount
        var netBalance = offerPaymentAmount - partnerCommissionAmount;

        var settlement = new ProviderSettlement
        {
            ProviderType = providerType,
            ProviderId = providerId,
            ProviderOwnerId = ownerId,
            PeriodType = (int)SettlementPeriodType.Weekly,
            PeriodYear = year,
            PeriodMonth = 0,
            PeriodWeek = week,
            PartnerTransactionCount = partnerTransactionCount,
            PartnerTransactionAmount = partnerTransactionAmount,
            PartnerCommissionAmount = partnerCommissionAmount,
            TotalPointsAwarded = totalPointsAwarded,
            OfferTransactionCount = offerTransactionCount,
            OfferPaymentAmount = offerPaymentAmount,
            TotalPointsDeducted = totalPointsDeducted,
            NetBalance = netBalance,
            WalletApplied = walletCoveredAmount,
            SettlementStatus = (int)SettlementStatus.Pending
        };

        applicationDbContext.ProviderSettlements.Add(settlement);
    }

    /// <summary>
    /// Calculates the week number using Sunday-Saturday week boundaries.
    /// Returns (year, weekNumber) where week 1 starts on the first Sunday of the year.
    /// </summary>
    private static (int Year, int Week) GetSundaySaturdayWeek(DateTime date)
    {
        var calendar = CultureInfo.InvariantCulture.Calendar;
        var week = calendar.GetWeekOfYear(date, CalendarWeekRule.FirstDay, DayOfWeek.Sunday);
        return (date.Year, week);
    }

    private async Task<int> ResolveProviderOwnerId(
        string providerType, int providerId, CancellationToken cancellationToken)
    {
        if (providerType == "ChargingPoint")
            return await applicationDbContext.ChargingPoints
                .Where(x => x.Id == providerId)
                .Select(x => x.OwnerId)
                .FirstOrDefaultAsync(cancellationToken) ?? 0;

        if (providerType == "ServiceProvider")
            return await applicationDbContext.ServiceProviders
                .Where(x => x.Id == providerId)
                .Select(x => x.OwnerId)
                .FirstOrDefaultAsync(cancellationToken) ?? 0;

        return 0;
    }
}

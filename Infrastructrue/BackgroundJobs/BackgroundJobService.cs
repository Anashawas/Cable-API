using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructrue.BackgroundJobs;

public class BackgroundJobService(
    IApplicationDbContext applicationDbContext,
    ILogger<BackgroundJobService> logger) : IBackgroundJobService
{
    public async Task<int> ExpireOfferTransactionCodesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var expiredTransactions = await applicationDbContext.OfferTransactions
            .Where(x => x.Status == (int)OfferTransactionStatus.Initiated
                         && x.CodeExpiresAt < now
                         && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var transaction in expiredTransactions)
        {
            transaction.Status = (int)OfferTransactionStatus.Expired;
        }

        if (expiredTransactions.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("ExpireOfferTransactionCodes: Expired {Count} offer transactions", expiredTransactions.Count);

        return expiredTransactions.Count;
    }

    public async Task<int> ExpirePartnerTransactionCodesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var expiredTransactions = await applicationDbContext.PartnerTransactions
            .Where(x => x.Status == (int)PartnerTransactionStatus.Initiated
                         && x.CodeExpiresAt < now
                         && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var transaction in expiredTransactions)
        {
            transaction.Status = (int)PartnerTransactionStatus.Expired;
        }

        // Refund reserved commission to provider balances
        var grouped = expiredTransactions
            .Where(t => t.CommissionAmount is > 0)
            .GroupBy(t => new { t.ProviderType, t.ProviderId });

        foreach (var group in grouped)
        {
            var totalRefund = group.Sum(t => t.CommissionAmount!.Value);
            if (group.Key.ProviderType == "ChargingPoint")
            {
                var cp = await applicationDbContext.ChargingPoints
                    .FirstOrDefaultAsync(x => x.Id == group.Key.ProviderId && !x.IsDeleted, cancellationToken);
                if (cp != null) cp.LoyaltyCurrentBalance += totalRefund;
            }
            else if (group.Key.ProviderType == "ServiceProvider")
            {
                var sp = await applicationDbContext.ServiceProviders
                    .FirstOrDefaultAsync(x => x.Id == group.Key.ProviderId && !x.IsDeleted, cancellationToken);
                if (sp != null) sp.LoyaltyCurrentBalance += totalRefund;
            }
        }

        if (expiredTransactions.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("ExpirePartnerTransactionCodes: Expired {Count} partner transactions", expiredTransactions.Count);

        return expiredTransactions.Count;
    }

    public async Task<int> GenerateMonthlySettlementsAsync(int year, int month,
        CancellationToken cancellationToken = default)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1);
        var settlementsCreated = 0;

        // ==========================================
        // 1. Process Offer Transactions (points deducted, fixed monetary value)
        // ==========================================
        var completedOfferTransactions = await applicationDbContext.OfferTransactions
            .Where(x => x.Status == (int)OfferTransactionStatus.Completed
                         && !x.IsDeleted
                         && x.CompletedAt >= startDate
                         && x.CompletedAt < endDate)
            .ToListAsync(cancellationToken);

        var offerGroups = completedOfferTransactions
            .GroupBy(x => new { x.ProviderType, x.ProviderId })
            .ToList();

        foreach (var group in offerGroups)
        {
            var totalMonetaryValue = group.Sum(x => x.MonetaryValue);
            var totalPointsDeducted = group.Sum(x => x.PointsDeducted);

            var firstTransaction = group.First();
            var offer = await applicationDbContext.ProviderOffers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == firstTransaction.ProviderOfferId, cancellationToken);

            var ownerId = offer?.ProposedByUserId ?? 0;

            settlementsCreated += await UpsertSettlement(
                group.Key.ProviderType, group.Key.ProviderId, ownerId,
                year, month,
                partnerTransactionCount: 0,
                partnerTransactionAmount: 0m,
                partnerCommissionAmount: 0m,
                totalPointsAwarded: 0,
                offerTransactionCount: group.Count(),
                offerPaymentAmount: totalMonetaryValue,
                totalPointsDeducted: totalPointsDeducted,
                cancellationToken);
        }

        // ==========================================
        // 2. Process Partner Transactions (points awarded, commission-based)
        // ==========================================
        var completedPartnerTransactions = await applicationDbContext.PartnerTransactions
            .Where(x => x.Status == (int)PartnerTransactionStatus.Completed
                         && !x.IsDeleted
                         && x.CompletedAt >= startDate
                         && x.CompletedAt < endDate)
            .ToListAsync(cancellationToken);

        var partnerGroups = completedPartnerTransactions
            .GroupBy(x => new { x.ProviderType, x.ProviderId })
            .ToList();

        foreach (var group in partnerGroups)
        {
            var totalTransactionAmount = group.Sum(x => x.TransactionAmount ?? 0);
            var totalCommissionAmount = group.Sum(x => x.CommissionAmount ?? 0);
            var totalPointsAwarded = group.Sum(x => x.PointsAwarded ?? 0);

            var ownerId = await ResolveProviderOwnerId(
                group.Key.ProviderType, group.Key.ProviderId, cancellationToken);

            settlementsCreated += await UpsertSettlement(
                group.Key.ProviderType, group.Key.ProviderId, ownerId,
                year, month,
                partnerTransactionCount: group.Count(),
                partnerTransactionAmount: totalTransactionAmount,
                partnerCommissionAmount: totalCommissionAmount,
                totalPointsAwarded: totalPointsAwarded,
                offerTransactionCount: 0,
                offerPaymentAmount: 0m,
                totalPointsDeducted: 0,
                cancellationToken);
        }

        await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("GenerateMonthlySettlements: Created {Count} settlements for {Year}-{Month:D2}",
            settlementsCreated, year, month);

        return settlementsCreated;
    }

    private async Task<int> UpsertSettlement(
        string providerType, int providerId, int ownerId,
        int year, int month,
        int partnerTransactionCount, decimal partnerTransactionAmount, decimal partnerCommissionAmount,
        int totalPointsAwarded,
        int offerTransactionCount, decimal offerPaymentAmount,
        int totalPointsDeducted,
        CancellationToken cancellationToken)
    {
        var existingSettlement = await applicationDbContext.ProviderSettlements
            .FirstOrDefaultAsync(x => x.ProviderType == providerType
                                      && x.ProviderId == providerId
                                      && x.PeriodYear == year
                                      && x.PeriodMonth == month
                                      && !x.IsDeleted, cancellationToken);

        if (existingSettlement != null)
        {
            existingSettlement.PartnerTransactionCount += partnerTransactionCount;
            existingSettlement.PartnerTransactionAmount += partnerTransactionAmount;
            existingSettlement.PartnerCommissionAmount += partnerCommissionAmount;
            existingSettlement.TotalPointsAwarded += totalPointsAwarded;
            existingSettlement.OfferTransactionCount += offerTransactionCount;
            existingSettlement.OfferPaymentAmount += offerPaymentAmount;
            existingSettlement.TotalPointsDeducted += totalPointsDeducted;

            // Recalculate net amount
            existingSettlement.NetAmountDueToProvider =
                (existingSettlement.PartnerTransactionAmount - existingSettlement.PartnerCommissionAmount)
                + existingSettlement.OfferPaymentAmount;

            return 0;
        }

        var netAmount = (partnerTransactionAmount - partnerCommissionAmount) + offerPaymentAmount;

        var settlement = new ProviderSettlement
        {
            ProviderType = providerType,
            ProviderId = providerId,
            ProviderOwnerId = ownerId,
            PeriodYear = year,
            PeriodMonth = month,
            PartnerTransactionCount = partnerTransactionCount,
            PartnerTransactionAmount = partnerTransactionAmount,
            PartnerCommissionAmount = partnerCommissionAmount,
            TotalPointsAwarded = totalPointsAwarded,
            OfferTransactionCount = offerTransactionCount,
            OfferPaymentAmount = offerPaymentAmount,
            TotalPointsDeducted = totalPointsDeducted,
            NetAmountDueToProvider = netAmount,
            SettlementStatus = (int)SettlementStatus.Pending
        };

        applicationDbContext.ProviderSettlements.Add(settlement);
        return 1;
    }

    // ==========================================
    // CRITICAL: Security Cleanup
    // ==========================================

    public async Task<int> CleanupExpiredPhoneVerificationsAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);

        var expiredRecords = await applicationDbContext.PhoneVerifications
            .Where(x => !x.IsDeleted
                         && (x.ExpiresAt < cutoff || (x.IsUsed && x.ExpiresAt < DateTime.UtcNow)))
            .ToListAsync(cancellationToken);

        foreach (var record in expiredRecords)
        {
            record.IsDeleted = true;
        }

        if (expiredRecords.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("CleanupExpiredPhoneVerifications: Cleaned up {Count} records", expiredRecords.Count);

        return expiredRecords.Count;
    }

    public async Task<int> CleanupExpiredPasswordResetsAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);

        var expiredRecords = await applicationDbContext.PasswordResets
            .Where(x => x.ExpiresAt < cutoff || (x.IsUsed && x.ExpiresAt < DateTime.UtcNow))
            .ToListAsync(cancellationToken);

        if (expiredRecords.Count > 0)
        {
            applicationDbContext.PasswordResets.RemoveRange(expiredRecords);
            await applicationDbContext.SaveChanges(cancellationToken);
        }

        logger.LogInformation("CleanupExpiredPasswordResets: Cleaned up {Count} records", expiredRecords.Count);

        return expiredRecords.Count;
    }

    public async Task<int> CleanupExpiredOtpRateLimitsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddHours(-24);

        // Unblock numbers where block period has passed
        var blockedRecords = await applicationDbContext.OtpRateLimits
            .Where(x => x.IsBlocked && x.BlockedUntil != null && x.BlockedUntil < now)
            .ToListAsync(cancellationToken);

        foreach (var record in blockedRecords)
        {
            record.IsBlocked = false;
            record.BlockedUntil = null;
        }

        // Delete old rate limit records (older than 24 hours)
        var staleRecords = await applicationDbContext.OtpRateLimits
            .Where(x => x.WindowStart < cutoff && !x.IsBlocked)
            .ToListAsync(cancellationToken);

        if (staleRecords.Count > 0)
            applicationDbContext.OtpRateLimits.RemoveRange(staleRecords);

        var totalAffected = blockedRecords.Count + staleRecords.Count;

        if (totalAffected > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation(
            "CleanupExpiredOtpRateLimits: Unblocked {Unblocked}, deleted {Deleted} stale records",
            blockedRecords.Count, staleRecords.Count);

        return totalAffected;
    }

    // ==========================================
    // IMPORTANT: Business Expiry
    // ==========================================

    public async Task<int> DeactivateExpiredOffersAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var expiredOffers = await applicationDbContext.ProviderOffers
            .Where(x => x.IsActive
                         && x.ValidTo != null
                         && x.ValidTo < now
                         && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var offer in expiredOffers)
        {
            offer.IsActive = false;
        }

        if (expiredOffers.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("DeactivateExpiredOffers: Deactivated {Count} offers", expiredOffers.Count);

        return expiredOffers.Count;
    }

    public async Task<int> DeactivateExpiredSharedLinksAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var expiredLinks = await applicationDbContext.SharedLinks
            .Where(x => x.IsActive
                         && !x.IsDeleted
                         && ((x.ExpiresAt != null && x.ExpiresAt < now)
                              || x.CurrentUsage >= x.MaxUsage))
            .ToListAsync(cancellationToken);

        foreach (var link in expiredLinks)
        {
            link.IsActive = false;
        }

        if (expiredLinks.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("DeactivateExpiredSharedLinks: Deactivated {Count} links", expiredLinks.Count);

        return expiredLinks.Count;
    }

    public async Task<int> EndExpiredLoyaltySeasonsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var expiredSeasons = await applicationDbContext.LoyaltySeasons
            .Where(x => x.IsActive
                         && x.EndDate < now
                         && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var season in expiredSeasons)
        {
            season.IsActive = false;
        }

        if (expiredSeasons.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("EndExpiredLoyaltySeasons: Ended {Count} seasons", expiredSeasons.Count);

        return expiredSeasons.Count;
    }

    public async Task<int> ExpireLoyaltyPointsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Find Earn transactions with ExpiresAt that have passed and haven't been expired yet
        var expiredTransactions = await applicationDbContext.LoyaltyPointTransactions
            .Where(x => x.ExpiresAt != null
                         && x.ExpiresAt < now
                         && x.TransactionType == (int)TransactionType.Earn
                         && x.Points > 0
                         && !x.IsDeleted)
            .Include(x => x.Account)
            .ToListAsync(cancellationToken);

        var expiredCount = 0;

        foreach (var transaction in expiredTransactions)
        {
            var account = transaction.Account;
            if (account == null || account.IsDeleted) continue;

            // Deduct expired points from balance (don't go below 0)
            var pointsToExpire = Math.Min(transaction.Points, account.CurrentBalance);
            if (pointsToExpire <= 0)
            {
                // Mark as expired even if no balance to deduct (prevent reprocessing)
                transaction.TransactionType = (int)TransactionType.Expired;
                transaction.Points = 0;
                expiredCount++;
                continue;
            }

            account.CurrentBalance -= pointsToExpire;
            account.TotalPointsRedeemed += pointsToExpire;

            // Mark original transaction as expired
            transaction.TransactionType = (int)TransactionType.Expired;
            transaction.Points = 0;

            // Create audit trail
            var expiryTransaction = new LoyaltyPointTransaction
            {
                UserLoyaltyAccountId = account.Id,
                TransactionType = (int)TransactionType.Expired,
                Points = -pointsToExpire,
                BalanceAfter = account.CurrentBalance,
                ReferenceType = "PointExpiry",
                ReferenceId = transaction.Id,
                Note = $"Points expired from transaction #{transaction.Id}",
                LoyaltySeasonId = transaction.LoyaltySeasonId
            };

            applicationDbContext.LoyaltyPointTransactions.Add(expiryTransaction);
            expiredCount++;
        }

        if (expiredCount > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("ExpireLoyaltyPoints: Expired {Count} point transactions", expiredCount);

        return expiredCount;
    }

    public async Task<int> DeactivateExpiredRewardsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var expiredRewards = await applicationDbContext.LoyaltyRewards
            .Where(x => x.IsActive
                         && !x.IsDeleted
                         && ((x.ValidTo != null && x.ValidTo < now)
                              || (x.MaxRedemptions != null && x.CurrentRedemptions >= x.MaxRedemptions)))
            .ToListAsync(cancellationToken);

        foreach (var reward in expiredRewards)
        {
            reward.IsActive = false;
        }

        if (expiredRewards.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("DeactivateExpiredRewards: Deactivated {Count} rewards", expiredRewards.Count);

        return expiredRewards.Count;
    }

    public async Task<int> UnblockExpiredLoyaltyBlocksAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var expiredBlocks = await applicationDbContext.UserLoyaltyAccounts
            .Where(x => x.IsBlocked
                         && x.BlockedUntil != null
                         && x.BlockedUntil < now
                         && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var account in expiredBlocks)
        {
            account.IsBlocked = false;
            account.BlockedAt = null;
            account.BlockedUntil = null;
            account.BlockReason = null;
            account.BlockedByUserId = null;
            account.ModifiedAt = now;
        }

        if (expiredBlocks.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("UnblockExpiredLoyaltyBlocks: Unblocked {Count} accounts", expiredBlocks.Count);

        return expiredBlocks.Count;
    }

    public async Task<int> UnblockExpiredProviderLoyaltyBlocksAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var count = 0;

        // ChargingPoints
        var blockedCps = await applicationDbContext.ChargingPoints
            .Where(x => x.IsLoyaltyBlocked
                         && x.LoyaltyBlockedUntil != null
                         && x.LoyaltyBlockedUntil < now
                         && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var cp in blockedCps)
        {
            cp.IsLoyaltyBlocked = false;
            cp.LoyaltyBlockedAt = null;
            cp.LoyaltyBlockedUntil = null;
            cp.LoyaltyBlockReason = null;
            cp.LoyaltyBlockedByUserId = null;
            cp.ModifiedAt = now;
        }

        // ServiceProviders
        var blockedSps = await applicationDbContext.ServiceProviders
            .Where(x => x.IsLoyaltyBlocked
                         && x.LoyaltyBlockedUntil != null
                         && x.LoyaltyBlockedUntil < now
                         && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var sp in blockedSps)
        {
            sp.IsLoyaltyBlocked = false;
            sp.LoyaltyBlockedAt = null;
            sp.LoyaltyBlockedUntil = null;
            sp.LoyaltyBlockReason = null;
            sp.LoyaltyBlockedByUserId = null;
            sp.ModifiedAt = now;
        }

        count = blockedCps.Count + blockedSps.Count;

        if (count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("UnblockExpiredProviderLoyaltyBlocks: Unblocked {Count} providers", count);

        return count;
    }

    // ==========================================
    // Private Helpers
    // ==========================================

    private async Task<int> ResolveProviderOwnerId(
        string providerType, int providerId, CancellationToken cancellationToken)
    {
        if (providerType == "ChargingPoint")
        {
            return await applicationDbContext.ChargingPoints
                .Where(x => x.Id == providerId)
                .Select(x => x.OwnerId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (providerType == "ServiceProvider")
        {
            return await applicationDbContext.ServiceProviders
                .Where(x => x.Id == providerId)
                .Select(x => x.OwnerId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return 0;
    }
}

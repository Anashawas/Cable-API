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

        // Group by offer to decrement usage counters
        var offerGroups = expiredTransactions.GroupBy(x => x.ProviderOfferId).ToList();

        foreach (var transaction in expiredTransactions)
        {
            transaction.Status = (int)OfferTransactionStatus.Expired;
        }

        // Decrement offer usage counters (were incremented at initiation)
        foreach (var group in offerGroups)
        {
            var count = group.Count();
            await applicationDbContext.ProviderOffers
                .Where(x => x.Id == group.Key && x.CurrentTotalUses >= count)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(o => o.CurrentTotalUses, o => o.CurrentTotalUses - count), cancellationToken);
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
            var transactionIds = string.Join(", ", group.Select(t => t.Id));

            if (group.Key.ProviderType == "ChargingPoint")
            {
                var cp = await applicationDbContext.ChargingPoints
                    .FirstOrDefaultAsync(x => x.Id == group.Key.ProviderId && !x.IsDeleted, cancellationToken);
                if (cp != null)
                {
                    cp.WalletBalance += totalRefund;
                    applicationDbContext.ProviderWalletTransactions.Add(new ProviderWalletTransaction
                    {
                        ProviderType = group.Key.ProviderType,
                        ProviderId = group.Key.ProviderId,
                        TransactionType = (int)WalletTransactionType.CommissionRefund,
                        Amount = totalRefund,
                        BalanceAfter = cp.WalletBalance,
                        ReferenceType = "ExpiredPartnerTransactions",
                        Note = $"Batch refund for {group.Count()} expired transactions (IDs: {transactionIds})",
                        RecordedByUserId = cp.OwnerId
                    });
                }
            }
            else if (group.Key.ProviderType == "ServiceProvider")
            {
                var sp = await applicationDbContext.ServiceProviders
                    .FirstOrDefaultAsync(x => x.Id == group.Key.ProviderId && !x.IsDeleted, cancellationToken);
                if (sp != null)
                {
                    sp.WalletBalance += totalRefund;
                    applicationDbContext.ProviderWalletTransactions.Add(new ProviderWalletTransaction
                    {
                        ProviderType = group.Key.ProviderType,
                        ProviderId = group.Key.ProviderId,
                        TransactionType = (int)WalletTransactionType.CommissionRefund,
                        Amount = totalRefund,
                        BalanceAfter = sp.WalletBalance,
                        ReferenceType = "ExpiredPartnerTransactions",
                        Note = $"Batch refund for {group.Count()} expired transactions (IDs: {transactionIds})",
                        RecordedByUserId = sp.OwnerId
                    });
                }
            }
        }

        if (expiredTransactions.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        logger.LogInformation("ExpirePartnerTransactionCodes: Expired {Count} partner transactions", expiredTransactions.Count);

        return expiredTransactions.Count;
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

}

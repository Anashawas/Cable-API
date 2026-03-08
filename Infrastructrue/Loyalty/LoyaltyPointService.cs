using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Infrastructrue.Loyalty;

public class LoyaltyPointService(IApplicationDbContext applicationDbContext) : ILoyaltyPointService
{
    public async Task<int> AwardPointsAsync(
        int userId,
        string actionCode,
        string? referenceType = null,
        int? referenceId = null,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        // 0. Check if user is blocked from loyalty
        await EnsureUserNotBlocked(userId, cancellationToken);

        // 1. Get current active season
        var activeSeason = await applicationDbContext.LoyaltySeasons
            .FirstOrDefaultAsync(s => s.IsActive && !s.IsDeleted, cancellationToken);

        if (activeSeason == null)
            return 0; // No active season, no points

        // 2. Lookup action
        var action = await applicationDbContext.LoyaltyPointActions
            .FirstOrDefaultAsync(a => a.ActionCode == actionCode && a.IsActive && !a.IsDeleted, cancellationToken);

        if (action == null)
            return 0; // Action not found or inactive

        // 3. Check daily limit
        if (action.MaxPerDay.HasValue)
        {
            var todayStart = DateTime.UtcNow.Date;
            var todayCount = await applicationDbContext.LoyaltyPointTransactions
                .CountAsync(t => t.Account.UserId == userId
                                 && t.LoyaltyPointActionId == action.Id
                                 && t.CreatedAt >= todayStart
                                 && !t.IsDeleted, cancellationToken);

            if (todayCount >= action.MaxPerDay.Value)
                return 0; // Daily limit reached
        }

        // 4. Check lifetime limit
        if (action.MaxPerLifetime.HasValue)
        {
            var lifetimeCount = await applicationDbContext.LoyaltyPointTransactions
                .CountAsync(t => t.Account.UserId == userId
                                 && t.LoyaltyPointActionId == action.Id
                                 && !t.IsDeleted, cancellationToken);

            if (lifetimeCount >= action.MaxPerLifetime.Value)
                return 0; // Lifetime limit reached
        }

        // 5. Find or create UserLoyaltyAccount (wallet)
        var wallet = await applicationDbContext.UserLoyaltyAccounts
            .FirstOrDefaultAsync(w => w.UserId == userId && !w.IsDeleted, cancellationToken);

        if (wallet == null)
        {
            wallet = new UserLoyaltyAccount
            {
                UserId = userId,
                TotalPointsEarned = 0,
                TotalPointsRedeemed = 0,
                CurrentBalance = 0,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId
            };
            applicationDbContext.UserLoyaltyAccounts.Add(wallet);
            await applicationDbContext.SaveChanges(cancellationToken);
        }

        // 6. Find or create UserSeasonProgress
        var seasonProgress = await applicationDbContext.UserSeasonProgresses
            .FirstOrDefaultAsync(sp => sp.UserId == userId
                                       && sp.LoyaltySeasonId == activeSeason.Id
                                       && !sp.IsDeleted, cancellationToken);

        if (seasonProgress == null)
        {
            seasonProgress = new UserSeasonProgress
            {
                UserId = userId,
                LoyaltySeasonId = activeSeason.Id,
                SeasonPointsEarned = 0,
                TierLevel = 1, // Bronze
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId
            };
            applicationDbContext.UserSeasonProgresses.Add(seasonProgress);
            await applicationDbContext.SaveChanges(cancellationToken);
        }

        // 7. Get tier multiplier
        var tier = await applicationDbContext.LoyaltyTiers
            .FirstOrDefaultAsync(t => t.Id == seasonProgress.TierLevel && t.IsActive, cancellationToken);

        var multiplier = tier?.Multiplier ?? 1.0;

        // 8. Calculate points with multiplier
        var points = (int)Math.Floor(action.Points * multiplier);
        if (points <= 0)
            return 0;

        // 9. Update wallet
        wallet.TotalPointsEarned += points;
        wallet.CurrentBalance += points;
        wallet.ModifiedAt = DateTime.UtcNow;
        wallet.ModifiedBy = userId;

        // 10. Update season progress
        seasonProgress.SeasonPointsEarned += points;
        seasonProgress.ModifiedAt = DateTime.UtcNow;
        seasonProgress.ModifiedBy = userId;

        // 11. Create transaction
        var transaction = new LoyaltyPointTransaction
        {
            UserLoyaltyAccountId = wallet.Id,
            LoyaltyPointActionId = action.Id,
            LoyaltySeasonId = activeSeason.Id,
            TransactionType = (int)TransactionType.Earn,
            Points = points,
            BalanceAfter = wallet.CurrentBalance,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Note = note ?? $"Earned {points} points for {action.Name}",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };
        applicationDbContext.LoyaltyPointTransactions.Add(transaction);

        // 12. Recalculate tier
        await RecalculateSeasonTier(seasonProgress, cancellationToken);

        // 13. Save
        await applicationDbContext.SaveChanges(cancellationToken);

        return points;
    }

    public async Task<int> AwardPointsFromOfferAsync(
        int userId,
        int calculatedPoints,
        string providerType,
        int providerId,
        int offerTransactionId,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        if (calculatedPoints <= 0)
            return 0;

        // 0. Check if user is blocked from loyalty
        await EnsureUserNotBlocked(userId, cancellationToken);

        // 1. Get current active season
        var activeSeason = await applicationDbContext.LoyaltySeasons
            .FirstOrDefaultAsync(s => s.IsActive && !s.IsDeleted, cancellationToken);

        // 2. Find or create wallet
        var wallet = await applicationDbContext.UserLoyaltyAccounts
            .FirstOrDefaultAsync(w => w.UserId == userId && !w.IsDeleted, cancellationToken);

        if (wallet == null)
        {
            wallet = new UserLoyaltyAccount
            {
                UserId = userId,
                TotalPointsEarned = 0,
                TotalPointsRedeemed = 0,
                CurrentBalance = 0,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId
            };
            applicationDbContext.UserLoyaltyAccounts.Add(wallet);
            await applicationDbContext.SaveChanges(cancellationToken);
        }

        // 3. Get USE_OFFER action for reference
        var useOfferAction = await applicationDbContext.LoyaltyPointActions
            .FirstOrDefaultAsync(a => a.ActionCode == "USE_OFFER" && a.IsActive && !a.IsDeleted, cancellationToken);

        // 4. Update wallet
        wallet.TotalPointsEarned += calculatedPoints;
        wallet.CurrentBalance += calculatedPoints;
        wallet.ModifiedAt = DateTime.UtcNow;
        wallet.ModifiedBy = userId;

        // 5. Update season progress if active season exists
        if (activeSeason != null)
        {
            var seasonProgress = await applicationDbContext.UserSeasonProgresses
                .FirstOrDefaultAsync(sp => sp.UserId == userId
                                           && sp.LoyaltySeasonId == activeSeason.Id
                                           && !sp.IsDeleted, cancellationToken);

            if (seasonProgress == null)
            {
                seasonProgress = new UserSeasonProgress
                {
                    UserId = userId,
                    LoyaltySeasonId = activeSeason.Id,
                    SeasonPointsEarned = 0,
                    TierLevel = 1,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = userId
                };
                applicationDbContext.UserSeasonProgresses.Add(seasonProgress);
                await applicationDbContext.SaveChanges(cancellationToken);
            }

            seasonProgress.SeasonPointsEarned += calculatedPoints;
            seasonProgress.ModifiedAt = DateTime.UtcNow;
            seasonProgress.ModifiedBy = userId;

            await RecalculateSeasonTier(seasonProgress, cancellationToken);
        }

        // 6. Create transaction
        var transaction = new LoyaltyPointTransaction
        {
            UserLoyaltyAccountId = wallet.Id,
            LoyaltyPointActionId = useOfferAction?.Id,
            LoyaltySeasonId = activeSeason?.Id,
            TransactionType = (int)TransactionType.Earn,
            Points = calculatedPoints,
            BalanceAfter = wallet.CurrentBalance,
            ReferenceType = providerType,
            ReferenceId = providerId,
            Note = note ?? $"Earned {calculatedPoints} points from partner transaction #{offerTransactionId}",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };
        applicationDbContext.LoyaltyPointTransactions.Add(transaction);

        // 7. Save
        await applicationDbContext.SaveChanges(cancellationToken);

        return calculatedPoints;
    }

    public async Task<int> DeductPointsFromOfferAsync(
        int userId,
        int pointsToDeduct,
        string providerType,
        int providerId,
        int offerTransactionId,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        if (pointsToDeduct <= 0)
            return 0;

        // 0. Check if user is blocked from loyalty
        await EnsureUserNotBlocked(userId, cancellationToken);

        // 1. Get current active season
        var activeSeason = await applicationDbContext.LoyaltySeasons
            .FirstOrDefaultAsync(s => s.IsActive && !s.IsDeleted, cancellationToken);

        // 2. Get wallet — must exist and have enough balance
        var wallet = await applicationDbContext.UserLoyaltyAccounts
            .FirstOrDefaultAsync(w => w.UserId == userId && !w.IsDeleted, cancellationToken);

        if (wallet == null || wallet.CurrentBalance < pointsToDeduct)
            throw new Cable.Core.DataValidationException("Points",
                $"Insufficient points balance. Required: {pointsToDeduct}, Available: {wallet?.CurrentBalance ?? 0}");

        // 3. Get REDEEM_OFFER action for reference
        var redeemOfferAction = await applicationDbContext.LoyaltyPointActions
            .FirstOrDefaultAsync(a => a.ActionCode == "REDEEM_OFFER" && a.IsActive && !a.IsDeleted, cancellationToken);

        // 4. Debit wallet
        wallet.TotalPointsRedeemed += pointsToDeduct;
        wallet.CurrentBalance -= pointsToDeduct;
        wallet.ModifiedAt = DateTime.UtcNow;
        wallet.ModifiedBy = userId;

        // 5. Create transaction (negative points = deduction)
        var transaction = new LoyaltyPointTransaction
        {
            UserLoyaltyAccountId = wallet.Id,
            LoyaltyPointActionId = redeemOfferAction?.Id,
            LoyaltySeasonId = activeSeason?.Id,
            TransactionType = (int)TransactionType.Redeem,
            Points = -pointsToDeduct,
            BalanceAfter = wallet.CurrentBalance,
            ReferenceType = providerType,
            ReferenceId = providerId,
            Note = note ?? $"Redeemed {pointsToDeduct} points for offer transaction #{offerTransactionId}",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };
        applicationDbContext.LoyaltyPointTransactions.Add(transaction);

        // 6. Save
        await applicationDbContext.SaveChanges(cancellationToken);

        return pointsToDeduct;
    }

    private async Task RecalculateSeasonTier(UserSeasonProgress seasonProgress, CancellationToken cancellationToken)
    {
        var tiers = await applicationDbContext.LoyaltyTiers
            .Where(t => t.IsActive)
            .OrderByDescending(t => t.MinPoints)
            .ToListAsync(cancellationToken);

        foreach (var tier in tiers)
        {
            if (seasonProgress.SeasonPointsEarned >= tier.MinPoints)
            {
                seasonProgress.TierLevel = tier.Id;
                break;
            }
        }
    }

    private async Task EnsureUserNotBlocked(int userId, CancellationToken cancellationToken)
    {
        var wallet = await applicationDbContext.UserLoyaltyAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId && !w.IsDeleted, cancellationToken);

        if (wallet is { IsBlocked: true })
        {
            // Check if temporary block has expired
            if (wallet.BlockedUntil.HasValue && wallet.BlockedUntil.Value < DateTime.UtcNow)
                return; // Block expired, allow — background job will clean up

            throw new Cable.Core.DataValidationException("Loyalty",
                "Your loyalty account is currently blocked. Please contact support.");
        }
    }
}

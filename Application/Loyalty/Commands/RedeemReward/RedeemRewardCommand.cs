using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Commands.RedeemReward;

public record RedeemRewardResult(int RedemptionId, string? RedemptionCode);

public record RedeemRewardCommand(int RewardId) : IRequest<RedeemRewardResult>;

public class RedeemRewardCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<RedeemRewardCommand, RedeemRewardResult>
{
    public async Task<RedeemRewardResult> Handle(RedeemRewardCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        // 1. Get reward
        var reward = await applicationDbContext.LoyaltyRewards
                         .FirstOrDefaultAsync(r => r.Id == request.RewardId && !r.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException($"Reward with Id '{request.RewardId}' not found");

        // 2. Validate reward is active and within dates
        if (!reward.IsActive)
            throw new DataValidationException("Reward", "This reward is no longer active");

        var now = DateTime.UtcNow;
        if (now < reward.ValidFrom)
            throw new DataValidationException("Reward", "This reward is not yet available");

        if (reward.ValidTo.HasValue && now > reward.ValidTo.Value)
            throw new DataValidationException("Reward", "This reward has expired");

        // 3. Check max redemptions
        if (reward.MaxRedemptions.HasValue && reward.CurrentRedemptions >= reward.MaxRedemptions.Value)
            throw new DataValidationException("Reward", "This reward has reached its maximum redemptions");

        // 4. Get wallet
        var wallet = await applicationDbContext.UserLoyaltyAccounts
                         .FirstOrDefaultAsync(w => w.UserId == userId && !w.IsDeleted, cancellationToken)
                     ?? throw new DataValidationException("Balance", "You don't have a loyalty account yet");

        // 4b. Check if user is blocked from loyalty
        if (wallet.IsBlocked)
        {
            if (!wallet.BlockedUntil.HasValue || wallet.BlockedUntil.Value >= DateTime.UtcNow)
                throw new DataValidationException("Loyalty",
                    "Your loyalty account is currently blocked. Please contact support.");
        }

        // 5. Check balance
        if (wallet.CurrentBalance < reward.PointsCost)
            throw new DataValidationException("Balance", $"Insufficient balance. Required: {reward.PointsCost}, Available: {wallet.CurrentBalance}");

        // 6. Debit wallet
        wallet.CurrentBalance -= reward.PointsCost;
        wallet.TotalPointsRedeemed += reward.PointsCost;
        wallet.ModifiedAt = now;
        wallet.ModifiedBy = userId;

        // 7. Create debit transaction
        var transaction = new LoyaltyPointTransaction
        {
            UserLoyaltyAccountId = wallet.Id,
            TransactionType = (int)TransactionType.Redeem,
            Points = -reward.PointsCost,
            BalanceAfter = wallet.CurrentBalance,
            ReferenceType = reward.ProviderType,
            ReferenceId = reward.ProviderId,
            Note = $"Redeemed reward: {reward.Name}",
            CreatedAt = now,
            CreatedBy = userId
        };
        applicationDbContext.LoyaltyPointTransactions.Add(transaction);
        await applicationDbContext.SaveChanges(cancellationToken);

        // 8. Generate redemption code
        var redemptionCode = GenerateRedemptionCode();

        // 9. Create redemption record
        var redemption = new UserRewardRedemption
        {
            UserId = userId,
            LoyaltyRewardId = reward.Id,
            LoyaltyPointTransactionId = transaction.Id,
            PointsSpent = reward.PointsCost,
            Status = (int)RedemptionStatus.Pending,
            RedemptionCode = redemptionCode,
            ProviderType = reward.ProviderType,
            ProviderId = reward.ProviderId,
            RedeemedAt = now,
            CreatedAt = now,
            CreatedBy = userId
        };
        applicationDbContext.UserRewardRedemptions.Add(redemption);

        // 10. Increment reward redemption counter
        reward.CurrentRedemptions++;

        await applicationDbContext.SaveChanges(cancellationToken);

        return new RedeemRewardResult(redemption.Id, redemptionCode);
    }

    private static string GenerateRedemptionCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = new Random();
        var code = new char[6];
        for (var i = 0; i < 6; i++)
            code[i] = chars[random.Next(chars.Length)];
        return $"RWD-{new string(code)}";
    }
}

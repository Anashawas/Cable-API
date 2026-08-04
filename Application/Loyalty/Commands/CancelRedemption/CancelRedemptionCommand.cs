using Application.Common.Security;
using Application.Common.Extensions;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Commands.CancelRedemption;

public record CancelRedemptionCommand(int RedemptionId) : IRequest;

public class CancelRedemptionCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<CancelRedemptionCommand>
{
    public async Task Handle(CancelRedemptionCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var redemption = await applicationDbContext.UserRewardRedemptions
                             .Include(r => r.Reward)
                             .FirstOrDefaultAsync(r => r.Id == request.RedemptionId && !r.IsDeleted, cancellationToken)
                         ?? throw new NotFoundException($"Redemption with Id '{request.RedemptionId}' not found");

        if (redemption.Status != (int)RedemptionStatus.Pending)
            throw new DataValidationException("Status", "Only pending redemptions can be cancelled");

        await using var dbTransaction = await applicationDbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Refund points to wallet with row lock
            var wallet = await applicationDbContext.UserLoyaltyAccounts
                             .FromSqlRaw("SELECT * FROM [UserLoyaltyAccount] WITH (UPDLOCK, ROWLOCK) WHERE UserId = {0} AND IsDeleted = 0", redemption.UserId)
                             .FirstOrDefaultAsync(cancellationToken)
                         ?? throw new NotFoundException("User loyalty account not found");

            var now = DateTime.UtcNow;

            wallet.CurrentBalance += redemption.PointsSpent;
            wallet.TotalPointsRedeemed -= redemption.PointsSpent;
            wallet.ModifiedAt = now;
            wallet.ModifiedBy = userId;

            // Create refund transaction
            var refundTransaction = new LoyaltyPointTransaction
            {
                UserLoyaltyAccountId = wallet.Id,
                TransactionType = (int)TransactionType.AdminAdjust,
                Points = redemption.PointsSpent,
                BalanceAfter = wallet.CurrentBalance,
                ReferenceType = "Redemption",
                ReferenceId = redemption.Id,
                Note = $"Refund for cancelled redemption of: {redemption.Reward.Name}",
                CreatedAt = now,
                CreatedBy = userId
            };
            applicationDbContext.LoyaltyPointTransactions.Add(refundTransaction);

            // Update redemption status
            redemption.Status = (int)RedemptionStatus.Cancelled;
            redemption.ModifiedAt = now;
            redemption.ModifiedBy = userId;

            // Atomically decrement reward counter
            await applicationDbContext.LoyaltyRewards
                .Where(x => x.Id == redemption.LoyaltyRewardId && x.CurrentRedemptions > 0)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(r => r.CurrentRedemptions, r => r.CurrentRedemptions - 1), cancellationToken);

            await applicationDbContext.SaveChanges(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}

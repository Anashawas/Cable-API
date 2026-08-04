using Application.Common.Security;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Commands.ReverseTransaction;

public record ReverseTransactionResult(int ReversalTransactionId, int PointsDelta, int NewBalance);

/// <summary>
/// K1 — audited reversal of a wrong transaction. Creates a compensating,
/// idempotent ledger entry on the user's wallet (row-locked) that references the
/// reversed transaction, so the audit trail shows exactly what was undone, why,
/// and by which admin.
///
/// SCOPE: reverses the POINTS side only. Provider wallet balances and settlements
/// are NOT modified — correct those via the existing wallet adjustment endpoints.
/// Pending redemptions must use CancelRedemption (which also frees reward stock).
/// Admin role required.
/// </summary>
public record ReverseTransactionCommand(
    string ActivityType, // Offer | Partner | Redemption | PointsAdjustment
    int TransactionId,
    string Reason
) : IRequest<ReverseTransactionResult>;

public class ReverseTransactionCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<ReverseTransactionCommand, ReverseTransactionResult>
{
    public async Task<ReverseTransactionResult> Handle(ReverseTransactionCommand request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var adminId = currentUserService.UserId!.Value;

        // Idempotency — a transaction can only be reversed once.
        var reversalRef = $"Reversal:{request.ActivityType}";
        var alreadyReversed = await applicationDbContext.LoyaltyPointTransactions
            .AnyAsync(t => t.ReferenceType == reversalRef
                           && t.ReferenceId == request.TransactionId
                           && !t.IsDeleted, cancellationToken);
        if (alreadyReversed)
            throw new DataValidationException("TransactionId",
                $"{request.ActivityType} transaction {request.TransactionId} has already been reversed");

        // Resolve what to compensate: (owner userId, signed points delta, description).
        var (userId, pointsDelta, description, redemption) = request.ActivityType switch
        {
            "PointsAdjustment" => await ResolveAdjustment(request.TransactionId, cancellationToken),
            "Offer" => await ResolveOffer(request.TransactionId, cancellationToken),
            "Partner" => await ResolvePartner(request.TransactionId, cancellationToken),
            "Redemption" => await ResolveRedemption(request.TransactionId, cancellationToken),
            _ => throw new DataValidationException("ActivityType",
                "ActivityType must be one of: Offer, Partner, Redemption, PointsAdjustment")
        };

        var now = DateTime.UtcNow;

        await using var dbTransaction = await applicationDbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var wallet = await applicationDbContext.UserLoyaltyAccounts
                             .FromSqlRaw("SELECT * FROM [UserLoyaltyAccount] WITH (UPDLOCK, ROWLOCK) WHERE UserId = {0} AND IsDeleted = 0", userId)
                             .FirstOrDefaultAsync(cancellationToken)
                         ?? throw new NotFoundException("User loyalty account not found");

            if (pointsDelta < 0 && wallet.CurrentBalance + pointsDelta < 0)
                throw new DataValidationException("Points",
                    $"Reversal would deduct {Math.Abs(pointsDelta)} points but the user only has {wallet.CurrentBalance}");

            wallet.CurrentBalance += pointsDelta;
            // Keep the lifetime totals consistent with how the original flow moved them.
            switch (request.ActivityType)
            {
                case "Offer" or "Redemption":
                    wallet.TotalPointsRedeemed -= pointsDelta; // refund of a spend
                    break;
                case "Partner":
                    wallet.TotalPointsEarned += pointsDelta; // clawback of awarded points
                    break;
                case "PointsAdjustment" when pointsDelta < 0:
                    wallet.TotalPointsEarned += pointsDelta; // undo a positive award
                    break;
                // Reversing a negative adjustment: the original never touched the
                // lifetime totals, so there is nothing to undo there.
            }
            wallet.ModifiedAt = now;
            wallet.ModifiedBy = adminId;

            var reversal = new LoyaltyPointTransaction
            {
                UserLoyaltyAccountId = wallet.Id,
                TransactionType = (int)TransactionType.AdminAdjust,
                Points = pointsDelta,
                BalanceAfter = wallet.CurrentBalance,
                ReferenceType = reversalRef,
                ReferenceId = request.TransactionId,
                Note = $"Reversal of {description}. Reason: {request.Reason}",
                CreatedAt = now,
                CreatedBy = adminId
            };
            applicationDbContext.LoyaltyPointTransactions.Add(reversal);

            // Reversing a fulfilled redemption also cancels it and frees reward stock.
            if (redemption != null)
            {
                redemption.Status = (int)RedemptionStatus.Cancelled;
                redemption.ModifiedAt = now;
                redemption.ModifiedBy = adminId;

                await applicationDbContext.LoyaltyRewards
                    .Where(x => x.Id == redemption.LoyaltyRewardId && x.CurrentRedemptions > 0)
                    .ExecuteUpdateAsync(x => x
                        .SetProperty(r => r.CurrentRedemptions, r => r.CurrentRedemptions - 1), cancellationToken);
            }

            await applicationDbContext.SaveChanges(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);

            return new ReverseTransactionResult(reversal.Id, pointsDelta, wallet.CurrentBalance);
        }
        catch
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<(int UserId, int Points, string Description, UserRewardRedemption? Redemption)>
        ResolveAdjustment(int id, CancellationToken ct)
    {
        var t = await applicationDbContext.LoyaltyPointTransactions
                    .AsNoTracking()
                    .Include(x => x.Account)
                    .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
                ?? throw new NotFoundException($"can not find points transaction with id {id}");

        if (t.TransactionType != (int)TransactionType.AdminAdjust)
            throw new DataValidationException("TransactionId",
                "Only AdminAdjust ledger entries can be reversed as PointsAdjustment");

        if (t.ReferenceType != null && t.ReferenceType.StartsWith("Reversal:"))
            throw new DataValidationException("TransactionId", "A reversal entry cannot itself be reversed");

        return (t.Account.UserId, -t.Points, $"points adjustment #{t.Id} ({t.Points:+#;-#;0} pts)", null);
    }

    private async Task<(int, int, string, UserRewardRedemption?)> ResolveOffer(int id, CancellationToken ct)
    {
        var t = await applicationDbContext.OfferTransactions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
                ?? throw new NotFoundException($"can not find offer transaction with id {id}");

        if (t.Status != (int)OfferTransactionStatus.Completed)
            throw new DataValidationException("TransactionId", "Only completed offer transactions can be reversed");
        if (t.UserId is null)
            throw new DataValidationException("TransactionId", "Offer transaction has no user to refund");

        return (t.UserId.Value, t.PointsDeducted, $"offer transaction #{t.Id} ({t.OfferCode})", null);
    }

    private async Task<(int, int, string, UserRewardRedemption?)> ResolvePartner(int id, CancellationToken ct)
    {
        var t = await applicationDbContext.PartnerTransactions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
                ?? throw new NotFoundException($"can not find partner transaction with id {id}");

        if (t.Status != (int)PartnerTransactionStatus.Completed)
            throw new DataValidationException("TransactionId", "Only completed partner transactions can be reversed");
        if (t.UserId is null)
            throw new DataValidationException("TransactionId", "Partner transaction has no user");

        return (t.UserId.Value, -(t.PointsAwarded ?? 0), $"partner transaction #{t.Id} ({t.TransactionCode})", null);
    }

    private async Task<(int, int, string, UserRewardRedemption?)> ResolveRedemption(int id, CancellationToken ct)
    {
        var r = await applicationDbContext.UserRewardRedemptions
                    .Include(x => x.Reward)
                    .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
                ?? throw new NotFoundException($"can not find redemption with id {id}");

        if (r.Status == (int)RedemptionStatus.Pending)
            throw new DataValidationException("TransactionId",
                "Pending redemptions should be cancelled via CancelRedemption instead");
        if (r.Status == (int)RedemptionStatus.Cancelled)
            throw new DataValidationException("TransactionId", "This redemption is already cancelled");

        return (r.UserId, r.PointsSpent, $"fulfilled redemption #{r.Id} ({r.Reward.Name})", r);
    }
}

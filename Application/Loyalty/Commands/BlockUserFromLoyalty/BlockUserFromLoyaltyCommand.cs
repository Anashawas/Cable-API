using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Commands.BlockUserFromLoyalty;

public record BlockUserFromLoyaltyCommand(
    int UserId,
    string Reason,
    DateTime? BlockUntil = null
) : IRequest;

public class BlockUserFromLoyaltyCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<BlockUserFromLoyaltyCommand>
{
    public async Task Handle(BlockUserFromLoyaltyCommand request, CancellationToken cancellationToken)
    {
        var adminId = currentUserService.UserId
                      ?? throw new NotAuthorizedAccessException("User not authenticated");

        // Validate target user exists
        var userExists = await applicationDbContext.UserAccounts
            .AnyAsync(u => u.Id == request.UserId && !u.IsDeleted, cancellationToken);

        if (!userExists)
            throw new NotFoundException($"User with Id '{request.UserId}' not found");

        // Get or create wallet
        var wallet = await applicationDbContext.UserLoyaltyAccounts
            .FirstOrDefaultAsync(w => w.UserId == request.UserId && !w.IsDeleted, cancellationToken);

        var now = DateTime.UtcNow;

        if (wallet == null)
        {
            wallet = new UserLoyaltyAccount
            {
                UserId = request.UserId,
                TotalPointsEarned = 0,
                TotalPointsRedeemed = 0,
                CurrentBalance = 0,
                CreatedAt = now,
                CreatedBy = adminId
            };
            applicationDbContext.UserLoyaltyAccounts.Add(wallet);
        }

        if (wallet.IsBlocked)
            throw new DataValidationException("Loyalty", "User is already blocked from the loyalty system");

        // Block the user
        wallet.IsBlocked = true;
        wallet.BlockedAt = now;
        wallet.BlockedUntil = request.BlockUntil;
        wallet.BlockReason = request.Reason;
        wallet.BlockedByUserId = adminId;
        wallet.ModifiedAt = now;
        wallet.ModifiedBy = adminId;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

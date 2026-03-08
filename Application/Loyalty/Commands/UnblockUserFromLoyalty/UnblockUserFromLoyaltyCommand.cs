using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Commands.UnblockUserFromLoyalty;

public record UnblockUserFromLoyaltyCommand(int UserId) : IRequest;

public class UnblockUserFromLoyaltyCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UnblockUserFromLoyaltyCommand>
{
    public async Task Handle(UnblockUserFromLoyaltyCommand request, CancellationToken cancellationToken)
    {
        var adminId = currentUserService.UserId
                      ?? throw new NotAuthorizedAccessException("User not authenticated");

        var wallet = await applicationDbContext.UserLoyaltyAccounts
                         .FirstOrDefaultAsync(w => w.UserId == request.UserId && !w.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException($"Loyalty account for user '{request.UserId}' not found");

        if (!wallet.IsBlocked)
            throw new DataValidationException("Loyalty", "User is not currently blocked from the loyalty system");

        // Unblock
        wallet.IsBlocked = false;
        wallet.BlockedAt = null;
        wallet.BlockedUntil = null;
        wallet.BlockReason = null;
        wallet.BlockedByUserId = null;
        wallet.ModifiedAt = DateTime.UtcNow;
        wallet.ModifiedBy = adminId;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

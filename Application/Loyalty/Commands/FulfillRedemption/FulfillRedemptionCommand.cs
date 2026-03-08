using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Commands.FulfillRedemption;

public record FulfillRedemptionCommand(int RedemptionId) : IRequest;

public class FulfillRedemptionCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<FulfillRedemptionCommand>
{
    public async Task Handle(FulfillRedemptionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var redemption = await applicationDbContext.UserRewardRedemptions
                             .FirstOrDefaultAsync(r => r.Id == request.RedemptionId && !r.IsDeleted, cancellationToken)
                         ?? throw new NotFoundException($"Redemption with Id '{request.RedemptionId}' not found");

        if (redemption.Status != (int)RedemptionStatus.Pending)
            throw new DataValidationException("Status", "Only pending redemptions can be fulfilled");

        var now = DateTime.UtcNow;
        redemption.Status = (int)RedemptionStatus.Fulfilled;
        redemption.FulfilledAt = now;
        redemption.ModifiedAt = now;
        redemption.ModifiedBy = userId;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

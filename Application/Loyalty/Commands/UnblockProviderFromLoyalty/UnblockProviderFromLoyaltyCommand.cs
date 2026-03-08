using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Commands.UnblockProviderFromLoyalty;

public record UnblockProviderFromLoyaltyCommand(
    string ProviderType,
    int ProviderId
) : IRequest;

public class UnblockProviderFromLoyaltyCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UnblockProviderFromLoyaltyCommand>
{
    public async Task Handle(UnblockProviderFromLoyaltyCommand request, CancellationToken cancellationToken)
    {
        var adminId = currentUserService.UserId
                      ?? throw new NotAuthorizedAccessException("User not authenticated");

        var now = DateTime.UtcNow;

        if (request.ProviderType == "ChargingPoint")
        {
            var cp = await applicationDbContext.ChargingPoints
                         .FirstOrDefaultAsync(x => x.Id == request.ProviderId && !x.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException($"ChargingPoint with Id '{request.ProviderId}' not found");

            if (!cp.IsLoyaltyBlocked)
                throw new DataValidationException("Loyalty", "This provider is not currently blocked from the loyalty system");

            cp.IsLoyaltyBlocked = false;
            cp.LoyaltyBlockedAt = null;
            cp.LoyaltyBlockedUntil = null;
            cp.LoyaltyBlockReason = null;
            cp.LoyaltyBlockedByUserId = null;
            cp.ModifiedAt = now;
            cp.ModifiedBy = adminId;
        }
        else if (request.ProviderType == "ServiceProvider")
        {
            var sp = await applicationDbContext.ServiceProviders
                         .FirstOrDefaultAsync(x => x.Id == request.ProviderId && !x.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException($"ServiceProvider with Id '{request.ProviderId}' not found");

            if (!sp.IsLoyaltyBlocked)
                throw new DataValidationException("Loyalty", "This provider is not currently blocked from the loyalty system");

            sp.IsLoyaltyBlocked = false;
            sp.LoyaltyBlockedAt = null;
            sp.LoyaltyBlockedUntil = null;
            sp.LoyaltyBlockReason = null;
            sp.LoyaltyBlockedByUserId = null;
            sp.ModifiedAt = now;
            sp.ModifiedBy = adminId;
        }
        else
        {
            throw new DataValidationException("ProviderType", "ProviderType must be 'ChargingPoint' or 'ServiceProvider'");
        }

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

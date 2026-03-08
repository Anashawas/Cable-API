using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Commands.BlockProviderFromLoyalty;

public record BlockProviderFromLoyaltyCommand(
    string ProviderType,
    int ProviderId,
    string Reason,
    DateTime? BlockUntil = null
) : IRequest;

public class BlockProviderFromLoyaltyCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<BlockProviderFromLoyaltyCommand>
{
    public async Task Handle(BlockProviderFromLoyaltyCommand request, CancellationToken cancellationToken)
    {
        var adminId = currentUserService.UserId
                      ?? throw new NotAuthorizedAccessException("User not authenticated");

        var now = DateTime.UtcNow;

        if (request.ProviderType == "ChargingPoint")
        {
            var cp = await applicationDbContext.ChargingPoints
                         .FirstOrDefaultAsync(x => x.Id == request.ProviderId && !x.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException($"ChargingPoint with Id '{request.ProviderId}' not found");

            if (cp.IsLoyaltyBlocked)
                throw new DataValidationException("Loyalty", "This provider is already blocked from the loyalty system");

            cp.IsLoyaltyBlocked = true;
            cp.LoyaltyBlockedAt = now;
            cp.LoyaltyBlockedUntil = request.BlockUntil;
            cp.LoyaltyBlockReason = request.Reason;
            cp.LoyaltyBlockedByUserId = adminId;
            cp.ModifiedAt = now;
            cp.ModifiedBy = adminId;
        }
        else if (request.ProviderType == "ServiceProvider")
        {
            var sp = await applicationDbContext.ServiceProviders
                         .FirstOrDefaultAsync(x => x.Id == request.ProviderId && !x.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException($"ServiceProvider with Id '{request.ProviderId}' not found");

            if (sp.IsLoyaltyBlocked)
                throw new DataValidationException("Loyalty", "This provider is already blocked from the loyalty system");

            sp.IsLoyaltyBlocked = true;
            sp.LoyaltyBlockedAt = now;
            sp.LoyaltyBlockedUntil = request.BlockUntil;
            sp.LoyaltyBlockReason = request.Reason;
            sp.LoyaltyBlockedByUserId = adminId;
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

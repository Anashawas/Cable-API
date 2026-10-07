using Application.Common.Security;
using Cable.Core;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.Rates.Queries.GetUserRating;

/// <summary>
/// A provider's view of a driver's standing, for deciding how to handle a
/// customer. Scoped deliberately: a provider may only look up a driver it has
/// actually served, so this cannot be used to enumerate the rating of every
/// user on the platform. Admins are unrestricted.
/// </summary>
public record GetUserRatingRequest(int UserId) : IRequest<UserRatingSummaryDto>;

public class GetUserRatingRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetUserRatingRequest, UserRatingSummaryDto>
{
    public async Task<UserRatingSummaryDto> Handle(GetUserRatingRequest request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUserService.UserId
                       ?? throw new NotAuthorizedAccessException("User not authenticated");

        // A driver reading their own rating goes through the same path as the
        // dedicated /GetMyUserRating endpoint.
        if (callerId != request.UserId
            && !await AdminRoleGuard.IsAdminAsync(applicationDbContext, currentUserService, cancellationToken))
        {
            await EnsureCallerHasServedDriver(callerId, request.UserId, cancellationToken);
        }

        return await UserRatingReader.GetSummaryAsync(applicationDbContext, request.UserId, cancellationToken);
    }

    private async Task EnsureCallerHasServedDriver(int callerId, int driverId, CancellationToken cancellationToken)
    {
        // Providers the caller owns or actively works for.
        var ownedChargingPointIds = await applicationDbContext.ChargingPoints.AsNoTracking()
            .Where(x => x.OwnerId == callerId && !x.IsDeleted)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var ownedServiceProviderIds = await applicationDbContext.ServiceProviders.AsNoTracking()
            .Where(x => x.OwnerId == callerId && !x.IsDeleted)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var managed = await applicationDbContext.ProviderManagers.AsNoTracking()
            .Where(pm => pm.UserId == callerId && pm.IsActive && !pm.IsDeleted)
            .Select(pm => new { pm.ProviderType, pm.ProviderId })
            .ToListAsync(cancellationToken);

        var chargingPointIds = ownedChargingPointIds
            .Concat(managed.Where(m => m.ProviderType == "ChargingPoint").Select(m => m.ProviderId))
            .Distinct().ToList();

        var serviceProviderIds = ownedServiceProviderIds
            .Concat(managed.Where(m => m.ProviderType == "ServiceProvider").Select(m => m.ProviderId))
            .Distinct().ToList();

        if (chargingPointIds.Count == 0 && serviceProviderIds.Count == 0)
            throw new ForbiddenAccessException("Only a provider that has served this driver, or an admin, can view their rating.");

        var hasServed = await applicationDbContext.PartnerTransactions.AsNoTracking()
            .AnyAsync(t => t.UserId == driverId
                           && !t.IsDeleted
                           && t.Status == (int)PartnerTransactionStatus.Completed
                           && ((t.ProviderType == "ChargingPoint" && chargingPointIds.Contains(t.ProviderId))
                               || (t.ProviderType == "ServiceProvider" && serviceProviderIds.Contains(t.ProviderId))),
                cancellationToken);

        if (!hasServed)
            throw new ForbiddenAccessException("Only a provider that has served this driver, or an admin, can view their rating.");
    }
}

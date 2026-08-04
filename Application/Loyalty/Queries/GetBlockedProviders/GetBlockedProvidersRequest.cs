using Application.Common.Models;
using Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetBlockedProviders;

public record BlockedProviderDto(
    string ProviderType,
    int ProviderId,
    string ProviderName,
    string? Reason,
    DateTime? BlockedAt,
    DateTime? BlockedUntil,
    int? BlockedByUserId
);

/// <summary>F2 — every provider currently blocked from loyalty. Admin role required.</summary>
public record GetBlockedProvidersRequest(int? Page = null, int? PageSize = null) : IRequest<PagedResult<BlockedProviderDto>>;

public class GetBlockedProvidersRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetBlockedProvidersRequest, PagedResult<BlockedProviderDto>>
{
    public async Task<PagedResult<BlockedProviderDto>> Handle(GetBlockedProvidersRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var now = DateTime.UtcNow;

        var chargingPoints = await applicationDbContext.ChargingPoints
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsLoyaltyBlocked
                        && (x.LoyaltyBlockedUntil == null || x.LoyaltyBlockedUntil >= now))
            .Select(x => new BlockedProviderDto(
                "ChargingPoint", x.Id, x.Name, x.LoyaltyBlockReason,
                x.LoyaltyBlockedAt, x.LoyaltyBlockedUntil, x.LoyaltyBlockedByUserId))
            .ToListAsync(cancellationToken);

        var serviceProviders = await applicationDbContext.ServiceProviders
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsLoyaltyBlocked
                        && (x.LoyaltyBlockedUntil == null || x.LoyaltyBlockedUntil >= now))
            .Select(x => new BlockedProviderDto(
                "ServiceProvider", x.Id, x.Name, x.LoyaltyBlockReason,
                x.LoyaltyBlockedAt, x.LoyaltyBlockedUntil, x.LoyaltyBlockedByUserId))
            .ToListAsync(cancellationToken);

        return chargingPoints
            .Concat(serviceProviders)
            .OrderByDescending(x => x.BlockedAt)
            .ToList()
            .ToOptionallyPaginated(request.Page, request.PageSize);
    }
}

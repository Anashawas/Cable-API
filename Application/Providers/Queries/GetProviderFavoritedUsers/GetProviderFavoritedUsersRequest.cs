using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Providers.Queries.GetProviderFavoritedUsers;

/// <summary>A user who favorited the provider. Deliberately excludes contact
/// details (phone/email) — providers see who likes them, not how to reach them.</summary>
public record FavoritedUserDto(int UserId, string? Name, string? City, DateTime FavoritedAt);

/// <summary>
/// Users who favorited a charging point or service provider. Visible to the
/// provider's owner, its active worker, or an admin.
/// </summary>
public record GetProviderFavoritedUsersRequest(
    string ProviderType,
    int ProviderId,
    int? Page = null,
    int? PageSize = null
) : IRequest<PagedResult<FavoritedUserDto>>;

public class GetProviderFavoritedUsersRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetProviderFavoritedUsersRequest, PagedResult<FavoritedUserDto>>
{
    public async Task<PagedResult<FavoritedUserDto>> Handle(GetProviderFavoritedUsersRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        int? ownerId = request.ProviderType switch
        {
            "ChargingPoint" => await applicationDbContext.ChargingPoints
                .AsNoTracking()
                .Where(x => x.Id == request.ProviderId && !x.IsDeleted)
                .Select(x => (int?)x.OwnerId)
                .FirstOrDefaultAsync(cancellationToken),
            "ServiceProvider" => await applicationDbContext.ServiceProviders
                .AsNoTracking()
                .Where(x => x.Id == request.ProviderId && !x.IsDeleted)
                .Select(x => (int?)x.OwnerId)
                .FirstOrDefaultAsync(cancellationToken),
            _ => throw new DataValidationException("ProviderType",
                "ProviderType must be 'ChargingPoint' or 'ServiceProvider'")
        };

        // FirstOrDefaultAsync on int? can't distinguish "not found" from
        // "unassigned owner" — re-check existence explicitly for a clean 404.
        if (ownerId is null)
        {
            var exists = request.ProviderType == "ChargingPoint"
                ? await applicationDbContext.ChargingPoints.AnyAsync(x => x.Id == request.ProviderId && !x.IsDeleted, cancellationToken)
                : await applicationDbContext.ServiceProviders.AnyAsync(x => x.Id == request.ProviderId && !x.IsDeleted, cancellationToken);
            if (!exists)
                throw new NotFoundException($"can not find {request.ProviderType} with id {request.ProviderId}");
        }

        var isWorker = await applicationDbContext.ProviderManagers
            .AsNoTracking()
            .AnyAsync(pm => pm.ProviderType == request.ProviderType
                            && pm.ProviderId == request.ProviderId
                            && pm.UserId == userId
                            && pm.IsActive && !pm.IsDeleted, cancellationToken);

        if (ownerId != userId && !isWorker
            && !await AdminRoleGuard.IsAdminAsync(applicationDbContext, currentUserService, cancellationToken))
            throw new ForbiddenAccessException("Only the provider owner, its worker, or an admin can view its favorites.");

        if (request.ProviderType == "ChargingPoint")
        {
            return await applicationDbContext.UserFavoriteChargingPoints
                .AsNoTracking()
                .Where(f => !f.IsDeleted && f.ChargingPointId == request.ProviderId && !f.User.IsDeleted)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new FavoritedUserDto(f.UserId, f.User.Name, f.User.City, f.CreatedAt))
                .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
        }

        return await applicationDbContext.UserFavoriteServiceProviders
            .AsNoTracking()
            .Where(f => !f.IsDeleted && f.ServiceProviderId == request.ProviderId && !f.User.IsDeleted)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FavoritedUserDto(f.UserId, f.User.Name, f.User.City, f.CreatedAt))
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
    }
}

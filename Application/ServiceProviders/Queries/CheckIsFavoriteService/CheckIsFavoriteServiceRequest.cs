using Cable.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Queries.CheckIsFavoriteService;

/// <summary>
/// Whether the caller has this service provider in their favorites — the
/// service-provider counterpart of <c>CheckIsFavoriteRequest</c> for charging
/// points, so a detail screen can render its heart icon without pulling the
/// whole favorites list and searching it.
///
/// Returns the same <c>CheckIsFavoriteDto</c> shape the charging-point check
/// returns, so clients can share one model for both.
/// </summary>
public record CheckIsFavoriteServiceRequest(int ServiceProviderId) : IRequest<Favorites.Queries.CheckIsFavorite.CheckIsFavoriteDto>;

public class CheckIsFavoriteServiceRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<CheckIsFavoriteServiceRequest, Favorites.Queries.CheckIsFavorite.CheckIsFavoriteDto>
{
    public async Task<Favorites.Queries.CheckIsFavorite.CheckIsFavoriteDto> Handle(
        CheckIsFavoriteServiceRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var favorite = await applicationDbContext.UserFavoriteServiceProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId
                                      && x.ServiceProviderId == request.ServiceProviderId
                                      && !x.IsDeleted,
                cancellationToken);

        return new Favorites.Queries.CheckIsFavorite.CheckIsFavoriteDto(favorite != null, favorite?.Id);
    }
}

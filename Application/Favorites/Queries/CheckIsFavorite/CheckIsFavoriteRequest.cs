using Cable.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Favorites.Queries.CheckIsFavorite;

public record CheckIsFavoriteRequest(int ChargingPointId) : IRequest<CheckIsFavoriteDto>;

public class CheckIsFavoriteQueryHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<CheckIsFavoriteRequest, CheckIsFavoriteDto>
{
    public async Task<CheckIsFavoriteDto> Handle(CheckIsFavoriteRequest request,
        CancellationToken cancellationToken)
    {
        // The route requires authentication, so a missing user is a broken
        // caller rather than a signed-out one. Returning "not favorited" here
        // (the previous behaviour) turned an expired token into a wrong answer.
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var favorite = await applicationDbContext.UserFavoriteChargingPoints
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId
                && x.ChargingPointId == request.ChargingPointId
                && !x.IsDeleted,
                cancellationToken);

        return new CheckIsFavoriteDto(favorite != null, favorite?.Id);
    }
}

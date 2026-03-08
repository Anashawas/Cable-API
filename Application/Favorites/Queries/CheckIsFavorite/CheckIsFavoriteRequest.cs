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
        var userId = currentUserService.UserId;

        if (userId == null)
            return new CheckIsFavoriteDto(false, null);

        var favorite = await applicationDbContext.UserFavoriteChargingPoints
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId
                && x.ChargingPointId == request.ChargingPointId
                && !x.IsDeleted,
                cancellationToken);

        return new CheckIsFavoriteDto(favorite != null, favorite?.Id);
    }
}

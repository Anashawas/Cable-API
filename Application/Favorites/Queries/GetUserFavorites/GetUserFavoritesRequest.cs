using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;

using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Favorites.Queries.GetUserFavorites;

public record GetUserFavoritesRequest() : IRequest<List<GetUserFavoritesDto>>;

public class GetUserFavoritesQueryHandler(
    IChargingPointRepository chargingPointRepository,
    ICurrentUserService currentUserService,
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetUserFavoritesRequest, List<GetUserFavoritesDto>>
{
    public async Task<List<GetUserFavoritesDto>> Handle(GetUserFavoritesRequest request,
        CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts
            .FirstOrDefaultAsync(x => x.Id == currentUserService.UserId && !x.IsDeleted, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException();
        }

        return await chargingPointRepository.GetUserFavoriteChargingPoints(user.Id, cancellationToken);
    }
}

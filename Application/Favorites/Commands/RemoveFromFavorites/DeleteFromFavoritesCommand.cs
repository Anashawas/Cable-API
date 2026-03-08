
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Favorites.Commands.RemoveFromFavorites;

public record DeleteFromFavoritesCommand(int ChargingPointId) : IRequest;

public class RemoveFromFavoritesCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteFromFavoritesCommand>
{
    public async Task Handle(DeleteFromFavoritesCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
            ?? throw new NotAuthorizedAccessException("User not authenticated");

        var favorite = await applicationDbContext.UserFavoriteChargingPoints
            .FirstOrDefaultAsync(x => x.UserId == userId
                && x.ChargingPointId == request.ChargingPointId
                && !x.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException($"Favorite not found for charging point {request.ChargingPointId}");

        // Soft delete
        favorite.IsDeleted = true;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

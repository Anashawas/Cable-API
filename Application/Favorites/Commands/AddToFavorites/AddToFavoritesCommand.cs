
using Cable.Core;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Favorites.Commands.AddToFavorites;

public record AddToFavoritesCommand(int ChargingPointId) : IRequest<int>;

public class AddToFavoritesCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    ILoyaltyPointService loyaltyPointService)
    : IRequestHandler<AddToFavoritesCommand, int>
{
    public async Task<int> Handle(AddToFavoritesCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

     
        var userExists = await applicationDbContext.UserAccounts
            .AnyAsync(x => x.Id == userId && !x.IsDeleted, cancellationToken);

        if (!userExists)
            throw new NotFoundException($"User with id {userId} not found");


        var chargingPointExists = await applicationDbContext.ChargingPoints
            .AnyAsync(x => x.Id == request.ChargingPointId && !x.IsDeleted, cancellationToken);

        if (!chargingPointExists)
            throw new NotFoundException($"Charging point with id {request.ChargingPointId} not found");
        
        var existingFavorite = await applicationDbContext.UserFavoriteChargingPoints
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ChargingPointId == request.ChargingPointId,
                cancellationToken);

        if (existingFavorite != null)
        {
            if (existingFavorite.IsDeleted)
            {

                existingFavorite.IsDeleted = false;
                await applicationDbContext.SaveChanges(cancellationToken);
                return existingFavorite.Id;
            }

            throw new DataValidationException("Favorite charging point", "Charging point already added to favorites");
        }
        
        var favorite = new UserFavoriteChargingPoint
        {
            UserId = userId,
            ChargingPointId = request.ChargingPointId
        };

        applicationDbContext.UserFavoriteChargingPoints.Add(favorite);
        await applicationDbContext.SaveChanges(cancellationToken);

        // Award loyalty points
        await loyaltyPointService.AwardPointsAsync(userId, "ADD_FAVORITE", "ChargingPoint", request.ChargingPointId, cancellationToken: cancellationToken);

        return favorite.Id;
    }
}
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Commands.AddToFavoriteService;

public record AddToFavoriteServiceCommand(int ServiceProviderId) : IRequest<int>;

public class AddToFavoriteServiceCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    ILoyaltyPointService loyaltyPointService)
    : IRequestHandler<AddToFavoriteServiceCommand, int>
{
    public async Task<int> Handle(AddToFavoriteServiceCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var serviceProviderExists = await applicationDbContext.ServiceProviders
            .AnyAsync(x => x.Id == request.ServiceProviderId && !x.IsDeleted, cancellationToken);

        if (!serviceProviderExists)
            throw new NotFoundException($"Service provider with id {request.ServiceProviderId} not found");

        var existingFavorite = await applicationDbContext.UserFavoriteServiceProviders
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ServiceProviderId == request.ServiceProviderId,
                cancellationToken);

        if (existingFavorite != null)
        {
            if (existingFavorite.IsDeleted)
            {
                existingFavorite.IsDeleted = false;
                await applicationDbContext.SaveChanges(cancellationToken);
                return existingFavorite.Id;
            }

            throw new DataValidationException("Favorite service provider", "Service provider already added to favorites");
        }

        var favorite = new UserFavoriteServiceProvider
        {
            UserId = userId,
            ServiceProviderId = request.ServiceProviderId
        };

        applicationDbContext.UserFavoriteServiceProviders.Add(favorite);
        await applicationDbContext.SaveChanges(cancellationToken);

        // Award loyalty points
        await loyaltyPointService.AwardPointsAsync(userId, "ADD_FAVORITE_SERVICE", "ServiceProvider", request.ServiceProviderId, cancellationToken: cancellationToken);

        return favorite.Id;
    }
}

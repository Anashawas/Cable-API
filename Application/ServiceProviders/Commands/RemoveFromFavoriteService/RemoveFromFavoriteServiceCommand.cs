using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Commands.RemoveFromFavoriteService;

public record RemoveFromFavoriteServiceCommand(int ServiceProviderId) : IRequest;

public class RemoveFromFavoriteServiceCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<RemoveFromFavoriteServiceCommand>
{
    public async Task Handle(RemoveFromFavoriteServiceCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var favorite = await applicationDbContext.UserFavoriteServiceProviders
                           .FirstOrDefaultAsync(x => x.UserId == userId
                                                     && x.ServiceProviderId == request.ServiceProviderId
                                                     && !x.IsDeleted,
                               cancellationToken)
                       ?? throw new NotFoundException($"Favorite not found for service provider {request.ServiceProviderId}");

        favorite.IsDeleted = true;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

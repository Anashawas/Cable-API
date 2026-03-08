using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Commands.ChangeServiceProviderOwner;

public record ChangeServiceProviderOwnerCommand(int ServiceProviderId, int NewOwnerId) : IRequest;

public class ChangeServiceProviderOwnerCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<ChangeServiceProviderOwnerCommand>
{
    public async Task Handle(ChangeServiceProviderOwnerCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var serviceProvider = await applicationDbContext.ServiceProviders
                                  .FirstOrDefaultAsync(x => x.Id == request.ServiceProviderId && !x.IsDeleted, cancellationToken)
                              ?? throw new NotFoundException($"Cannot find service provider with id {request.ServiceProviderId}");

        if (serviceProvider.OwnerId != userId)
            throw new ForbiddenAccessException("You are not the owner of this service provider");

        var newOwner = await applicationDbContext.UserAccounts
                           .FirstOrDefaultAsync(x => x.Id == request.NewOwnerId && !x.IsDeleted, cancellationToken)
                       ?? throw new NotFoundException($"Cannot find user with id {request.NewOwnerId}");

        serviceProvider.OwnerId = request.NewOwnerId;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

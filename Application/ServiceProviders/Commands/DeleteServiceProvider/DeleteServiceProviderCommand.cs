using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Commands.DeleteServiceProvider;

public record DeleteServiceProviderCommand(int Id) : IRequest;

public class DeleteServiceProviderCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteServiceProviderCommand>
{
    public async Task Handle(DeleteServiceProviderCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var serviceProvider = await applicationDbContext.ServiceProviders
                                  .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                              ?? throw new NotFoundException($"Service provider with id {request.Id} not found");

        if (serviceProvider.OwnerId != userId)
            throw new ForbiddenAccessException("You are not the owner of this service provider");

        serviceProvider.IsDeleted = true;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Commands.DeleteServiceProvider;

public record DeleteServiceProviderCommand(int Id) : IRequest;

public class DeleteServiceProviderCommandHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<DeleteServiceProviderCommand>
{
    public async Task Handle(DeleteServiceProviderCommand request, CancellationToken cancellationToken)
    {
        var serviceProvider = await applicationDbContext.ServiceProviders
                                  .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                              ?? throw new NotFoundException($"Service provider with id {request.Id} not found");

        serviceProvider.IsDeleted = true;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

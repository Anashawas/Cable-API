using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Commands.VerifyServiceProvider;

public record VerifyServiceProviderCommand(int Id) : IRequest;

public class VerifyServiceProviderCommandHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<VerifyServiceProviderCommand>
{
    public async Task Handle(VerifyServiceProviderCommand request, CancellationToken cancellationToken)
    {
        var serviceProvider = await applicationDbContext.ServiceProviders
                                  .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                              ?? throw new NotFoundException($"Service provider with id {request.Id} not found");

        serviceProvider.IsVerified = true;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

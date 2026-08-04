using Application.Providers;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Commands.ChangeServiceProviderOwner;

/// <summary>NewOwnerId null = unassign the provider (blocked while it has active offers / partner agreements).</summary>
public record ChangeServiceProviderOwnerCommand(int ServiceProviderId, int? NewOwnerId) : IRequest;

public class ChangeServiceProviderOwnerCommandHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<ChangeServiceProviderOwnerCommand>
{
    public async Task Handle(ChangeServiceProviderOwnerCommand request, CancellationToken cancellationToken)
    {
        var serviceProvider = await applicationDbContext.ServiceProviders
                                  .FirstOrDefaultAsync(x => x.Id == request.ServiceProviderId && !x.IsDeleted, cancellationToken)
                              ?? throw new NotFoundException($"Cannot find service provider with id {request.ServiceProviderId}");

        if (serviceProvider.OwnerId == request.NewOwnerId)
            throw new DataValidationException("NewOwnerId", request.NewOwnerId.HasValue
                ? "New owner is already the current owner of this service provider"
                : "This service provider is already unassigned");

        if (request.NewOwnerId.HasValue)
        {
            var newOwner = await applicationDbContext.UserAccounts
                               .FirstOrDefaultAsync(x => x.Id == request.NewOwnerId.Value && !x.IsDeleted, cancellationToken)
                           ?? throw new NotFoundException($"Cannot find user with id {request.NewOwnerId}");

            if (newOwner.RoleId != 4)
                throw new DataValidationException("NewOwnerId", "New owner must have Provider role");
        }
        else
        {
            await ProviderUnassignGuard.EnsureCanUnassignAsync(
                applicationDbContext, "ServiceProvider", serviceProvider.Id, cancellationToken);
        }

        serviceProvider.OwnerId = request.NewOwnerId;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

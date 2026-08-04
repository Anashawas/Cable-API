using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Workers.Commands.SetWorkerActive;

/// <summary>
/// Owner activates or deactivates the worker. Deactivating also blocks the
/// worker's login (the account's IsActive is toggled in lock-step).
/// </summary>
public record SetWorkerActiveCommand(int ProviderManagerId, bool IsActive) : IRequest;

public class SetWorkerActiveCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<SetWorkerActiveCommand>
{
    public async Task Handle(SetWorkerActiveCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUserService.UserId
                       ?? throw new NotAuthorizedAccessException("User not authenticated");

        var providerManager = await applicationDbContext.ProviderManagers
                                  .FirstOrDefaultAsync(pm => pm.Id == request.ProviderManagerId && !pm.IsDeleted,
                                      cancellationToken)
                              ?? throw new NotFoundException($"Worker assignment {request.ProviderManagerId} not found");

        await WorkerOwnershipHelper.EnsureCallerOwnsProviderAsync(
            applicationDbContext, providerManager.ProviderType, providerManager.ProviderId, callerId, cancellationToken);

        providerManager.IsActive = request.IsActive;

        var worker = await applicationDbContext.UserAccounts
            .FirstOrDefaultAsync(u => u.Id == providerManager.UserId, cancellationToken);
        if (worker is not null)
            worker.IsActive = request.IsActive;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

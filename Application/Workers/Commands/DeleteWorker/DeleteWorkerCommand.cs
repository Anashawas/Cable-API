using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Workers.Commands.DeleteWorker;

/// <summary>
/// Owner removes the worker: soft-deletes the assignment AND the worker's account
/// (so the orphaned account can no longer log into the provider app). This frees the
/// provider to receive a new worker.
/// </summary>
public record DeleteWorkerCommand(int ProviderManagerId) : IRequest;

public class DeleteWorkerCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteWorkerCommand>
{
    public async Task Handle(DeleteWorkerCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUserService.UserId
                       ?? throw new NotAuthorizedAccessException("User not authenticated");

        var providerManager = await applicationDbContext.ProviderManagers
                                  .FirstOrDefaultAsync(pm => pm.Id == request.ProviderManagerId && !pm.IsDeleted,
                                      cancellationToken)
                              ?? throw new NotFoundException($"Worker assignment {request.ProviderManagerId} not found");

        await WorkerOwnershipHelper.EnsureCallerOwnsProviderAsync(
            applicationDbContext, providerManager.ProviderType, providerManager.ProviderId, callerId, cancellationToken);

        providerManager.IsDeleted = true;
        providerManager.IsActive  = false;

        var worker = await applicationDbContext.UserAccounts
            .FirstOrDefaultAsync(u => u.Id == providerManager.UserId, cancellationToken);
        if (worker is not null)
        {
            worker.IsActive  = false;
            worker.IsDeleted = true;
        }

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

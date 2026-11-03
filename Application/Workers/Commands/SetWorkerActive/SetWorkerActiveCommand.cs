using Application.Common.Interfaces;
using Application.Workers.WorkerAuth;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Workers.Commands.SetWorkerActive;

/// <summary>
/// Owner suspends or resumes the worker — the assignment only (§3.3): suspending ends the worker's partner-app
/// sessions at once (the next request answers 401) but never touches the account, so a driver who is also a
/// worker keeps the Cable app. Resuming re-runs the plan rule (decision 38).
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

        if (providerManager.IsActive == request.IsActive) return;

        providerManager.IsActive = request.IsActive;
        if (!request.IsActive) providerManager.KeepOnFree = false;
        await applicationDbContext.SaveChanges(cancellationToken);

        if (!request.IsActive)
            await PartnerSessions.EndAllAsync(applicationDbContext, providerManager.UserId, cancellationToken);

        await WorkerPlanSync.ApplyAsync(applicationDbContext, providerManager.ProviderType, providerManager.ProviderId, cancellationToken);
    }
}

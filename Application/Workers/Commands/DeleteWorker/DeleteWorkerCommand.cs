using Application.Common.Interfaces;
using Application.Workers.WorkerAuth;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Workers.Commands.DeleteWorker;

/// <summary>
/// Owner removes the worker: the assignment and the partner credential are soft-deleted and the partner sessions
/// end. The account itself is only deactivated when it was created by the worker wizard (role Worker) — a driver's
/// account is never touched. The phone can be added again later (the account comes back).
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

        providerManager.IsDeleted  = true;
        providerManager.IsActive   = false;
        providerManager.KeepOnFree = false;

        var credential = await applicationDbContext.PartnerCredentials
            .FirstOrDefaultAsync(c => c.UserId == providerManager.UserId && !c.IsDeleted, cancellationToken);
        if (credential is not null) credential.IsDeleted = true;

        var workerRoleId = await applicationDbContext.Roles
            .Where(r => r.Name == "Worker" && !r.IsDeleted).Select(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        var worker = await applicationDbContext.UserAccounts
            .FirstOrDefaultAsync(u => u.Id == providerManager.UserId, cancellationToken);
        if (worker is not null && worker.RoleId == workerRoleId)
            worker.IsActive = false;

        await applicationDbContext.SaveChanges(cancellationToken);

        await PartnerSessions.ForgetDevicesAsync(applicationDbContext, providerManager.UserId, cancellationToken);
        await PartnerSessions.EndAllAsync(applicationDbContext, providerManager.UserId, cancellationToken);

        // A freed slot may let a plan-suspended colleague back in.
        await WorkerPlanSync.ApplyAsync(applicationDbContext, providerManager.ProviderType, providerManager.ProviderId, cancellationToken);
    }
}

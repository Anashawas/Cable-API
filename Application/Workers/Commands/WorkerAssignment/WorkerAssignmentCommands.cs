using Application.Common.Interfaces;
using Application.Common.Security;
using Application.Workers.Queries.GetWorkerByProvider;
using Application.Workers.WorkerAuth;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Workers.Commands.WorkerAssignment;

/// <summary>
/// Decision 38: the owner picks which worker stays when the plan drops to one. Clears the flag on the others and
/// re-runs the plan rule at once, so the chosen worker is usable and the rest are suspended by the plan.
/// </summary>
public record SetKeepOnFreeCommand(int ProviderManagerId) : IRequest<List<WorkerDto>>;

public class SetKeepOnFreeCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<SetKeepOnFreeCommand, List<WorkerDto>>
{
    public async Task<List<WorkerDto>> Handle(SetKeepOnFreeCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");

        var pm = await db.ProviderManagers.FirstOrDefaultAsync(x => x.Id == request.ProviderManagerId && !x.IsDeleted, cancellationToken)
                 ?? throw new NotFoundException($"Worker assignment {request.ProviderManagerId} not found");
        await WorkerOwnershipHelper.EnsureCallerOwnsProviderAsync(db, pm.ProviderType, pm.ProviderId, callerId, cancellationToken);

        if (!pm.IsActive)
            throw new ConflictException("WORKER_INACTIVE", Lang.Pick("الموظف موقوف — فعّله أول", "This worker is suspended — resume them first"));

        var siblings = await db.ProviderManagers
            .Where(x => x.ProviderType == pm.ProviderType && x.ProviderId == pm.ProviderId && !x.IsDeleted)
            .ToListAsync(cancellationToken);
        foreach (var s in siblings) s.KeepOnFree = s.Id == pm.Id;
        await db.SaveChanges(cancellationToken);

        await WorkerPlanSync.ApplyAsync(db, pm.ProviderType, pm.ProviderId, cancellationToken);
        return await WorkerDtoQuery.ListAsync(db, pm.ProviderType, pm.ProviderId, cancellationToken);
    }
}

/// <summary>
/// Owner or admin changes a worker's phone (§5.1, §6.4). The new number must be free; the worker's partner sessions
/// end and the next login (on the new phone) needs an OTP, which is what verifies it.
/// </summary>
public record ChangeWorkerPhoneCommand(int ProviderManagerId, string Phone) : IRequest<WorkerDto>;

public class ChangeWorkerPhoneCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ChangeWorkerPhoneCommand, WorkerDto>
{
    public const string PhoneInUse = "PHONE_IN_USE";

    public async Task<WorkerDto> Handle(ChangeWorkerPhoneCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");

        var pm = await db.ProviderManagers.AsNoTracking()
                     .Where(x => x.Id == request.ProviderManagerId && !x.IsDeleted)
                     .Select(x => new { x.Id, x.UserId, x.ProviderType, x.ProviderId })
                     .FirstOrDefaultAsync(cancellationToken)
                 ?? throw new NotFoundException($"Worker assignment {request.ProviderManagerId} not found");

        if (!await AdminRoleGuard.IsAdminAsync(db, currentUser, cancellationToken))
            await WorkerOwnershipHelper.EnsureCallerOwnsProviderAsync(db, pm.ProviderType, pm.ProviderId, callerId, cancellationToken);

        var normalized = WorkerPhones.Normalize(request.Phone)
                         ?? throw new DataValidationException("Phone", Lang.Pick("رقم الموبايل غير صحيح", "Invalid phone number format."));

        var worker = await db.UserAccounts.FirstAsync(u => u.Id == pm.UserId, cancellationToken);
        if (worker.Phone == normalized) return await WorkerDtoQuery.OneAsync(db, pm.Id, cancellationToken);

        if (await db.UserAccounts.AnyAsync(u => u.Phone == normalized && !u.IsDeleted && u.Id != worker.Id, cancellationToken))
            throw new ConflictException(PhoneInUse, Lang.Pick("هذا الرقم مستعمل بحساب ثاني", "This phone number is used by another account."));

        worker.Phone = normalized;
        await db.SaveChanges(cancellationToken);

        await PartnerSessions.ForgetDevicesAsync(db, worker.Id, cancellationToken);
        await PartnerSessions.EndAllAsync(db, worker.Id, cancellationToken);

        return await WorkerDtoQuery.OneAsync(db, pm.Id, cancellationToken);
    }
}

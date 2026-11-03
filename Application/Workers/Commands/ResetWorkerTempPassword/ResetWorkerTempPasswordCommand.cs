using Cable.Core;
using Cable.Security.Encryption.Interfaces;
using Microsoft.EntityFrameworkCore;
using Application.Common.Security;
using Application.Workers.WorkerAuth;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;

namespace Application.Workers.Commands.ResetWorkerTempPassword;

/// <summary>
/// "Share the details again" (§3.2.2) and the admin's support action (§6.4): a new temporary password replaces the
/// old one, the forced change comes back, trusted devices are forgotten and the worker's partner sessions end.
/// Owner of the provider, or an admin.
/// </summary>
public record ResetWorkerTempPasswordCommand(int ProviderManagerId, string? TempPassword = null) : IRequest<WorkerTempPasswordResult>;

public class ResetWorkerTempPasswordCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IPasswordHasher passwordHasher)
    : IRequestHandler<ResetWorkerTempPasswordCommand, WorkerTempPasswordResult>
{
    public async Task<WorkerTempPasswordResult> Handle(ResetWorkerTempPasswordCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");

        var pm = await db.ProviderManagers
                     .Where(x => x.Id == request.ProviderManagerId && !x.IsDeleted)
                     .Select(x => new { x.Id, x.UserId, x.ProviderType, x.ProviderId, x.User.Name, x.User.Phone })
                     .FirstOrDefaultAsync(cancellationToken)
                 ?? throw new NotFoundException($"Worker assignment {request.ProviderManagerId} not found");

        if (!await AdminRoleGuard.IsAdminAsync(db, currentUser, cancellationToken))
            await WorkerOwnershipHelper.EnsureCallerOwnsProviderAsync(db, pm.ProviderType, pm.ProviderId, callerId, cancellationToken);

        var settings = await WorkerAuthSettings.ReadAsync(db, cancellationToken);
        var (temp, expiresAt) = await WorkerCredentials.IssueTempPasswordAsync(db, passwordHasher, settings, pm.UserId, request.TempPassword, cancellationToken);
        await db.SaveChanges(cancellationToken);

        await PartnerSessions.ForgetDevicesAsync(db, pm.UserId, cancellationToken);
        await PartnerSessions.EndAllAsync(db, pm.UserId, cancellationToken);

        var (ownerName, providerName) = await WorkerCredentials.NamesAsync(db, pm.ProviderType, pm.ProviderId, cancellationToken);
        var message = WorkerShareMessage.Build(Lang.IsArabic ? "ar" : "en", pm.Name ?? Lang.Pick("الموظف", "Worker"), ownerName, providerName,
            WorkerPhones.ToLocal(pm.Phone), temp, expiresAt, settings.TempPasswordHours, settings.PartnerLoginUrl, hasBiometricLogin: false);

        return new WorkerTempPasswordResult(pm.Id, pm.UserId, temp, expiresAt, message);
    }
}

/// <summary>Admin support (§6.4) or the owner: clears a login lock and the failed-attempt counter.</summary>
public record UnlockWorkerLoginCommand(int ProviderManagerId) : IRequest;

public class UnlockWorkerLoginCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<UnlockWorkerLoginCommand>
{
    public async Task Handle(UnlockWorkerLoginCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");

        var pm = await db.ProviderManagers
                     .Where(x => x.Id == request.ProviderManagerId && !x.IsDeleted)
                     .Select(x => new { x.UserId, x.ProviderType, x.ProviderId })
                     .FirstOrDefaultAsync(cancellationToken)
                 ?? throw new NotFoundException($"Worker assignment {request.ProviderManagerId} not found");

        if (!await AdminRoleGuard.IsAdminAsync(db, currentUser, cancellationToken))
            await WorkerOwnershipHelper.EnsureCallerOwnsProviderAsync(db, pm.ProviderType, pm.ProviderId, callerId, cancellationToken);

        await db.PartnerCredentials.Where(c => c.UserId == pm.UserId && !c.IsDeleted)
            .ExecuteUpdateAsync(x => x
                .SetProperty(c => c.LockedUntil, (DateTime?)null)
                .SetProperty(c => c.FailedLoginCount, 0)
                .SetProperty(c => c.ModifiedAt, DateTime.UtcNow), cancellationToken);
    }
}

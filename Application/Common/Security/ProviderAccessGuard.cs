using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Common.Security;

/// <summary>
/// May this caller act on behalf of this provider?
///
/// Both transaction-creation endpoints previously required only that the caller
/// be signed in. The provider was taken from the request body — an offer id or
/// an agreement id — and never checked against the caller, so any authenticated
/// account could mint a redemption code against any provider's offer, or open a
/// charging transaction against any station's agreement. The staff user id was
/// recorded on the row but never used to authorise anything.
///
/// Access is granted to the provider's owner, to an active worker
/// (<see cref="Domain.Enitites.ProviderManager"/>) since operating the till is
/// exactly what workers exist for, and to admins for support.
///
/// <see cref="Application.Workers.WorkerOwnershipHelper"/> stays owner-only and
/// is not replaced by this: a worker must not be able to manage other workers.
/// </summary>
public static class ProviderAccessGuard
{
    public const string ServiceProvider = "ServiceProvider";
    public const string ChargingPoint = "ChargingPoint";

    /// <param name="requiredPrivilege">
    /// A WorkerPrivileges key. Owners and admins always pass; a worker passes only when the owner
    /// granted that privilege (null stored = everything). Null here = any active worker.
    /// </param>
    public static async Task EnsureCanActForProviderAsync(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        string providerType,
        int providerId,
        CancellationToken cancellationToken,
        string? requiredPrivilege = null)
    {
        var callerUserId = currentUser.UserId
                           ?? throw new NotAuthorizedAccessException("User not authenticated");

        var ownerId = providerType switch
        {
            ServiceProvider => await db.ServiceProviders
                .AsNoTracking()
                .Where(x => x.Id == providerId && !x.IsDeleted)
                .Select(x => x.OwnerId)
                .FirstOrDefaultAsync(cancellationToken),
            ChargingPoint => await db.ChargingPoints
                .AsNoTracking()
                .Where(x => x.Id == providerId && !x.IsDeleted)
                .Select(x => x.OwnerId)
                .FirstOrDefaultAsync(cancellationToken),
            _ => throw new DataValidationException("ProviderType",
                $"Unknown provider type '{providerType}'.")
        };

        if (ownerId == callerUserId)
            return;

        var worker = await db.ProviderManagers
            .AsNoTracking()
            .Where(x => x.ProviderType == providerType
                        && x.ProviderId == providerId
                        && x.UserId == callerUserId
                        && x.IsActive
                        && !x.IsDeleted)
            .Select(x => new { x.Privileges })
            .FirstOrDefaultAsync(cancellationToken);

        if (worker is not null)
        {
            if (requiredPrivilege is null || Cable.Core.Constants.WorkerPrivileges.Has(worker.Privileges, requiredPrivilege))
                return;
            throw new ForbiddenAccessException("Your account does not have this permission. Ask the station owner.");
        }

        if (await AdminRoleGuard.IsAdminAsync(db, currentUser, cancellationToken))
            return;

        throw new ForbiddenAccessException(
            "You do not have access to this provider.");
    }
}

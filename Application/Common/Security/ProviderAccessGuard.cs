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

    public static async Task EnsureCanActForProviderAsync(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        string providerType,
        int providerId,
        CancellationToken cancellationToken)
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

        var isWorker = await db.ProviderManagers
            .AsNoTracking()
            .AnyAsync(x => x.ProviderType == providerType
                           && x.ProviderId == providerId
                           && x.UserId == callerUserId
                           && x.IsActive
                           && !x.IsDeleted,
                cancellationToken);

        if (isWorker)
            return;

        if (await AdminRoleGuard.IsAdminAsync(db, currentUser, cancellationToken))
            return;

        throw new ForbiddenAccessException(
            "You do not have access to this provider.");
    }
}

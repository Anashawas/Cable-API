using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core.Exceptions;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Commands.ManageOcppAuthorizedTags;

/// <summary>Owner/manager/admin: pause or resume a card without removing it.</summary>
public record SetOcppAuthorizedTagEnabledCommand(int Id, bool IsEnabled) : IRequest;

public class SetOcppAuthorizedTagEnabledCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IBackgroundJobClient jobs)
    : IRequestHandler<SetOcppAuthorizedTagEnabledCommand>
{
    public async Task Handle(SetOcppAuthorizedTagEnabledCommand request, CancellationToken cancellationToken)
    {
        var tag = await OcppAuthorizedTagLookup.TrackedAsync(db, currentUser, request.Id, cancellationToken);
        tag.IsEnabled = request.IsEnabled;
        await db.SaveChanges(cancellationToken);
        OcppLocalListSync.Enqueue(jobs, tag.ChargingPointId);
    }
}

/// <summary>Owner/manager/admin: remove a card. Re-adding the same tag later re-enables this row.</summary>
public record RemoveOcppAuthorizedTagCommand(int Id) : IRequest;

public class RemoveOcppAuthorizedTagCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IBackgroundJobClient jobs)
    : IRequestHandler<RemoveOcppAuthorizedTagCommand>
{
    public async Task Handle(RemoveOcppAuthorizedTagCommand request, CancellationToken cancellationToken)
    {
        var tag = await OcppAuthorizedTagLookup.TrackedAsync(db, currentUser, request.Id, cancellationToken);
        tag.IsDeleted = true;
        tag.IsEnabled = false;
        await db.SaveChanges(cancellationToken);
        OcppLocalListSync.Enqueue(jobs, tag.ChargingPointId);
    }
}

/// <summary>
/// Every card change pushes the station's list into its chargers (SendLocalList) so they
/// authorize offline. Fire-and-forget: the API answers the admin at once, the Hangfire
/// server does the pushing and records the result on each charger row.
/// </summary>
public static class OcppLocalListSync
{
    public static void Enqueue(IBackgroundJobClient jobs, int chargingPointId) =>
        jobs.Enqueue<IBackgroundJobService>(s => s.SyncOcppLocalListAsync(chargingPointId, null, CancellationToken.None));
}

internal static class OcppAuthorizedTagLookup
{
    /// <summary>Loads the row and checks the caller may act for its station (owner, active manager or admin).</summary>
    public static async Task<Domain.Enitites.OcppAuthorizedTag> TrackedAsync(
        IApplicationDbContext db, ICurrentUserService currentUser, int id, CancellationToken ct)
    {
        var tag = await db.OcppAuthorizedTags.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct)
                  ?? throw new NotFoundException($"cannot find OCPP authorized tag with id: {id}");

        await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser,
            ProviderAccessGuard.ChargingPoint, tag.ChargingPointId, ct, requiredPrivilege: Cable.Core.Constants.WorkerPrivileges.ConnectControl);
        return tag;
    }
}

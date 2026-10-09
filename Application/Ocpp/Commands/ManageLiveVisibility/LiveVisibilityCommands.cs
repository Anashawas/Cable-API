using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Commands.ManageLiveVisibility;

// ---------------------------------------------------------------------------
// N-2 — who decides whether drivers see a station's live charger data.
//   Owner / manager: ShareLiveStatus (consent).  Admin: LiveStatusBlocked (veto).
//   The veto only switches OFF what the owner switched ON; an admin can never
//   turn sharing on for an owner who turned it off.
// ---------------------------------------------------------------------------

/// <summary>Station owner or active manager (or admin on their behalf) sets the sharing switch.</summary>
public record SetShareLiveStatusCommand(int ChargingPointId, bool Share) : IRequest<OcppLiveVisibilityDto>;

public class SetShareLiveStatusCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<SetShareLiveStatusCommand, OcppLiveVisibilityDto>
{
    public async Task<OcppLiveVisibilityDto> Handle(SetShareLiveStatusCommand request, CancellationToken cancellationToken)
    {
        await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser, ProviderAccessGuard.ChargingPoint, request.ChargingPointId, cancellationToken, requiredPrivilege: Cable.Core.Constants.WorkerPrivileges.ConnectControl);

        var cp = await db.ChargingPoints.FirstOrDefaultAsync(c => c.Id == request.ChargingPointId && !c.IsDeleted, cancellationToken)
                 ?? throw new NotFoundException("cannot find charging point with id: " + request.ChargingPointId);

        cp.ShareLiveStatus = request.Share;
        cp.ShareLiveStatusSetAt = DateTime.UtcNow;
        cp.ShareLiveStatusSetByUserId = currentUser.UserId;
        await db.SaveChanges(cancellationToken);

        return await OcppLiveVisibility.GetAsync(db, cp.Id, cancellationToken);
    }
}

/// <summary>Admin veto. Blocking needs a reason (it is shown to the owner); unblocking clears it.</summary>
public record SetLiveStatusBlockedCommand(int ChargingPointId, bool Blocked, string? Reason) : IRequest<OcppLiveVisibilityDto>;

public class SetLiveStatusBlockedCommandValidator : AbstractValidator<SetLiveStatusBlockedCommand>
{
    public SetLiveStatusBlockedCommandValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300).When(x => x.Blocked)
            .WithMessage("A reason is required when blocking; the station owner sees it.");
    }
}

public class SetLiveStatusBlockedCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<SetLiveStatusBlockedCommand, OcppLiveVisibilityDto>
{
    public async Task<OcppLiveVisibilityDto> Handle(SetLiveStatusBlockedCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);

        var cp = await db.ChargingPoints.FirstOrDefaultAsync(c => c.Id == request.ChargingPointId && !c.IsDeleted, cancellationToken)
                 ?? throw new NotFoundException("cannot find charging point with id: " + request.ChargingPointId);

        cp.LiveStatusBlocked = request.Blocked;
        cp.LiveStatusBlockedAt = request.Blocked ? DateTime.UtcNow : null;
        cp.LiveStatusBlockedByUserId = request.Blocked ? currentUser.UserId : null;
        cp.LiveStatusBlockReason = request.Blocked ? request.Reason?.Trim() : null;
        await db.SaveChanges(cancellationToken);

        return await OcppLiveVisibility.GetAsync(db, cp.Id, cancellationToken);
    }
}

/// <summary>Owner / manager / admin: the current state of the gates for one station.</summary>
public record GetLiveVisibilityRequest(int ChargingPointId) : IRequest<OcppLiveVisibilityDto>;

public class GetLiveVisibilityRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetLiveVisibilityRequest, OcppLiveVisibilityDto>
{
    public async Task<OcppLiveVisibilityDto> Handle(GetLiveVisibilityRequest request, CancellationToken cancellationToken)
    {
        await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser, ProviderAccessGuard.ChargingPoint, request.ChargingPointId, cancellationToken, requiredPrivilege: Cable.Core.Constants.WorkerPrivileges.ConnectView);
        var exists = await db.ChargingPoints.AsNoTracking().AnyAsync(c => c.Id == request.ChargingPointId && !c.IsDeleted, cancellationToken);
        if (!exists) throw new NotFoundException("cannot find charging point with id: " + request.ChargingPointId);
        return await OcppLiveVisibility.GetAsync(db, request.ChargingPointId, cancellationToken);
    }
}

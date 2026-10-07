using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Security.Encryption.Interfaces;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Commands.ManageOcppChargePoint;

// ---------------------------------------------------------------------------
// Update display fields
// ---------------------------------------------------------------------------

public record UpdateOcppChargePointCommand(int Id, string? DisplayName, int? HeartbeatInterval) : IRequest;

public class UpdateOcppChargePointCommandValidator : AbstractValidator<UpdateOcppChargePointCommand>
{
    public UpdateOcppChargePointCommandValidator()
    {
        RuleFor(x => x.DisplayName).MaximumLength(100);
        RuleFor(x => x.HeartbeatInterval).InclusiveBetween(10, 3600).When(x => x.HeartbeatInterval is not null)
            .WithMessage("HeartbeatInterval is seconds, 10–3600. It is handed to the charger on its next boot.");
    }
}

public class UpdateOcppChargePointCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<UpdateOcppChargePointCommand>
{
    public async Task Handle(UpdateOcppChargePointCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var cp = await OcppChargePointLookup.TrackedAsync(db, request.Id, cancellationToken);

        cp.DisplayName = request.DisplayName;
        if (request.HeartbeatInterval is int hb) cp.HeartbeatInterval = hb;
        await db.SaveChanges(cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Rotate / clear password
// ---------------------------------------------------------------------------

/// <summary>
/// New one-time password (or none when <paramref name="RequirePassword"/> is false).
/// The charger keeps working on its current socket; the new credentials apply at its
/// next reconnect, so rotate right before the technician re-types them.
/// </summary>
public record RotateOcppChargePointPasswordCommand(int Id, bool RequirePassword = true) : IRequest<RotateOcppChargePointPasswordResult>;

public record RotateOcppChargePointPasswordResult(int Id, string ChargePointId, string Username, string? Password, string UrlPath, string? WebSocketBaseUrl, int? Port);

public class RotateOcppChargePointPasswordCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IPasswordHasher hasher, IOcppCommandClient ocppServer)
    : IRequestHandler<RotateOcppChargePointPasswordCommand, RotateOcppChargePointPasswordResult>
{
    public async Task<RotateOcppChargePointPasswordResult> Handle(RotateOcppChargePointPasswordCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var cp = await OcppChargePointLookup.TrackedAsync(db, request.Id, cancellationToken);

        var password = request.RequirePassword ? OcppCredentials.GeneratePassword() : null;
        cp.PasswordHash = password is null ? null : hasher.HashPassword(password);
        cp.FailedAuthCount = 0;
        cp.LockedUntil = null;
        await db.SaveChanges(cancellationToken);

        return new RotateOcppChargePointPasswordResult(cp.Id, cp.ChargePointId, cp.ChargePointId, password,
            "/ocpp16/" + cp.ChargePointId, OcppEndpoint.WebSocketBaseUrl(ocppServer.ServerUrl), OcppEndpoint.Port(ocppServer.ServerUrl));
    }
}

// ---------------------------------------------------------------------------
// Enable / disable
// ---------------------------------------------------------------------------

/// <summary>
/// Disabled = the handshake is refused and a live socket is closed within a minute
/// (Cable.Ocpp re-checks on its periodic touch). History and connectors stay.
/// </summary>
public record SetOcppChargePointEnabledCommand(int Id, bool IsEnabled) : IRequest;

public class SetOcppChargePointEnabledCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<SetOcppChargePointEnabledCommand>
{
    public async Task Handle(SetOcppChargePointEnabledCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var cp = await OcppChargePointLookup.TrackedAsync(db, request.Id, cancellationToken);

        cp.IsEnabled = request.IsEnabled;
        if (request.IsEnabled)
        {
            cp.FailedAuthCount = 0;
            cp.LockedUntil = null;
        }
        await db.SaveChanges(cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Delete (soft)
// ---------------------------------------------------------------------------

/// <summary>Soft delete; the id becomes free for re-registration. Refused while a session is open.</summary>
public record DeleteOcppChargePointCommand(int Id) : IRequest;

public class DeleteOcppChargePointCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<DeleteOcppChargePointCommand>
{
    public async Task Handle(DeleteOcppChargePointCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var cp = await OcppChargePointLookup.TrackedAsync(db, request.Id, cancellationToken);

        var openSessions = await db.OcppTransactions.AsNoTracking()
            .CountAsync(t => t.OcppChargePointId == cp.Id && t.IsOpen && !t.IsStale, cancellationToken);
        if (openSessions > 0)
            throw new DataValidationException("Id", $"Charger {cp.ChargePointId} has {openSessions} open session(s). Disable it first and wait for them to close.");

        cp.IsDeleted = true;
        cp.IsEnabled = false;
        await db.SaveChanges(cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Connector display mapping
// ---------------------------------------------------------------------------

/// <summary>Admin: label a connector with the station's plug type and rated power for the apps.</summary>
public record UpdateOcppConnectorCommand(int Id, int? PlugTypeId, decimal? PowerKw) : IRequest;

public class UpdateOcppConnectorCommandValidator : AbstractValidator<UpdateOcppConnectorCommand>
{
    public UpdateOcppConnectorCommandValidator()
    {
        RuleFor(x => x.PowerKw).InclusiveBetween(0.1m, 1000m).When(x => x.PowerKw is not null);
    }
}

public class UpdateOcppConnectorCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<UpdateOcppConnectorCommand>
{
    public async Task Handle(UpdateOcppConnectorCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);

        var connector = await db.OcppConnectors
            .FirstOrDefaultAsync(k => k.Id == request.Id && !k.ChargePoint.IsDeleted, cancellationToken)
            ?? throw new NotFoundException($"cannot find OCPP connector with id: {request.Id}");

        if (request.PlugTypeId is int plugTypeId)
        {
            var exists = await db.PlugTypes.AsNoTracking().AnyAsync(p => p.Id == plugTypeId && !p.IsDeleted, cancellationToken);
            if (!exists) throw new DataValidationException(nameof(request.PlugTypeId), $"Plug type {plugTypeId} does not exist");
        }

        connector.PlugTypeId = request.PlugTypeId;
        connector.PowerKw = request.PowerKw;
        await db.SaveChanges(cancellationToken);
    }
}

internal static class OcppChargePointLookup
{
    public static async Task<Domain.Enitites.OcppChargePoint> TrackedAsync(IApplicationDbContext db, int id, CancellationToken ct)
        => await db.OcppChargePoints.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct)
           ?? throw new NotFoundException($"cannot find OCPP charge point with id: {id}");
}

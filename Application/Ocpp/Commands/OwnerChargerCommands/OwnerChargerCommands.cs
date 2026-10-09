using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Common.Security;
using Application.Ocpp.Commands.ControlOcppChargePoint;
using Application.Ocpp.Queries;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Commands.OwnerChargerCommands;

// ---------------------------------------------------------------------------
// Phase D (partner app) — what a station owner / manager may do with their own
// chargers from the phone: the maintenance commands, the session history, the
// command history. Same mechanism and audit as the admin path; the only difference
// is the guard (owner-or-manager of THAT station) and the smaller action list.
// ---------------------------------------------------------------------------

/// <summary>
/// One maintenance command from the owner. Action: Reset (Type Soft|Hard) · UnlockConnector
/// (ConnectorId) · ChangeAvailability (ConnectorId, Type Operative|Inoperative) ·
/// RemoteStopTransaction (TransactionId) · TriggerMessage (RequestedMessage, ConnectorId?) ·
/// RemoteStartTransaction (ConnectorId) — start a session for a walk-in customer without a card.
/// </summary>
public record OwnerChargerCommand(
    int ChargingPointId,
    int OcppChargePointId,
    string Action,
    int? ConnectorId = null,
    string? Type = null,
    int? TransactionId = null,
    string? RequestedMessage = null) : IRequest<OcppCommandResultDto>;

public class OwnerChargerCommandValidator : AbstractValidator<OwnerChargerCommand>
{
    public static readonly string[] Actions = ["Reset", "UnlockConnector", "ChangeAvailability", "RemoteStopTransaction", "TriggerMessage", "RemoteStartTransaction"];

    public OwnerChargerCommandValidator()
    {
        RuleFor(x => x.Action).Must(a => Actions.Contains(a)).WithMessage("Action must be one of: " + string.Join(", ", Actions));
        When(x => x.Action == "Reset", () => RuleFor(x => x.Type).Must(t => t is "Soft" or "Hard").WithMessage("Type must be Soft or Hard"));
        When(x => x.Action == "UnlockConnector", () => RuleFor(x => x.ConnectorId).NotNull().GreaterThanOrEqualTo(1));
        When(x => x.Action == "ChangeAvailability", () =>
        {
            RuleFor(x => x.ConnectorId).NotNull().GreaterThanOrEqualTo(0);
            RuleFor(x => x.Type).Must(t => t is "Operative" or "Inoperative").WithMessage("Type must be Operative or Inoperative");
        });
        When(x => x.Action == "RemoteStopTransaction", () => RuleFor(x => x.TransactionId).NotNull().GreaterThan(0));
        When(x => x.Action == "RemoteStartTransaction", () => RuleFor(x => x.ConnectorId).NotNull().GreaterThanOrEqualTo(1));
        When(x => x.Action == "TriggerMessage", () => RuleFor(x => x.RequestedMessage)
            .Must(m => TriggerOcppMessageCommandValidator.Messages.Contains(m)).WithMessage("RequestedMessage must be one of: " + string.Join(", ", TriggerOcppMessageCommandValidator.Messages)));
    }
}

public class OwnerChargerCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IOcppCommandClient client)
    : IRequestHandler<OwnerChargerCommand, OcppCommandResultDto>
{
    public async Task<OcppCommandResultDto> Handle(OwnerChargerCommand request, CancellationToken cancellationToken)
    {
        // Access first, so a worker without the control privilege gets a clear 403 before any pre-flight detail.
        await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser, ProviderAccessGuard.ChargingPoint, request.ChargingPointId, cancellationToken,
            requiredPrivilege: WorkerPrivileges.ConnectControl);

        var payload = new Dictionary<string, object>();
        switch (request.Action)
        {
            case "Reset":
                payload["type"] = request.Type!;
                break;
            case "UnlockConnector":
            {
                var status = await db.OcppConnectors.AsNoTracking()
                    .Where(k => k.OcppChargePointId == request.OcppChargePointId && k.ConnectorId == request.ConnectorId)
                    .Select(k => k.Status).FirstOrDefaultAsync(cancellationToken);
                if (status == OcppConnectorStatus.Charging)
                    throw new DataValidationException(nameof(request.ConnectorId), "Connector " + request.ConnectorId + " is charging. Stop the session first.");
                payload["connectorId"] = request.ConnectorId!.Value;
                break;
            }
            case "ChangeAvailability":
                payload["connectorId"] = request.ConnectorId!.Value;
                payload["type"] = request.Type!;
                break;
            case "RemoteStopTransaction":
            {
                var tx = await db.OcppTransactions.AsNoTracking()
                    .Where(t => t.Id == request.TransactionId && t.OcppChargePointId == request.OcppChargePointId)
                    .Select(t => new { t.IsOpen }).FirstOrDefaultAsync(cancellationToken)
                    ?? throw new NotFoundException("cannot find session " + request.TransactionId + " on this charger");
                if (!tx.IsOpen) throw new DataValidationException(nameof(request.TransactionId), "Session " + request.TransactionId + " is already closed.");
                payload["transactionId"] = request.TransactionId!.Value;
                break;
            }
            case "TriggerMessage":
                payload["requestedMessage"] = request.RequestedMessage!;
                if (request.ConnectorId is int c) payload["connectorId"] = c;
                break;
            case "RemoteStartTransaction":
            {
                // The station's virtual tag: the unit accepts it only against this very request (TagAuthorizer).
                await Application.Ocpp.Commands.DriverSessions.OcppRemoteStart.EnsureCanStartAsync(db, request.OcppChargePointId, request.ConnectorId!.Value, cancellationToken);
                payload = Application.Ocpp.Commands.DriverSessions.OcppRemoteStart.Payload(request.ConnectorId!.Value, OcppVirtualTag.ForStation(request.ChargingPointId));
                break;
            }
        }

        return await OcppCommandRunner.RunAsync(db, currentUser, client, request.OcppChargePointId, request.Action, payload, cancellationToken,
            ownerStationId: request.ChargingPointId);
    }
}

// ---------------------------------------------------------------------------
// Session history for the owner
// ---------------------------------------------------------------------------

public record OwnerSessionDto(
    int Id,
    int OcppChargePointId,
    string? ChargerName,
    int ConnectorId,
    string IdTag,
    DateTime StartedAt,
    DateTime? StoppedAt,
    int? DurationSec,
    decimal? EnergyKwh,
    string? StopReason,
    bool IsOpen,
    string StartSource = "Card",
    string? StopReasonText = null,
    int? CostFils = null,
    decimal? CostJod = null);

/// <summary>Owner / manager: sessions at their station, newest first; optional charger and date filters (UTC).</summary>
public record GetMyStationSessionsRequest(int ChargingPointId, int? OcppChargePointId = null, DateTime? From = null, DateTime? To = null, int? Page = null, int? PageSize = null)
    : IRequest<PagedResult<OwnerSessionDto>>;

public class GetMyStationSessionsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetMyStationSessionsRequest, PagedResult<OwnerSessionDto>>
{
    public async Task<PagedResult<OwnerSessionDto>> Handle(GetMyStationSessionsRequest request, CancellationToken cancellationToken)
    {
        await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser, ProviderAccessGuard.ChargingPoint, request.ChargingPointId, cancellationToken, requiredPrivilege: Cable.Core.Constants.WorkerPrivileges.ConnectView);
        var now = DateTime.UtcNow;

        var q = db.OcppTransactions.AsNoTracking()
            .Where(t => t.ChargePoint.ChargingPointId == request.ChargingPointId && !t.ChargePoint.IsDeleted && !t.WasRejected && !t.IsOrphan);
        if (request.OcppChargePointId is int cp) q = q.Where(t => t.OcppChargePointId == cp);
        if (request.From is DateTime from) q = q.Where(t => t.StartedAt >= from);
        if (request.To is DateTime to) q = q.Where(t => t.StartedAt < to);

        var page = await q.OrderByDescending(t => t.StartedAt)
            .Select(t => new OwnerSessionDto(
                t.Id, t.OcppChargePointId, t.ChargePoint.DisplayName, t.ConnectorId, t.IdTag, t.StartedAt, t.StoppedAt,
                null, t.EnergyKwh, t.StopReason, t.IsOpen, t.StartSource, null, t.CostFils, t.CostFils == null ? null : t.CostFils / 1000m))
            .ToPaginatedAsync(request.Page, request.PageSize, 20, 200, cancellationToken);

        // Duration computed in memory: the SQL date-diff helper lives in the SqlServer provider, which Application does not reference.
        return page.As(page.Items.Select(d => d with
        {
            DurationSec = (int)((d.StoppedAt ?? now) - d.StartedAt).TotalSeconds,
            StopReasonText = OcppStopReason.Describe(d.StopReason),
        }).ToList());
    }
}

/// <summary>Owner / manager: the commands sent to one of their chargers (by anyone), newest first.</summary>
public record GetMyChargerCommandsRequest(int ChargingPointId, int OcppChargePointId, int? Take = null) : IRequest<List<OcppCommandDto>>;

public class GetMyChargerCommandsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetMyChargerCommandsRequest, List<OcppCommandDto>>
{
    public async Task<List<OcppCommandDto>> Handle(GetMyChargerCommandsRequest request, CancellationToken cancellationToken)
    {
        await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser, ProviderAccessGuard.ChargingPoint, request.ChargingPointId, cancellationToken, requiredPrivilege: Cable.Core.Constants.WorkerPrivileges.ConnectView);
        var belongs = await db.OcppChargePoints.AsNoTracking()
            .AnyAsync(c => c.Id == request.OcppChargePointId && c.ChargingPointId == request.ChargingPointId && !c.IsDeleted, cancellationToken);
        if (!belongs) throw new NotFoundException("cannot find charger " + request.OcppChargePointId + " at this station");

        var rows = await db.OcppCommands.AsNoTracking()
            .Where(c => c.OcppChargePointId == request.OcppChargePointId && !c.IsDeleted)
            .OrderByDescending(c => c.Id)
            .Take(Math.Clamp(request.Take ?? 30, 1, 200))
            .Select(c => new OcppCommandDto(
                c.Id, c.Action, c.RequestPayload, c.Status, c.ResultStatus, c.ResponsePayload,
                c.ErrorCode, c.ErrorDescription, c.DurationMs, c.CreatedBy,
                db.UserAccounts.Where(u => u.Id == c.CreatedBy).Select(u => u.Name).FirstOrDefault(),
                c.CreatedAt, c.CompletedAt, null))
            .ToListAsync(cancellationToken);
        return rows.Select(d => d with { ConfirmedAfterSec = d.CompletedAt is DateTime done ? (int)Math.Max(0, (done - d.CreatedAt).TotalSeconds) : null }).ToList();
    }
}

using System.Text.Json;
using Application.Common.Interfaces;
using Application.Common.Security;
using Application.Ocpp.Commands.ManageOcppChargePoint;
using Application.Ocpp.Queries;
using Cable.Core;
using Cable.Core.Exceptions;
using Domain.Enitites;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Commands.ControlOcppChargePoint;

// ---------------------------------------------------------------------------
// Phase 2 — central-system commands (server → charger).
//
// Every command here: admin guard → load the charger → POST to Cable.Ocpp, which
// owns the socket and waits for the unit's reply → one OcppCommand audit row →
// the outcome back to the caller. Nothing is retried: a Reset that timed out must
// not fire twice because the first one may well have gone through.
// ---------------------------------------------------------------------------

internal static class OcppCommandRunner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <param name="ownerStationId">
    /// When set, the caller is a station owner / manager acting on that station (partner app):
    /// the owner guard applies instead of the admin guard, and the charger must belong to the station.
    /// </param>
    public static async Task<OcppCommandResultDto> RunAsync(
        IApplicationDbContext db, ICurrentUserService currentUser, IOcppCommandClient client,
        int ocppChargePointId, string action, IDictionary<string, object> payload,
        CancellationToken cancellationToken, int? timeoutSeconds = null, int? ownerStationId = null)
    {
        if (ownerStationId is int stationId)
            await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser, ProviderAccessGuard.ChargingPoint, stationId, cancellationToken);
        else
            await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);

        var cp = await OcppChargePointLookup.TrackedAsync(db, ocppChargePointId, cancellationToken);
        if (ownerStationId is int sid && cp.ChargingPointId != sid)
            throw new NotFoundException("cannot find charger " + ocppChargePointId + " at this station");
        if (!cp.IsEnabled)
            throw new DataValidationException("Id", $"Charger {cp.ChargePointId} is disabled. Enable it before sending commands.");

        var requestJson = JsonSerializer.Serialize(payload, Json);
        var now = DateTime.UtcNow;

        // Rate limit: at most N commands per charger per minute, whoever sends them.
        var lastMinute = await db.OcppCommands.AsNoTracking()
            .CountAsync(c => c.OcppChargePointId == cp.Id && !c.IsDeleted && c.CreatedAt >= now.AddMinutes(-1), cancellationToken);
        if (lastMinute >= Cable.Core.Constants.OcppLimits.CommandsPerChargerPerMinute)
            throw new DataValidationException("Id",
                $"Too many commands to {cp.ChargePointId}: {lastMinute} in the last minute (limit {Cable.Core.Constants.OcppLimits.CommandsPerChargerPerMinute}). Wait a moment.");

        // Duplicate protection: the same confirmable command, still unconfirmed and recent, is not sent twice
        // (a Reset that was accepted but has not rebooted yet must not be followed by another Reset).
        if (Cable.Core.Constants.OcppLimits.ConfirmableCommands.Contains(action))
        {
            var since = now - Cable.Core.Constants.OcppLimits.DuplicateCommandWindow;
            var pendingTwin = await db.OcppCommands.AsNoTracking()
                .Where(c => c.OcppChargePointId == cp.Id && c.Action == action && c.RequestPayload == requestJson && !c.IsDeleted
                            && c.Status == "Answered" && c.CompletedAt == null && c.CreatedAt >= since
                            && c.ResultStatus != "Rejected" && c.ResultStatus != "NotSupported" && c.ResultStatus != "UnlockFailed")
                .OrderByDescending(c => c.Id)
                .Select(c => new { c.Id, c.CreatedAt })
                .FirstOrDefaultAsync(cancellationToken);
            if (pendingTwin is not null)
                throw new DataValidationException("Id",
                    $"The same {action} was accepted {(int)(now - pendingTwin.CreatedAt).TotalSeconds} s ago and the charger has not confirmed it yet. Wait for the confirmation (or up to {(int)Cable.Core.Constants.OcppLimits.DuplicateCommandWindow.TotalSeconds} s) before sending it again.");
        }

        var outcome = await client.SendAsync(cp.ChargePointId, action, payload, cancellationToken, timeoutSeconds);

        var row = new OcppCommand
        {
            OcppChargePointId = cp.Id,
            Action = action,
            RequestPayload = requestJson,
            Status = outcome.Status,
            ResultStatus = outcome.Answered ? ReadStatus(outcome.Payload) : null,
            ResponsePayload = outcome.Payload,
            ErrorCode = outcome.ErrorCode,
            ErrorDescription = Truncate(outcome.ErrorDescription, 500),
            DurationMs = (int)Math.Min(outcome.ElapsedMs, int.MaxValue),
        };
        db.OcppCommands.Add(row);
        await db.SaveChanges(cancellationToken);

        return new OcppCommandResultDto(row.Id, action, row.Status, row.ResultStatus, row.ResponsePayload,
            row.ErrorCode, row.ErrorDescription, outcome.ElapsedMs, IsPositive(row.ResultStatus));
    }

    /// <summary>The unit's verdict: every 1.6 CALLRESULT we send carries a top-level "status".</summary>
    private static string? ReadStatus(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Accepted / Scheduled / Unlocked count as "it worked"; GetConfiguration has no status and is positive when answered.</summary>
    private static bool IsPositive(string? resultStatus) => resultStatus is null or "Accepted" or "Scheduled" or "Unlocked";

    private static string? Truncate(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}

// ---------------------------------------------------------------------------
// TriggerMessage — "refresh now"
// ---------------------------------------------------------------------------

/// <summary>Asks the charger to send one message now (StatusNotification for all plugs when ConnectorId is null).</summary>
public record TriggerOcppMessageCommand(int Id, string RequestedMessage, int? ConnectorId = null) : IRequest<OcppCommandResultDto>;

public class TriggerOcppMessageCommandValidator : AbstractValidator<TriggerOcppMessageCommand>
{
    public static readonly string[] Messages =
        ["BootNotification", "DiagnosticsStatusNotification", "FirmwareStatusNotification", "Heartbeat", "MeterValues", "StatusNotification"];

    public TriggerOcppMessageCommandValidator()
    {
        RuleFor(x => x.RequestedMessage).Must(m => Messages.Contains(m))
            .WithMessage($"RequestedMessage must be one of: {string.Join(", ", Messages)}");
        RuleFor(x => x.ConnectorId).GreaterThanOrEqualTo(0).When(x => x.ConnectorId is not null);
    }
}

public class TriggerOcppMessageCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IOcppCommandClient client)
    : IRequestHandler<TriggerOcppMessageCommand, OcppCommandResultDto>
{
    public Task<OcppCommandResultDto> Handle(TriggerOcppMessageCommand request, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object> { ["requestedMessage"] = request.RequestedMessage };
        if (request.ConnectorId is int c) payload["connectorId"] = c;
        return OcppCommandRunner.RunAsync(db, currentUser, client, request.Id, "TriggerMessage", payload, cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Reset
// ---------------------------------------------------------------------------

/// <summary>Soft = restart the OCPP software after finishing any running session; Hard = full reboot now.</summary>
public record ResetOcppChargePointCommand(int Id, string Type) : IRequest<OcppCommandResultDto>;

public class ResetOcppChargePointCommandValidator : AbstractValidator<ResetOcppChargePointCommand>
{
    public ResetOcppChargePointCommandValidator()
    {
        RuleFor(x => x.Type).Must(t => t is "Soft" or "Hard").WithMessage("Type must be Soft or Hard");
    }
}

public class ResetOcppChargePointCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IOcppCommandClient client)
    : IRequestHandler<ResetOcppChargePointCommand, OcppCommandResultDto>
{
    public Task<OcppCommandResultDto> Handle(ResetOcppChargePointCommand request, CancellationToken cancellationToken) =>
        OcppCommandRunner.RunAsync(db, currentUser, client, request.Id, "Reset",
            new Dictionary<string, object> { ["type"] = request.Type }, cancellationToken);
}

// ---------------------------------------------------------------------------
// UnlockConnector
// ---------------------------------------------------------------------------

/// <summary>Releases the cable lock on one plug; the unit stops any running session first.</summary>
public record UnlockOcppConnectorCommand(int Id, int ConnectorId) : IRequest<OcppCommandResultDto>;

public class UnlockOcppConnectorCommandValidator : AbstractValidator<UnlockOcppConnectorCommand>
{
    public UnlockOcppConnectorCommandValidator()
    {
        RuleFor(x => x.ConnectorId).GreaterThanOrEqualTo(1).WithMessage("ConnectorId must be a plug (1..n), not 0");
    }
}

public class UnlockOcppConnectorCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IOcppCommandClient client)
    : IRequestHandler<UnlockOcppConnectorCommand, OcppCommandResultDto>
{
    public async Task<OcppCommandResultDto> Handle(UnlockOcppConnectorCommand request, CancellationToken cancellationToken)
    {
        // Safety: never release a cable that is carrying current. Stop the session first (or let the driver).
        var status = await db.OcppConnectors.AsNoTracking()
            .Where(k => k.OcppChargePointId == request.Id && k.ConnectorId == request.ConnectorId)
            .Select(k => k.Status)
            .FirstOrDefaultAsync(cancellationToken);
        if (status == Cable.Core.Constants.OcppConnectorStatus.Charging)
            throw new DataValidationException(nameof(request.ConnectorId),
                "Connector " + request.ConnectorId + " is charging. Unlocking a live cable is refused; stop the session first.");

        return await OcppCommandRunner.RunAsync(db, currentUser, client, request.Id, "UnlockConnector",
            new Dictionary<string, object> { ["connectorId"] = request.ConnectorId }, cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// ChangeAvailability — out of service / back in service
// ---------------------------------------------------------------------------

/// <summary>ConnectorId 0 = the whole unit. Inoperative plugs report Unavailable; a running session finishes first (Scheduled).</summary>
public record ChangeOcppAvailabilityCommand(int Id, int ConnectorId, string Type) : IRequest<OcppCommandResultDto>;

public class ChangeOcppAvailabilityCommandValidator : AbstractValidator<ChangeOcppAvailabilityCommand>
{
    public ChangeOcppAvailabilityCommandValidator()
    {
        RuleFor(x => x.ConnectorId).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Type).Must(t => t is "Operative" or "Inoperative").WithMessage("Type must be Operative or Inoperative");
    }
}

public class ChangeOcppAvailabilityCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IOcppCommandClient client)
    : IRequestHandler<ChangeOcppAvailabilityCommand, OcppCommandResultDto>
{
    public Task<OcppCommandResultDto> Handle(ChangeOcppAvailabilityCommand request, CancellationToken cancellationToken) =>
        OcppCommandRunner.RunAsync(db, currentUser, client, request.Id, "ChangeAvailability",
            new Dictionary<string, object> { ["connectorId"] = request.ConnectorId, ["type"] = request.Type }, cancellationToken);
}

// ---------------------------------------------------------------------------
// RemoteStopTransaction — end a running session from the admin / partner app
// ---------------------------------------------------------------------------

/// <summary>
/// Phase 2 safety tool (not the phase-3 remote start): stops the open session with this
/// transaction id. The unit answers Accepted / Rejected and then sends its own
/// StopTransaction (reason Remote), which closes the session row and confirms the command.
/// </summary>
public record RemoteStopOcppTransactionCommand(int Id, int TransactionId) : IRequest<OcppCommandResultDto>;

public class RemoteStopOcppTransactionCommandValidator : AbstractValidator<RemoteStopOcppTransactionCommand>
{
    public RemoteStopOcppTransactionCommandValidator()
    {
        RuleFor(x => x.TransactionId).GreaterThan(0);
    }
}

public class RemoteStopOcppTransactionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IOcppCommandClient client)
    : IRequestHandler<RemoteStopOcppTransactionCommand, OcppCommandResultDto>
{
    public async Task<OcppCommandResultDto> Handle(RemoteStopOcppTransactionCommand request, CancellationToken cancellationToken)
    {
        var tx = await db.OcppTransactions.AsNoTracking()
            .Where(t => t.Id == request.TransactionId && t.OcppChargePointId == request.Id)
            .Select(t => new { t.IsOpen, t.IsStale, t.ConnectorId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("cannot find session " + request.TransactionId + " on this charger");
        if (!tx.IsOpen)
            throw new DataValidationException(nameof(request.TransactionId), "Session " + request.TransactionId + " is already closed.");

        return await OcppCommandRunner.RunAsync(db, currentUser, client, request.Id, "RemoteStopTransaction",
            new Dictionary<string, object> { ["transactionId"] = request.TransactionId }, cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// GetConfiguration / ChangeConfiguration — the unit's settings
// ---------------------------------------------------------------------------

/// <summary>Empty Keys = every key the unit has. The reply's configurationKey[] carries key, readonly, value.</summary>
public record GetOcppConfigurationCommand(int Id, List<string>? Keys = null) : IRequest<OcppCommandResultDto>;

public class GetOcppConfigurationCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IOcppCommandClient client)
    : IRequestHandler<GetOcppConfigurationCommand, OcppCommandResultDto>
{
    public Task<OcppCommandResultDto> Handle(GetOcppConfigurationCommand request, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object>();
        var keys = request.Keys?.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct().ToList();
        if (keys is { Count: > 0 }) payload["key"] = keys;
        return OcppCommandRunner.RunAsync(db, currentUser, client, request.Id, "GetConfiguration", payload, cancellationToken);
    }
}

/// <summary>Sets one key. The unit answers Accepted, Rejected, RebootRequired or NotSupported.</summary>
public record ChangeOcppConfigurationCommand(int Id, string Key, string Value) : IRequest<OcppCommandResultDto>;

public class ChangeOcppConfigurationCommandValidator : AbstractValidator<ChangeOcppConfigurationCommand>
{
    public ChangeOcppConfigurationCommandValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(50);     // OCPP 1.6: CiString50Type
        RuleFor(x => x.Value).NotNull().MaximumLength(500);   // CiString500Type
        // Keys that point the unit at a server or define its identity are never editable from a screen.
        RuleFor(x => x.Key).Must(k => !Cable.Core.Constants.OcppProtectedConfigurationKeys.IsProtected(k))
            .WithMessage("This key defines where the charger connects or who it is. It is protected and can only be changed on the unit itself.");
    }
}

public class ChangeOcppConfigurationCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IOcppCommandClient client)
    : IRequestHandler<ChangeOcppConfigurationCommand, OcppCommandResultDto>
{
    public Task<OcppCommandResultDto> Handle(ChangeOcppConfigurationCommand request, CancellationToken cancellationToken) =>
        OcppCommandRunner.RunAsync(db, currentUser, client, request.Id, "ChangeConfiguration",
            new Dictionary<string, object> { ["key"] = request.Key.Trim(), ["value"] = request.Value }, cancellationToken);
}

// ---------------------------------------------------------------------------
// SendLocalList — "sync cards now" for one charger (the automatic path is the Hangfire job)
// ---------------------------------------------------------------------------

/// <summary>Pushes the station's current card list into this charger right now and reports the new sync state.</summary>
public record SyncOcppLocalListCommand(int Id) : IRequest<OcppLocalListStateDto>;

public class SyncOcppLocalListCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IBackgroundJobService jobs)
    : IRequestHandler<SyncOcppLocalListCommand, OcppLocalListStateDto>
{
    public async Task<OcppLocalListStateDto> Handle(SyncOcppLocalListCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var cp = await OcppChargePointLookup.TrackedAsync(db, request.Id, cancellationToken);
        if (!cp.IsEnabled)
            throw new DataValidationException("Id", $"Charger {cp.ChargePointId} is disabled. Enable it before syncing cards.");

        var confirmed = await jobs.SyncOcppLocalListAsync(cp.ChargingPointId, cp.Id, cancellationToken);

        var fresh = await db.OcppChargePoints.AsNoTracking()
            .Where(c => c.Id == cp.Id)
            .Select(c => new { c.LocalListStatus, c.LocalListVersion, c.LocalListSyncedAt })
            .FirstAsync(cancellationToken);
        var cards = await db.OcppAuthorizedTags.AsNoTracking()
            .CountAsync(t => t.ChargingPointId == cp.ChargingPointId && t.IsEnabled && !t.IsDeleted, cancellationToken);

        return new OcppLocalListStateDto(fresh.LocalListStatus, fresh.LocalListVersion, fresh.LocalListSyncedAt, cards, confirmed == 1);
    }
}

// ---------------------------------------------------------------------------
// N-6 — recompute reliability on demand (the daily job does it at 03:30 UTC)
// ---------------------------------------------------------------------------

public record RecomputeOcppReliabilityCommand : IRequest<int>;

public class RecomputeOcppReliabilityCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IBackgroundJobService jobs)
    : IRequestHandler<RecomputeOcppReliabilityCommand, int>
{
    public async Task<int> Handle(RecomputeOcppReliabilityCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        return await jobs.ComputeOcppReliabilityAsync(cancellationToken);
    }
}

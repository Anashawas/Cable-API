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

    public static async Task<OcppCommandResultDto> RunAsync(
        IApplicationDbContext db, ICurrentUserService currentUser, IOcppCommandClient client,
        int ocppChargePointId, string action, IDictionary<string, object> payload,
        CancellationToken cancellationToken, int? timeoutSeconds = null)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var cp = await OcppChargePointLookup.TrackedAsync(db, ocppChargePointId, cancellationToken);
        if (!cp.IsEnabled)
            throw new DataValidationException("Id", $"Charger {cp.ChargePointId} is disabled. Enable it before sending commands.");

        var outcome = await client.SendAsync(cp.ChargePointId, action, payload, cancellationToken, timeoutSeconds);

        var row = new OcppCommand
        {
            OcppChargePointId = cp.Id,
            Action = action,
            RequestPayload = JsonSerializer.Serialize(payload, Json),
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

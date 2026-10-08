using Application.Common.Models;
using Application.Ocpp.Commands.AddOcppAuthorizedTag;
using Application.Ocpp;
using Application.Ocpp.Commands.ControlOcppChargePoint;
using Application.Ocpp.Commands.ManageLiveVisibility;
using Application.Ocpp.Commands.ManageOcppAuthorizedTags;
using Application.Ocpp.Commands.ManageOcppChargePoint;
using Application.Ocpp.Commands.RegisterOcppChargePoint;
using Application.Ocpp.Queries;
using Application.Ocpp.Queries.GetOcppAlerts;
using Application.Ocpp.Queries.GetOcppAuthorizedTags;
using Application.Ocpp.Queries.GetOcppChargePointById;
using Application.Ocpp.Queries.GetOcppChargePoints;
using Application.Ocpp.Queries.GetOcppCommands;
using Application.Ocpp.Queries.GetOcppFleetHealth;
using Application.Ocpp.Queries.GetOcppRawMessages;
using Application.Ocpp.Queries.GetStationLive;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

/// <summary>
/// Cable Connect (OCPP 1.6J) management. The chargers themselves never touch these —
/// they talk to the separate Cable.Ocpp host. Provider live/history endpoints follow
/// in phase D under /api/provider.
/// </summary>
public static class OcppRoutes
{
    public record SetEnabledRequest(bool IsEnabled);
    public record RotatePasswordRequest(bool RequirePassword = true);
    public record UpdateChargePointRequest(string? DisplayName, int? HeartbeatInterval);
    public record UpdateConnectorRequest(int? PlugTypeId, decimal? PowerKw);
    public record AddAuthorizedTagRequest(int ChargingPointId, string IdTag, string? Label, DateTime? ExpiresAt);

    // Phase 2 — control
    public record TriggerMessageRequest(string RequestedMessage, int? ConnectorId);
    public record ResetRequest(string Type);
    public record UnlockConnectorRequest(int ConnectorId);
    public record ChangeAvailabilityRequest(int ConnectorId, string Type);
    public record GetConfigurationRequest(List<string>? Keys);
    public record ChangeConfigurationRequest(string Key, string Value);
    public record RemoteStopRequest(int TransactionId);
    public record ShareLiveStatusRequest(bool Share);
    public record LiveStatusBlockRequest(bool Blocked, string? Reason);
    public record ChargerDisplayNameRequest(string? DisplayName);

    public static IEndpointRouteBuilder MapOcppRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/admin/ocpp")
            .WithTags("OCPP")
            .MapChargePointRoutes()
            .MapControlRoutes()
            .MapLiveVisibilityRoutes()
            .MapAuthorizedTagRoutes();

        // Station owner / managers (partner app): the sharing switch, their live picture, cabinet names.
        app.MapGroup("/api/provider/charging-points")
            .WithTags("Provider - Charging Points")
            .MapProviderLiveVisibilityRoutes()
            .MapProviderLiveRoutes();

        // Drivers (Cable app): the live picture of a station, gated by N-2.
        app.MapGroup("/api/charging-points")
            .WithTags("Charging Points")
            .MapDriverLiveRoutes();

        return app;
    }

    /// <summary>N-3 / N-4 / N-7: what the driver app shows on a station.</summary>
    private static RouteGroupBuilder MapDriverLiveRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/{id:int}/live", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetStationLiveRequest(id), ct)))
            .Produces<StationLiveDto>()
            .ProducesNotFound().ProducesInternalServerError()
            .WithName("Get station live (driver app)")
            .WithSummary("The station as a driver sees it: free / busy / out-of-order per plug, grouped per plug type, all cabinets aggregated, owner-named. Empty with unavailableReason (NoSubscription | NotShared | Blocked | NoChargers | Offline) unless the sharing gates are open and a charger is online.")
            .WithOpenApi();

        app.MapGet("/live-summary", async (IMediator mediator, [FromQuery] string ids, CancellationToken ct) =>
            {
                var list = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => int.TryParse(x, out var v) ? v : 0).Where(v => v > 0).ToList();
                return Results.Ok(await mediator.Send(new GetStationsLiveSummaryRequest(list), ct));
            })
            .Produces<List<StationLiveSummaryDto>>()
            .ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Get stations live summary (driver app)")
            .WithSummary("Badge data for the stations on screen: ids=1,2,3 (max 50). Per station: available, free / total per plug type, updatedAt. Stations without chargers are omitted.")
            .WithOpenApi();

        return app;
    }

    /// <summary>Partner app: live picture of the owner's station and cabinet naming.</summary>
    private static RouteGroupBuilder MapProviderLiveRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/{chargingPointId:int}/live", async (IMediator mediator, [FromRoute] int chargingPointId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetMyStationLiveRequest(chargingPointId), ct)))
            .Produces<StationLiveDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Provider: get station live")
            .WithSummary("Owner / manager: the same live picture drivers get, without the sharing gates (it is their own data).")
            .WithOpenApi();

        app.MapPut("/{chargingPointId:int}/chargers/{ocppChargePointId:int}/display-name", async (IMediator mediator, [FromRoute] int chargingPointId, [FromRoute] int ocppChargePointId, ChargerDisplayNameRequest body, CancellationToken ct) =>
            {
                await mediator.Send(new SetChargerDisplayNameCommand(chargingPointId, ocppChargePointId, body.DisplayName), ct);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Provider: set charger display name")
            .WithSummary("Owner / manager: name a cabinet the way drivers recognise it (\"the right-hand charger\"). Empty = app default. The OCPP id is never shown to drivers.")
            .WithOpenApi();

        return app;
    }

    /// <summary>N-2: live-data visibility gates — admin side (read + veto).</summary>
    private static RouteGroupBuilder MapLiveVisibilityRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/stations/{chargingPointId:int}/live-visibility", async (IMediator mediator, [FromRoute] int chargingPointId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetLiveVisibilityRequest(chargingPointId), ct)))
            .Produces<OcppLiveVisibilityDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Get OCPP live visibility")
            .WithSummary("Admin / owner: the gates between this station's live charger data and the driver app — subscription, owner sharing switch, admin block — and the resulting yes/no")
            .WithOpenApi();

        app.MapPut("/stations/{chargingPointId:int}/live-visibility/block", async (IMediator mediator, [FromRoute] int chargingPointId, LiveStatusBlockRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new SetLiveStatusBlockedCommand(chargingPointId, body.Blocked, body.Reason), ct)))
            .Produces<OcppLiveVisibilityDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Set OCPP live visibility block")
            .WithSummary("Admin veto: hide this station's live data from drivers (reason required, shown to the owner). It cannot turn sharing on — only the owner can.")
            .WithOpenApi();

        app.MapPut("/stations/{chargingPointId:int}/live-visibility/share", async (IMediator mediator, [FromRoute] int chargingPointId, ShareLiveStatusRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new SetShareLiveStatusCommand(chargingPointId, body.Share), ct)))
            .Produces<OcppLiveVisibilityDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Set OCPP sharing (admin on behalf of the owner)")
            .WithSummary("Admin acting for the owner (e.g. by phone request): sets the owner's sharing switch. Recorded as the admin's decision.")
            .WithOpenApi();

        return app;
    }

    /// <summary>N-2: the owner's switch, for the partner app.</summary>
    private static RouteGroupBuilder MapProviderLiveVisibilityRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/{chargingPointId:int}/live-visibility", async (IMediator mediator, [FromRoute] int chargingPointId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetLiveVisibilityRequest(chargingPointId), ct)))
            .Produces<OcppLiveVisibilityDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Provider: get live visibility")
            .WithSummary("Owner / manager: is this station's live charger data shown to drivers, and why not (subscription off, sharing off, blocked by Cable with a reason)")
            .WithOpenApi();

        app.MapPut("/{chargingPointId:int}/live-visibility/share", async (IMediator mediator, [FromRoute] int chargingPointId, ShareLiveStatusRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new SetShareLiveStatusCommand(chargingPointId, body.Share), ct)))
            .Produces<OcppLiveVisibilityDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Provider: set live sharing")
            .WithSummary("Owner / manager: share this station's live plug states (free / charging / faulted, plug type, power) with drivers in the Cable app. On by default with the Cable Connect subscription; switching it off hides the data at once.")
            .WithOpenApi();

        return app;
    }

    private static RouteGroupBuilder MapChargePointRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/fleet-health", async (IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetOcppFleetHealthRequest(), ct)))
            .Produces<OcppFleetHealthDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Get OCPP fleet health")
            .WithSummary("Admin dashboard tile: chargers online / reconnecting / offline, free and faulted connectors, open and stale sessions, today's kWh")
            .WithOpenApi();

        app.MapGet("/alerts", async (IMediator mediator, [FromQuery] bool? openOnly, [FromQuery] int? chargePointId, [FromQuery] int? take, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetOcppAlertsRequest(openOnly ?? true, chargePointId, take), ct)))
            .Produces<List<OcppAlertDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Get OCPP alerts")
            .WithSummary("Admin: the alert job's findings (charger offline > 15 min, plug Faulted > 15 min, session open > 6 h). openOnly=true by default; chargePointId for one charger's history.")
            .WithOpenApi();

        app.MapGet("/charge-points", async (IMediator mediator,
                    [FromQuery] int? chargingPointId, [FromQuery] string? search, [FromQuery] bool? isEnabled,
                    [FromQuery] string? connectionState, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetOcppChargePointsRequest(chargingPointId, search, isEnabled, connectionState, page, pageSize), ct)))
            .Produces<PagedResult<OcppChargePointListItemDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Get OCPP charge points")
            .WithSummary("Admin: registered chargers with live connection state, connector counts and the station's Cable Connect subscription")
            .WithDescription("connectionState = Online (socket open and a message within 2.5× heartbeat) | Reconnecting (gap under 2 min) | Offline. search matches id, display name, station, serial, vendor, model. Default page size 20, max 200.")
            .WithOpenApi();

        app.MapGet("/charge-points/{id:int}", async (IMediator mediator, [FromRoute] int id, [FromQuery] int? recentTransactions, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetOcppChargePointByIdRequest(id, recentTransactions ?? 10), ct)))
            .Produces<OcppChargePointDetailDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Get OCPP charge point")
            .WithSummary("Admin: one charger — boot info, connectors with plug mapping, recent sessions, today's totals, credentials state")
            .WithOpenApi();

        app.MapPost("/charge-points",
                async (IMediator mediator, RegisterOcppChargePointCommand request, CancellationToken ct) =>
                    Results.Ok(await mediator.Send(request, ct)))
            .Produces<RegisterOcppChargePointResult>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Register OCPP charge point")
            .WithSummary("Admin: register a physical charger under a station so Cable.Ocpp accepts its connection")
            .WithDescription("Returns what the technician types into the charger: urlPath (append to wss://ocpp.cable-app.com), username (= chargePointId) and the one-time password. chargePointId null = generated CBL-{station}-{nn}; pass the charger's existing id (e.g. RH4) to keep it. requirePassword=false for units on OCPP Security Profile 0. Vendor/model/firmware fill in automatically on the first BootNotification.")
            .WithOpenApi();

        app.MapPut("/charge-points/{id:int}", async (IMediator mediator, [FromRoute] int id, UpdateChargePointRequest body, CancellationToken ct) =>
            {
                await mediator.Send(new UpdateOcppChargePointCommand(id, body.DisplayName, body.HeartbeatInterval), ct);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Update OCPP charge point")
            .WithSummary("Admin: display name and heartbeat interval (applied at the charger's next boot)")
            .WithOpenApi();

        app.MapPost("/charge-points/{id:int}/rotate-password", async (IMediator mediator, [FromRoute] int id, RotatePasswordRequest? body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new RotateOcppChargePointPasswordCommand(id, body?.RequirePassword ?? true), ct)))
            .Produces<RotateOcppChargePointPasswordResult>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Rotate OCPP charge point password")
            .WithSummary("Admin: issue a new one-time password (requirePassword=false clears it for Security Profile 0 units). Applies at the charger's next reconnect.")
            .WithOpenApi();

        app.MapPut("/charge-points/{id:int}/enabled", async (IMediator mediator, [FromRoute] int id, SetEnabledRequest body, CancellationToken ct) =>
            {
                await mediator.Send(new SetOcppChargePointEnabledCommand(id, body.IsEnabled), ct);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Enable or disable OCPP charge point")
            .WithSummary("Admin: disabled = handshake refused and any live socket closed within a minute; history kept")
            .WithOpenApi();

        app.MapDelete("/charge-points/{id:int}", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
            {
                await mediator.Send(new DeleteOcppChargePointCommand(id), ct);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Delete OCPP charge point")
            .WithSummary("Admin: soft delete (refused while a session is open). The id becomes free for re-registration.")
            .WithOpenApi();

        app.MapGet("/charge-points/{id:int}/raw-messages", async (IMediator mediator, [FromRoute] int id,
                    [FromQuery] int? take, [FromQuery] string? direction, [FromQuery] string? action, [FromQuery] long? beforeId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetOcppRawMessagesRequest(id, take, direction, action, beforeId), ct)))
            .Produces<List<OcppRawMessageDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Get OCPP raw messages")
            .WithSummary("Admin support tool: the last frames exchanged with a charger, newest first (direction in|out|sys, action, beforeId for older pages)")
            .WithOpenApi();

        app.MapPut("/connectors/{id:int}", async (IMediator mediator, [FromRoute] int id, UpdateConnectorRequest body, CancellationToken ct) =>
            {
                await mediator.Send(new UpdateOcppConnectorCommand(id, body.PlugTypeId, body.PowerKw), ct);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Update OCPP connector")
            .WithSummary("Admin: label a connector with the plug type and rated kW shown in the apps")
            .WithOpenApi();

        return app;
    }

    /// <summary>
    /// Server → charger commands. The API forwards each one to Cable.Ocpp, which owns the
    /// socket and waits (30 s) for the unit's reply; every call is written to OcppCommand.
    /// The response's status is the transport outcome, resultStatus the unit's own verdict,
    /// accepted folds both. Nothing here is retried automatically.
    /// </summary>
    private static RouteGroupBuilder MapControlRoutes(this RouteGroupBuilder app)
    {
        app.MapPost("/charge-points/{id:int}/commands/trigger-message", async (IMediator mediator, [FromRoute] int id, TriggerMessageRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new TriggerOcppMessageCommand(id, body.RequestedMessage, body.ConnectorId), ct)))
            .Produces<OcppCommandResultDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("OCPP TriggerMessage")
            .WithSummary("Admin: ask the charger to send a message now — StatusNotification (all plugs when connectorId is null), Heartbeat, MeterValues, BootNotification")
            .WithOpenApi();

        app.MapPost("/charge-points/{id:int}/commands/reset", async (IMediator mediator, [FromRoute] int id, ResetRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new ResetOcppChargePointCommand(id, body.Type), ct)))
            .Produces<OcppCommandResultDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("OCPP Reset")
            .WithSummary("Admin: reboot the charger. Soft = after any running session ends; Hard = immediately. It reconnects and boots again by itself.")
            .WithOpenApi();

        app.MapPost("/charge-points/{id:int}/commands/unlock-connector", async (IMediator mediator, [FromRoute] int id, UnlockConnectorRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new UnlockOcppConnectorCommand(id, body.ConnectorId), ct)))
            .Produces<OcppCommandResultDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("OCPP UnlockConnector")
            .WithSummary("Admin: release a stuck cable on one plug (resultStatus Unlocked | UnlockFailed | NotSupported)")
            .WithOpenApi();

        app.MapPost("/charge-points/{id:int}/commands/change-availability", async (IMediator mediator, [FromRoute] int id, ChangeAvailabilityRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new ChangeOcppAvailabilityCommand(id, body.ConnectorId, body.Type), ct)))
            .Produces<OcppCommandResultDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("OCPP ChangeAvailability")
            .WithSummary("Admin: take a plug (or the whole unit, connectorId 0) out of service / back in service. Inoperative plugs show Unavailable in the apps; Scheduled = after the running session.")
            .WithOpenApi();

        app.MapPost("/charge-points/{id:int}/commands/remote-stop", async (IMediator mediator, [FromRoute] int id, RemoteStopRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new RemoteStopOcppTransactionCommand(id, body.TransactionId), ct)))
            .Produces<OcppCommandResultDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("OCPP RemoteStopTransaction")
            .WithSummary("Admin: stop a running session (transactionId = the open OcppTransaction id). The unit answers Accepted / Rejected and then sends StopTransaction (reason Remote), which closes the session and confirms the command.")
            .WithOpenApi();

        app.MapPost("/charge-points/{id:int}/commands/get-configuration", async (IMediator mediator, [FromRoute] int id, GetConfigurationRequest? body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetOcppConfigurationCommand(id, body?.Keys), ct)))
            .Produces<OcppCommandResultDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("OCPP GetConfiguration")
            .WithSummary("Admin: read the unit's configuration keys (all when keys is empty). responsePayload.configurationKey[] = { key, readonly, value }; unknownKey[] lists what it does not have.")
            .WithOpenApi();

        app.MapPost("/charge-points/{id:int}/commands/change-configuration", async (IMediator mediator, [FromRoute] int id, ChangeConfigurationRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new ChangeOcppConfigurationCommand(id, body.Key, body.Value), ct)))
            .Produces<OcppCommandResultDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("OCPP ChangeConfiguration")
            .WithSummary("Admin: set one configuration key (resultStatus Accepted | Rejected | RebootRequired | NotSupported)")
            .WithOpenApi();

        app.MapPost("/charge-points/{id:int}/commands/sync-local-list", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new SyncOcppLocalListCommand(id), ct)))
            .Produces<OcppLocalListStateDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("OCPP SendLocalList now")
            .WithSummary("Admin: push the station's allowed cards into this charger now (SendLocalList Full + ClearCache). The automatic push runs after every card change and when a Pending unit boots.")
            .WithOpenApi();

        app.MapPost("/reliability/recompute", async (IMediator mediator, CancellationToken ct) =>
                Results.Ok(new { chargers = await mediator.Send(new RecomputeOcppReliabilityCommand(), ct) }))
            .Produces(200)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Recompute OCPP reliability")
            .WithSummary("Admin: recompute the 30-day reliability score of every charger now (the daily job runs at 03:30 UTC)")
            .WithOpenApi();

        app.MapGet("/charge-points/{id:int}/commands", async (IMediator mediator, [FromRoute] int id, [FromQuery] int? take, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetOcppCommandsRequest(id, take), ct)))
            .Produces<List<OcppCommandDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Get OCPP commands")
            .WithSummary("Admin: commands sent to this charger, newest first — who asked, what was sent, what the unit answered")
            .WithOpenApi();

        return app;
    }

    private static RouteGroupBuilder MapAuthorizedTagRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/authorized-tags", async (IMediator mediator, [FromQuery] int chargingPointId, [FromQuery] bool? includeDisabled, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetOcppAuthorizedTagsRequest(chargingPointId, includeDisabled ?? true), ct)))
            .Produces<List<OcppAuthorizedTagDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Get OCPP authorized tags")
            .WithSummary("Admin or station owner/manager: the cards / passwords allowed to charge at a station")
            .WithOpenApi();

        app.MapPost("/authorized-tags",
                async (IMediator mediator, AddAuthorizedTagRequest body, CancellationToken ct) =>
                    Results.Ok(new { id = await mediator.Send(new AddOcppAuthorizedTagCommand(body.ChargingPointId, body.IdTag, body.Label, body.ExpiresAt), ct) }))
            .Produces(200)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Add OCPP authorized tag")
            .WithSummary("Admin or station owner/manager: allow a card / password to charge at a station")
            .WithDescription("Cable.Ocpp answers Authorize/StartTransaction with Accepted only for tags on this list (Ocpp:AuthorizeMode=List). Tags are matched trimmed and case-insensitive. Re-adding a removed tag re-enables it. ExpiresAt is Jordan local time.")
            .WithOpenApi();

        app.MapPut("/authorized-tags/{id:int}/enabled", async (IMediator mediator, [FromRoute] int id, SetEnabledRequest body, CancellationToken ct) =>
            {
                await mediator.Send(new SetOcppAuthorizedTagEnabledCommand(id, body.IsEnabled), ct);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Enable or disable OCPP authorized tag")
            .WithSummary("Admin or station owner/manager: pause or resume a card without removing it")
            .WithOpenApi();

        app.MapDelete("/authorized-tags/{id:int}", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
            {
                await mediator.Send(new RemoveOcppAuthorizedTagCommand(id), ct);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Remove OCPP authorized tag")
            .WithSummary("Admin or station owner/manager: remove a card (re-adding the same tag re-enables it)")
            .WithOpenApi();

        return app;
    }
}

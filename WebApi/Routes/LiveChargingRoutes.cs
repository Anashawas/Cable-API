using Application.Ocpp.Commands.DriverSessions;
using Application.Ocpp.LiveCharging;
using Application.Pricing;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

/// <summary>
/// Sprint 5 — live charging session, part A (Docs/Sprint5-LiveCharging-A.md): monthly summary, a station's own tariff, and the live-charging switches. The start / current / history
/// endpoints stay in OcppRoutes.
/// </summary>
public static class LiveChargingRoutes
{
    public record StationTariffRequest(List<TouWindowDto> Windows, string? Note);
    public record LiveChargingSettingsRequest(int? MaxDistanceMeters, bool? RequireLocation);

    public static IEndpointRouteBuilder MapLiveChargingRoutes(this IEndpointRouteBuilder app)
    {
        // Driver (Cable app)
        app.MapGroup("/api/users/me/ocpp-sessions").WithTags("Charging Points").MapDriverSummaryRoutes();
        app.MapGroup("/api/charging-points").WithTags("Charging Points").MapPublicTariffRoutes();

        // Station owner / managers (partner app): read-only
        app.MapGroup("/api/provider/charging-points").WithTags("Provider - Charging Points").MapProviderTariffRoutes();

        // Admin
        app.MapGroup("/api/admin/ocpp").WithTags("OCPP").MapAdminLiveChargingRoutes();

        return app;
    }

    private static RouteGroupBuilder MapDriverSummaryRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/summary", async (IMediator mediator, [FromQuery] string? month, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetMyOcppSessionsSummaryRequest(month), ct)))
            .Produces<MySessionsSummaryDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Get my charging month summary")
            .WithSummary("Driver: one month of charging (month=YYYY-MM, Jordan calendar; default the current month) — sessions, energyKwh, costJod, pointsAwarded. Same sessions as the history list, by start time.")
            .WithOpenApi();

        return app;
    }

    private static RouteGroupBuilder MapPublicTariffRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/{id:int}/tariff", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetStationTariffRequest(id), ct)))
            .Produces<StationTariffDto>()
            .ProducesNotFound().ProducesInternalServerError()
            .WithName("Get station tariff (driver app)")
            .WithSummary("Public: the time-of-use tariff that prices this station's sessions — the station's own (source station) or the national one (source national) — and the window in force now with its end and the next window, for \"now: off-peak · 183 fils/kWh\".")
            .WithOpenApi();

        return app;
    }

    private static RouteGroupBuilder MapProviderTariffRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/{chargingPointId:int}/tariff", async (IMediator mediator, [FromRoute] int chargingPointId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetProviderStationTariffRequest(chargingPointId), ct)))
            .Produces<StationTariffDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Provider: get station tariff")
            .WithSummary("Owner / manager (ConnectView): the tariff that prices the station's sessions. Read-only — Cable sets a station's own tariff.")
            .WithOpenApi();

        return app;
    }

    private static RouteGroupBuilder MapAdminLiveChargingRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/stations/{chargingPointId:int}/tariff", async (IMediator mediator, [FromRoute] int chargingPointId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetAdminStationTariffRequest(chargingPointId), ct)))
            .Produces<StationTariffDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Get station tariff")
            .WithSummary("Admin: the tariff that prices this station's sessions (hasOwnTariff, source station | national) and the window now.")
            .WithOpenApi();

        app.MapPut("/stations/{chargingPointId:int}/tariff", async (IMediator mediator, [FromRoute] int chargingPointId, StationTariffRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new SetStationTariffCommand(chargingPointId, body.Windows ?? [], body.Note), ct)))
            .Produces<StationTariffDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Set station tariff")
            .WithSummary("Admin: give the station its own time-of-use tariff — same windows as PUT /api/admin/pricing/tou (24 h exactly, unique keys, fils/kWh). Overrides the national tariff for this station's sessions from now on: live cost, the final price, JOD limits. A new version each time.")
            .WithOpenApi();

        app.MapDelete("/stations/{chargingPointId:int}/tariff", async (IMediator mediator, [FromRoute] int chargingPointId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new RemoveStationTariffCommand(chargingPointId), ct)))
            .Produces<StationTariffDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Remove station tariff")
            .WithSummary("Admin: drop the station's own tariff; its sessions are priced with the national tariff again. Returns the tariff now in force.")
            .WithOpenApi();

        app.MapGet("/live-charging-settings", async (IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetLiveChargingSettingsRequest(), ct)))
            .Produces<LiveChargingSettingsDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Get live charging settings")
            .WithSummary("Admin: app-start proximity (maxDistanceMeters, requireLocation). Automatic points are not used and cannot be switched on here.")
            .WithOpenApi();

        app.MapPut("/live-charging-settings", async (IMediator mediator, LiveChargingSettingsRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new SetLiveChargingSettingsCommand(body.MaxDistanceMeters, body.RequireLocation, null), ct)))
            .Produces<LiveChargingSettingsDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Set live charging settings")
            .WithSummary("Admin: change any of them (null = unchanged). Switching automatic points on only counts sessions that end after the switch.")
            .WithOpenApi();

        return app;
    }

}

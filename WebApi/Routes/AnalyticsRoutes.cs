using Application.Analytics.Commands.TrackEvent;
using Application.Analytics.Queries.GetEntityAnalytics;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class AnalyticsRoutes
{
    public static IEndpointRouteBuilder MapAnalyticsRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics")
            .WithTags("Analytics");

        group.MapTrackingRoutes();
        group.MapDashboardRoutes();

        return app;
    }

    private static RouteGroupBuilder MapTrackingRoutes(this RouteGroupBuilder app)
    {
        // Public front door of the analytics engine. Auth is optional — a logged-in
        // caller's UserId is captured automatically; anonymous callers may pass an
        // AnonymousId (device/session id) for unique-visitor tracking.
        app.MapPost("/track", async (IMediator mediator, TrackEventCommand request, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(request, cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .ProducesValidationProblem()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Track Analytics Event")
            .WithSummary("Records an analytics event for a provider or banner.")
            .WithDescription(
                "EntityType: ChargingPoint | ServiceProvider | Banner. " +
                "EventType: 1 FullView, 2 HalfView, 3 CallButtonClick, 4 MapClick, 20 BannerView, 21 BannerClick.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        return app;
    }

    private static RouteGroupBuilder MapDashboardRoutes(this RouteGroupBuilder app)
    {
        // Per-event totals, unique users and a daily time-series for one entity.
        // Provider analytics are owner-only; banners require an authenticated caller.
        app.MapGet("/{entityType}/{entityId:int}/summary", async (
                    [FromRoute] string entityType,
                    [FromRoute] int entityId,
                    [FromQuery] DateTime? from,
                    [FromQuery] DateTime? to,
                    IMediator mediator,
                    CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(
                    new GetEntityAnalyticsRequest(entityType, entityId, from, to), cancellationToken)))
            .RequireAuthorization()
            .Produces<EntityAnalyticsDto>()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Get Entity Analytics Summary")
            .WithSummary("Totals, unique users and daily time-series for a provider or banner.")
            .WithDescription(
                "entityType: ChargingPoint | ServiceProvider | Banner. " +
                "Optional from/to (UTC) default to the last 30 days. Owner-only for providers.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "ChargingPoint | ServiceProvider | Banner";
                op.Parameters[1].Required = true;
                op.Parameters[1].Description = "The id of the entity";
                return op;
            });

        return app;
    }
}

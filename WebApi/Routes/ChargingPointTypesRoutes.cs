using Application.ChargingPointTypes.Queries;
using MediatR;

namespace Cable.Routes;

public static class ChargingPointTypesRoutes
{
    public static IEndpointRouteBuilder MapChargingPointTypesRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/chargingPointTypes")
            .WithTags("Charging Point Types")
            .MapRoutes();
        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/GetAllChargingPointTypes", async (IMediator mediator, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new GetAllChargingPointTypesRequest(), cancellation)))
            .Produces<List<GetAllChargingPointTypesDto>>()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .RequireAuthorization()
            .ProducesInternalServerError()
            .WithName("Get all charging point types")
            .WithSummary(" Get all charging point types of the application")
            .WithOpenApi();

        return app;
    }
}
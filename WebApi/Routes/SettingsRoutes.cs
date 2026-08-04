using Application.Settings.Commands.UpdateNearbyRadius;
using Application.Settings.Queries.GetNearbyRadius;
using MediatR;

namespace Cable.Routes;

public static class SettingsRoutes
{
    public static IEndpointRouteBuilder MapSettingsRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/settings")
            .WithTags("Settings")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/nearby-radius", async (IMediator mediator, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new GetNearbyRadiusRequest(), cancellation)))
            .Produces<NearbyRadiusDto>()
            .ProducesInternalServerError()
            .WithName("Get nearby radius")
            .WithSummary("The global max-distance cap (km) used by nearby banners/stations serving")
            .WithOpenApi();

        app.MapPut("/nearby-radius",
                async (IMediator mediator, UpdateNearbyRadiusCommand request, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(request, cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Update nearby radius")
            .WithSummary("Admin: change the global nearby-radius cap (km)")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        return app;
    }
}

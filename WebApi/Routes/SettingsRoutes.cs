using Application.Settings.Commands.UpdateNearbyRadius;
using Application.Settings.Commands.UpdateWelcomeBonus;
using Application.Settings.Queries.GetNearbyRadius;
using Application.Settings.Queries.GetWelcomeBonus;
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

        app.MapGet("/welcome-bonus", async (IMediator mediator, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new GetWelcomeBonusRequest(), cancellation)))
            .Produces<WelcomeBonusDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get welcome bonus")
            .WithSummary("Admin: the multiplier applied to a customer's first ever charge")
            .WithDescription("isDefault = true means no override has been set and the shipped default (2) is in force; the bonus still applies. isEnabled = false means the multiplier is 1 and the bonus is off.")
            .WithOpenApi();

        app.MapPut("/welcome-bonus",
                async (IMediator mediator, UpdateWelcomeBonusCommand request, CancellationToken cancellationToken) =>
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
            .WithName("Update welcome bonus")
            .WithSummary("Admin: change or switch off the first-charge welcome bonus")
            .WithDescription("Multiplier applied to a customer's first ever charge, at any provider. Send 1 to switch the bonus OFF - there is no separate enabled flag. Send 2 for double points, 3 for triple. Max 10. Takes effect immediately, no deploy. Does not stack with a provider campaign: whichever multiplier is higher wins.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        return app;
    }
}

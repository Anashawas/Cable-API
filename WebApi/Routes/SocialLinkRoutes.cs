using Application.SocialLinks.Commands.DeleteSocialLink;
using Application.SocialLinks.Commands.SetSocialLinks;
using Application.SocialLinks.Queries.GetSocialLinksByProvider;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class SocialLinkRoutes
{
    public static IEndpointRouteBuilder MapSocialLinkRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/socialLinks")
            .WithTags("Social Links")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        // Get social links for a specific ServiceProvider or ChargingPoint.
        app.MapGet("/", async (
                IMediator mediator,
                [FromQuery] string providerType,
                [FromQuery] int    providerId,
                CancellationToken  cancellationToken) =>
                Results.Ok(await mediator.Send(
                    new GetSocialLinksByProviderRequest(providerType, providerId),
                    cancellationToken)))
            .Produces<List<SocialLinkDto>>()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Get Social Links By Provider")
            .WithSummary("List social links for a ServiceProvider or ChargingPoint")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Description = "ProviderType: 'ServiceProvider' or 'ChargingPoint'";
                op.Parameters[1].Description = "Id of the ServiceProvider or ChargingPoint";
                return op;
            });

        // Replace the full set of social links for a provider (admin).
        app.MapPost("/", async (
                IMediator mediator,
                SetSocialLinksCommand request,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<int[]>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Set Social Links")
            .WithSummary("Admin: replace the full set of social links for a provider")
            .WithDescription("Soft-deletes the current links for (ProviderType, ProviderId) and inserts the new list atomically. Multiple links per platform are allowed.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "Returns the new SocialLink Ids in order";
                return op;
            });

        // Soft-delete a single link (admin).
        app.MapDelete("/{id:int}", async (
                IMediator mediator,
                [FromRoute] int id,
                CancellationToken cancellationToken) =>
            {
                await mediator.Send(new DeleteSocialLinkCommand(id), cancellationToken);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete Social Link")
            .WithSummary("Admin: soft-delete a single social link");

        return app;
    }
}

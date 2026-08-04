using Application.ChargerBrands.Commands.AddChargerBrand;
using Application.ChargerBrands.Commands.DeleteChargerBrand;
using Application.ChargerBrands.Commands.UpdateChargerBrand;
using Application.ChargerBrands.Queries.GetAllChargerBrands;
using Cable.Requests.ChargerBrands;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class ChargerBrandRoutes
{
    public static IEndpointRouteBuilder MapChargerBrandRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/charger-brands")
            .WithTags("Charger Brands")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/GetAllChargerBrands", async (IMediator mediator, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new GetAllChargerBrandsRequest(), cancellation)))
            .Produces<List<GetAllChargerBrandsDto>>()
            .ProducesInternalServerError()
            .WithName("Get all charger brands")
            .WithSummary("Get all charger brands (lookup for charging point brand)")
            .WithOpenApi();

        app.MapPost("/AddChargerBrand",
                async (IMediator mediator, AddChargerBrandCommand request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Add charger brand")
            .WithSummary("Add a new charger brand")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the charger brand";
                return op;
            });

        app.MapPut("/UpdateChargerBrand/{id:int}",
                async ([FromRoute] int id, IMediator mediator, UpdateChargerBrandRequest request,
                        CancellationToken cancellationToken) =>
                    await mediator.Send(new UpdateChargerBrandCommand(id, request.Name), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Update charger brand")
            .WithSummary("Update a charger brand (also syncs the brand name on charging points)")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the charger brand";
                op.RequestBody.Required = true;
                return op;
            });

        app.MapDelete("/DeleteChargerBrand/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                await mediator.Send(new DeleteChargerBrandCommand(id), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Delete charger brand")
            .WithSummary("Delete a charger brand (blocked while any charging point uses it)")
            .WithOpenApi();

        return app;
    }
}

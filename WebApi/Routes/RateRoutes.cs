using Application.Rates.Commands.AddRateCommand;
using Application.Rates.Commands.UpdateRate;
using Application.Rates.Queries.GetChargingPointRateById;
using Cable.Requests.Rates;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Client;

namespace Cable.Routes;

public static class RateRoutes
{
    public static IEndpointRouteBuilder MapRateRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/rate")
            .WithTags("Rates")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/GetChargingPointRatesById/{id}", async (IMediator mediator,[FromRoute]int id, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new GetChargingPointRateByIdRequest(id), cancellation)))
            .Produces<double>()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .RequireAuthorization()
            .ProducesInternalServerError()
            .WithName("Get all rates")
            .WithSummary(" get")
            .WithOpenApi();

        app.MapPost("/AddRate", async (IMediator mediator, AddRateCommand request, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(request, cancellation)))
            .Produces<int>()
            .WithName("Add rate")
            .WithSummary("Add a new rate")
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .RequireAuthorization()
            .ProducesInternalServerError()
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the rate";
                return op;
            });

        app.MapPatch("/UpdateRate/{id:int}", async (IMediator mediator, [FromRoute] int id, UpdateRateRequest request,
                    CancellationToken cancellation) =>
                await mediator.Send(new UpdateRateCommand(id, request.ChargingPointRate), cancellation))
            .Produces<int>()
            .WithName("Update rate")
            .WithSummary("Update a rate")
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .RequireAuthorization()
            .ProducesInternalServerError()
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the rate";
                return op;
            });

        return app;
    }
}
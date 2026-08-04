using Application.Rates.Commands.AddRateCommand;
using Application.Rates.Commands.DeleteRate;
using Application.Rates.Commands.UpdateRate;
using Application.Rates.Queries.GetChargingPointRateById;
using Application.Rates.Queries.GetChargingPointReviews;
using Application.Rates.Queries.GetMyReview;
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
            .ProducesInternalServerError()
            .WithName("Get all rates")
            .WithSummary("Get all rates")
            .WithOpenApi();

        app.MapGet("/GetChargingPointReviews/{id:int}", async (IMediator mediator, [FromRoute] int id, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new GetChargingPointReviewsRequest(id), cancellation)))
            .Produces<GetChargingPointReviewsDto>()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get charging point reviews")
            .WithSummary("Get all ratings with review comments for a charging point, newest first")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the charging point";
                return op;
            });

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
                await mediator.Send(new UpdateRateCommand(id, request.ChargingPointRate, request.Comment), cancellation))
            .Produces<int>()
            .WithName("Update rate")
            .WithSummary("Update a rate (author or admin only)")
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

        // F2: same handler under PUT — the client contract asks for PUT UpdateRate/{reviewId}.
        app.MapPut("/UpdateRate/{id:int}", async (IMediator mediator, [FromRoute] int id, UpdateRateRequest request,
                    CancellationToken cancellation) =>
                {
                    await mediator.Send(new UpdateRateCommand(id, request.ChargingPointRate, request.Comment), cancellation);
                    return Results.Ok();
                })
            .Produces(200)
            .WithName("Update rate (PUT)")
            .WithSummary("F2: edit your OWN review (rating and/or comment). Author or admin only")
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .RequireAuthorization()
            .ProducesInternalServerError()
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        app.MapGet("/GetMyReview/{chargingPointId:int}",
                async (IMediator mediator, [FromRoute] int chargingPointId, CancellationToken cancellation) =>
                    Results.Ok(await mediator.Send(new GetMyReviewRequest(chargingPointId), cancellation)))
            .Produces<MyReviewDto?>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get my review")
            .WithSummary("F2: the caller's own review of this station, or null if they haven't rated it")
            .WithOpenApi();

        app.MapDelete("/DeleteRate/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellation) =>
                {
                    await mediator.Send(new DeleteRateCommand(id), cancellation);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete rate")
            .WithSummary("F2: remove your OWN review (author or admin). The station average self-corrects")
            .WithOpenApi();

        return app;
    }
}
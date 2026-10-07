using Application.Rates.Commands.AddRateCommand;
using Application.Rates.Commands.DeleteRate;
using Application.Rates.Commands.RateUser;
using Application.Rates.Commands.UpdateRate;
using Application.Rates.Queries.GetChargingPointRateById;
using Application.Rates.Queries.GetChargingPointReviews;
using Application.Rates.Queries.GetMyReview;
using Application.Rates.Queries.GetMyUserRating;
using Application.Rates.Queries.GetUserRating;
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

        // ==========================================
        // Driver ratings — the provider rates the USER (reverse direction)
        // ==========================================

        app.MapPost("/RateUser", async (IMediator mediator, RateUserCommand request, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(request, cancellation)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Rate user")
            .WithSummary("Rate the driver served by a completed partner transaction (owner, worker, or admin)")
            .WithDescription("Called after the driver's code is scanned and the transaction completes — the provider scores the customer 1-5 with an optional comment. The rating is anchored to the transaction, so a provider can only rate a driver it actually served, and only once per visit. Returns 403 if the caller does not own or work for that provider, and a validation error if the transaction is not Completed or has already been rated.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the created driver rating";
                return op;
            });

        app.MapGet("/GetMyUserRating", async (IMediator mediator, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new GetMyUserRatingRequest(), cancellation)))
            .Produces<UserRatingSummaryDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get my user rating")
            .WithSummary("The caller's own standing as a driver: average, count, and the ratings providers left")
            .WithDescription("For the consumer app. A driver nobody has rated yet gets an empty summary (averageRating null, ratingsCount 0) rather than a 404.")
            .WithOpenApi();

        app.MapGet("/GetUserRating/{userId:int}",
                async (IMediator mediator, [FromRoute] int userId, CancellationToken cancellation) =>
                    Results.Ok(await mediator.Send(new GetUserRatingRequest(userId), cancellation)))
            .Produces<UserRatingSummaryDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get user rating")
            .WithSummary("A driver's rating, for a provider that has served them (or an admin)")
            .WithDescription("Scoped on purpose: a provider may only read the rating of a driver it has a completed transaction with, so this cannot be used to enumerate every user's rating. Admins are unrestricted, and a caller reading their own id always succeeds.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the driver";
                return op;
            });

        return app;
    }
}
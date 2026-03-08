using Application.Favorites.Commands.AddToFavorites;
using Application.Favorites.Commands.RemoveFromFavorites;
using Application.Favorites.Queries.CheckIsFavorite;
using Application.Favorites.Queries.GetUserFavorites;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class FavoritesRoutes
{
    public static IEndpointRouteBuilder MapFavoritesRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/favorites")
            .WithTags("Favorites")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        // Get user's favorites
        app.MapGet("/", async (IMediator mediator, CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new GetUserFavoritesRequest(), cancellationToken)))
            .Produces<List<GetUserFavoritesDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get User Favorites")
            .WithSummary("Get all favorite charging points for the current user")
            .WithOpenApi();

        // Check if charging point is favorited
        app.MapGet("/check/{chargingPointId:int}",
                async (IMediator mediator, [FromRoute] int chargingPointId, CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new CheckIsFavoriteRequest(chargingPointId), cancellationToken)))
            .Produces<CheckIsFavoriteDto>()
            .ProducesInternalServerError()
            .WithName("Check Is Favorite")
            .WithSummary("Check if a charging point is in user's favorites")
            .WithOpenApi();

        // Add to favorites
        app.MapPost("/{chargingPointId:int}",
                async (IMediator mediator, [FromRoute] int chargingPointId, CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new AddToFavoritesCommand(chargingPointId), cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Add to Favorites")
            .WithSummary("Add a charging point to user's favorites")
            .WithOpenApi(op =>
            {
                op.Responses["200"].Description = "The id of the favorite record";
                return op;
            });

        // Remove from favorites
        app.MapDelete("/{chargingPointId:int}",
                async (IMediator mediator, [FromRoute] int chargingPointId, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new DeleteFromFavoritesCommand(chargingPointId), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Remove from Favorites")
            .WithSummary("Remove a charging point from user's favorites")
            .WithOpenApi();

        return app;
    }
}

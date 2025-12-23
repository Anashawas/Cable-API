using Application.Common.Interfaces;
using Application.SharedLinks.Commands.CreateSharedLink;
using Application.SharedLinks.Commands.ValidateSharedLink;
using Application.SharedLinks.Queries;
using Application.SharedLinks.Queries.GetSharedLinkByToken;
using Application.Common.Interfaces.Repositories;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class SharedLinksRoutes
{
    public static IEndpointRouteBuilder MapSharedLinksRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/shared-links")
            .WithTags("Shared Links")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapPost("/create",
                async (CreateSharedLinkCommand request, IMediator mediator, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<string>()
            .ProducesInternalServerError()
            .WithName("Create shared link")
            .WithSummary("Create a new shared link")
            .WithOpenApi();

        app.MapGet("/validate/{token}",
                async ([FromRoute] string token, [FromQuery] string? deviceInfo, [FromQuery] string? ipAddress,
                        IMediator mediator, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new ValidateSharedLinkCommand(token, deviceInfo, ipAddress), cancellationToken)))
            .Produces<ValidateSharedLinkResult>()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Validate shared link")
            .WithSummary("Validate and use a shared link")
            .WithOpenApi();

        app.MapGet("/get/{token}",
                async ([FromRoute] string token, IMediator mediator, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetSharedLinkByTokenRequest(token), cancellationToken)))
            .Produces<SharedLinkDto>()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get shared link by token")
            .WithSummary("Get shared link details by token")
            .WithOpenApi();

        app.MapGet("/my-links",
                async (ISharedLinkRepository repository, ICurrentUserService currentUserService, CancellationToken cancellationToken) =>
                {
                    if (currentUserService.UserId == null)
                        return Results.Unauthorized();
                    
                    var links = await repository.GetSharedLinksByUserIdAsync(currentUserService.UserId.Value, cancellationToken);
                    return Results.Ok(links);
                })
            .Produces<List<SharedLinkDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get my shared links")
            .WithSummary("Get current user's shared links")
            .WithOpenApi();

        app.MapGet("/usage/{linkId:int}",
                async ([FromRoute] int linkId, ISharedLinkRepository repository, CancellationToken cancellationToken) =>
                    Results.Ok(await repository.GetSharedLinkUsageAsync(linkId, cancellationToken)))
            .Produces<List<SharedLinkUsageDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get shared link usage")
            .WithSummary("Get usage statistics for a shared link")
            .WithOpenApi();

        app.MapGet("/types",
                async (ISharedLinkRepository repository, CancellationToken cancellationToken) =>
                    Results.Ok(await repository.GetAllSharedLinkTypesAsync(cancellationToken)))
            .Produces<List<SharedLinkTypeDto>>()
            .ProducesInternalServerError()
            .WithName("Get shared link types")
            .WithSummary("Get all available shared link types")
            .WithOpenApi();

        app.MapPost("/cleanup-expired",
                async (ISharedLinkRepository repository, CancellationToken cancellationToken) =>
                {
                    await repository.CleanupExpiredLinksAsync(cancellationToken);
                    return Results.Ok("Cleanup completed");
                })
            .Produces<string>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Cleanup expired links")
            .WithSummary("Clean up expired shared links (admin only)")
            .WithOpenApi();

        return app;
    }
}
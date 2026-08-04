using Application.SocialMediaPlatforms.Commands.AddSocialMediaPlatform;
using Application.SocialMediaPlatforms.Commands.DeleteSocialMediaPlatform;
using Application.SocialMediaPlatforms.Commands.UpdateSocialMediaPlatform;
using Application.SocialMediaPlatforms.Queries.GetAllSocialMediaPlatforms;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class SocialMediaPlatformRoutes
{
    public static IEndpointRouteBuilder MapSocialMediaPlatformRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/socialMediaPlatforms")
            .WithTags("Social Media Platforms")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        // List all platforms (public — needed so the mobile app can render the catalog).
        app.MapGet("/", async (
                IMediator mediator,
                [FromQuery] bool activeOnly = true,
                CancellationToken cancellationToken = default) =>
                Results.Ok(await mediator.Send(
                    new GetAllSocialMediaPlatformsRequest(activeOnly),
                    cancellationToken)))
            .Produces<List<SocialMediaPlatformDto>>()
            .ProducesInternalServerError()
            .WithName("Get All Social Media Platforms")
            .WithSummary("List social-media platforms with icon URLs")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Description = "Return only IsActive platforms (default true)";
                return op;
            });

        // Add platform with optional icon (admin).
        app.MapPost("/", async (
                IMediator mediator,
                [FromForm] string name,
                [FromForm] string? nameAr,
                [FromForm] int displayOrder,
                IFormFile? icon,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(
                    new AddSocialMediaPlatformCommand(name, nameAr, displayOrder, icon),
                    cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .DisableAntiforgery()
            .WithName("Add Social Media Platform")
            .WithSummary("Admin: add a new social-media platform with optional icon")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "Returns the new platform Id";
                return op;
            });

        // Update platform (admin).
        app.MapPut("/{id:int}", async (
                IMediator mediator,
                [FromRoute] int id,
                [FromForm] string name,
                [FromForm] string? nameAr,
                [FromForm] int displayOrder,
                [FromForm] bool isActive,
                IFormFile? icon,
                CancellationToken cancellationToken) =>
            {
                await mediator.Send(
                    new UpdateSocialMediaPlatformCommand(id, name, nameAr, displayOrder, isActive, icon),
                    cancellationToken);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .DisableAntiforgery()
            .WithName("Update Social Media Platform")
            .WithSummary("Admin: update an existing platform; optionally replace its icon");

        // Soft-delete platform (admin).
        app.MapDelete("/{id:int}", async (
                IMediator mediator,
                [FromRoute] int id,
                CancellationToken cancellationToken) =>
            {
                await mediator.Send(new DeleteSocialMediaPlatformCommand(id), cancellationToken);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete Social Media Platform")
            .WithSummary("Admin: soft-delete a social-media platform");

        return app;
    }
}

using Application.Announcements.Commands.AnnouncementAdminExtras;
using Application.Announcements.Commands.CreateAnnouncement;
using Application.Announcements.Commands.UpdateAnnouncement;
using Application.Announcements.Queries.GetAllAnnouncements;
using Application.Announcements.Queries.GetHomeAnnouncement;
using Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class HomeRoutes
{
    public static IEndpointRouteBuilder MapHomeRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/home")
            .WithTags("Home")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        // Anonymous on purpose: guests use the home screen too. With a token,
        // targeting + frequency caps are enforced per user.
        app.MapGet("/announcement",
                async (IMediator mediator, [FromQuery] double? lat, [FromQuery] double? lng,
                        [FromQuery] string? city, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetHomeAnnouncementRequest(lat, lng, city), cancellationToken)))
            .Produces<HomeAnnouncementDto?>()
            .ProducesInternalServerError()
            .WithName("Get home announcement")
            .WithSummary("The BE-decided home takeover: at most ONE targeted welcome message, or null. Serving to a logged-in user counts as a show for the frequency caps")
            .WithDescription("Targeting: radius beats city beats national. Caps (maxPerDay/cooldownHours/maxLifetime/stopOnDismiss) are enforced server-side for logged-in users; guests are uncapped server-side.")
            .WithOpenApi();

        app.MapPost("/admin/announcements",
                async (IMediator mediator, CreateAnnouncementCommand request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Create announcement")
            .WithSummary("Admin: create a welcome-message takeover (bilingual content, targeting, schedule, frequency caps)")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPut("/admin/announcements/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdateAnnouncementCommand request,
                        CancellationToken cancellationToken) =>
                {
                    await mediator.Send(request with { Id = id }, cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Update announcement")
            .WithSummary("Admin: update a welcome message (content, targeting, caps, isActive)")
            .WithOpenApi();

        app.MapGet("/admin/announcements",
                async (IMediator mediator, [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetAllAnnouncementsRequest(page, pageSize), cancellationToken)))
            .Produces<PagedResult<AnnouncementAdminDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get all announcements")
            .WithSummary("Admin: all welcome messages (active and not)")
            .WithOpenApi();

        app.MapDelete("/admin/announcements/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new DeleteAnnouncementCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete announcement")
            .WithSummary("Admin: HARD delete a welcome message (per-user states removed; analytics history kept)")
            .WithOpenApi();

        app.MapPost("/admin/announcements/{id:int}/image",
                async (IMediator mediator, [FromRoute] int id, [FromForm] IFormFile files,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new UploadAnnouncementImageCommand(id, files), cancellationToken)))
            .Produces<AnnouncementImageResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Upload announcement image")
            .WithSummary("Admin: upload the welcome message's image (multipart, form key 'files', one jpg/png/webp ≤5MB) — sets imageUrl and returns it")
            .WithOpenApi()
            .DisableAntiforgery();

        app.MapGet("/admin/announcements/{id:int}/stats",
                async (IMediator mediator, [FromRoute] int id, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetAnnouncementStatsRequest(id, from, to), cancellationToken)))
            .Produces<AnnouncementStatsDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get announcement stats")
            .WithSummary("Admin: per-welcome-message engagement — impressions/clicks/dismisses/conversions/ctr/uniqueUsers (guests counted via anonymousId). Optional from/to")
            .WithOpenApi();

        return app;
    }
}

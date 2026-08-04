using Application.Common.Models;
using Application.Analytics;
using Application.Analytics.Commands.TrackEvent;
using Application.Banners.Commands.AddBanner;
using Application.Banners.Commands.DeleteBanner;
using Application.Banners.Commands.UpdateBanner;
using Application.Banners.Commands.SetBannerTargeting;
using Application.Banners.Queries.GetAllBanners;
using Application.Banners.Queries.GetNearbyBanners;
using Cable.Core.Emuns;
using Cable.Requests.Banners;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class BannerRoutes
{
    public static IEndpointRouteBuilder MapBannerRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/banners")
            .WithTags("Banners")
            .MapGroup();
        
        return app;
    }


    private static RouteGroupBuilder MapGroup(this RouteGroupBuilder app)
    {
        app.MapGet("/GetAllBanners", async (IMediator mediator, [FromQuery] int? page, [FromQuery] int? pageSize,
                    CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetAllBannersRequest(page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<GetAllBannersDto>>()
            .Produces<PagedResult<GetAllBannersDto>>()
            .ProducesInternalServerError()
            .WithName("Get All Banners")
            .WithSummary("Get all banners")
            .WithOpenApi();

        app.MapGet("/GetNearbyBanners",
                async (IMediator mediator, [FromQuery] double? lat, [FromQuery] double? lng,
                        [FromQuery] string? city, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetNearbyBannersRequest(lat, lng, city), cancellationToken)))
            .Produces<List<NearbyBannerDto>>()
            .ProducesInternalServerError()
            .WithName("Get Nearby Banners")
            .WithSummary("Location-targeted, scheduled, ranked banners for the home carousel (radius-hit → same city → national; render the returned order as-is)")
            .WithDescription("lat/lng/city optional but recommended. Only banners inside their active window are returned; expired ads never serve. Each radius-targeted item carries distanceKm.")
            .WithOpenApi();

        app.MapPut("/SetBannerTargeting/{id:int}",
                async (IMediator mediator, [FromRoute] int id, SetBannerTargetingCommand request,
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
            .WithName("Set Banner Targeting")
            .WithSummary("Admin: set a banner's ad-serving fields — targetType (national/city/radius), city or center+radius, linked entity, priority, campaign. Legacy Add/UpdateBanner stay unchanged")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        app.MapPost("/AddBanner",
                async (IMediator mediator, AddBannerCommand request, CancellationToken cancellationToken) => Results.Ok(
                    await mediator.Send(request, cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Add Banner")
            .WithSummary("Add a new banner")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the banner";
                return op;
            });

        app.MapPut("/UpdateBanner/{id:int}",
                async (IMediator mediator, UpdateBannerRequest request, CancellationToken cancellationToken, int id) =>
                    await mediator.Send(
                        new UpdateBannerCommand(id, request.Name, request.Phone, request.Email,request.ActionType,request.ActionUrl, request.StartDate,
                            request.EndDate), cancellationToken))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Update Banner")
            .WithSummary("Update a banner")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the banner";
                return op;
            });

        app.MapDelete("/DeleteBanner/{id:int}",
                async (IMediator mediator, int id, CancellationToken cancellationToken) =>
                    await mediator.Send(new DeleteBannerCommand(id), cancellationToken))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Delete Banner")
            .WithSummary("Delete a banner")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the banner";
                return op;
            });

        // Analytics — convenience wrappers over the analytics engine.
        app.MapPost("/{id:int}/view", async (IMediator mediator, int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(
                        new TrackEventCommand(AnalyticsCatalog.Banner, id, AnalyticsEventType.BannerView),
                        cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Track Banner View")
            .WithSummary("Records a banner view")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the banner";
                return op;
            });

        app.MapPost("/{id:int}/click", async (IMediator mediator, int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(
                        new TrackEventCommand(AnalyticsCatalog.Banner, id, AnalyticsEventType.BannerClick),
                        cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Track Banner Click")
            .WithSummary("Records a banner click")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the banner";
                return op;
            });

        return app;
    }
}
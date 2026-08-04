using Application.Ads.Commands.ManageAdvertisers;
using Application.Ads.Commands.ManageCampaigns;
using Application.Ads.Queries.GetCampaignStats;
using Application.Ads.Queries.GetPremiumStationStats;
using Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class AdsRoutes
{
    public static IEndpointRouteBuilder MapAdsRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/ads")
            .WithTags("Ads")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapPost("/admin/advertisers",
                async (IMediator mediator, CreateAdvertiserCommand request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Create advertiser")
            .WithSummary("Admin: create an advertiser (ad customer)")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPut("/admin/advertisers/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdateAdvertiserCommand request,
                        CancellationToken cancellationToken) =>
                {
                    await mediator.Send(request with { Id = id }, cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Update advertiser")
            .WithSummary("Admin: update an advertiser")
            .WithOpenApi();

        app.MapGet("/admin/advertisers",
                async (IMediator mediator, [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetAllAdvertisersRequest(page, pageSize), cancellationToken)))
            .Produces<PagedResult<AdvertiserDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Get all advertisers")
            .WithSummary("Admin: all advertisers with campaign counts")
            .WithOpenApi();

        app.MapPost("/admin/campaigns",
                async (IMediator mediator, CreateCampaignCommand request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Create campaign")
            .WithSummary("Admin: create a sold campaign (type: banner | premium | welcome)")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPut("/admin/campaigns/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdateCampaignCommand request,
                        CancellationToken cancellationToken) =>
                {
                    await mediator.Send(request with { Id = id }, cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Update campaign")
            .WithSummary("Admin: update a campaign (window, price, status 0 Draft / 1 Active / 2 Paused / 3 Ended)")
            .WithOpenApi();

        app.MapGet("/admin/campaigns",
                async (IMediator mediator, [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetAllCampaignsRequest(page, pageSize), cancellationToken)))
            .Produces<PagedResult<CampaignDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Get all campaigns")
            .WithSummary("Admin: all campaigns with their advertiser")
            .WithOpenApi();

        app.MapGet("/campaigns/{id:int}/stats",
                async (IMediator mediator, [FromRoute] int id, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetCampaignStatsRequest(id, from, to), cancellationToken)))
            .Produces<CampaignStatsDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Get campaign stats")
            .WithSummary("Admin: impressions/clicks/ctr + byDay + byCity for a campaign's linked banners and announcements")
            .WithOpenApi();

        app.MapGet("/premium/{stationId:int}/stats",
                async (IMediator mediator, [FromRoute] int stationId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetPremiumStationStatsRequest(stationId, from, to), cancellationToken)))
            .Produces<PremiumStationStatsDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Get premium station stats")
            .WithSummary("Premium home-card impressions/clicks/ctr for one station (owner, worker, or admin)")
            .WithOpenApi();

        return app;
    }
}

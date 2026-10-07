using Application.Admin.Queries.GetAttentionSummary;
using MediatR;
using Application.Admin.Queries.GetPartnerAdoption;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class AdminRoutes
{
    public static IEndpointRouteBuilder MapAdminRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/admin")
            .WithTags("Admin")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/attention-summary", async (IMediator mediator, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new GetAttentionSummaryRequest(), cancellation)))
            .Produces<AttentionSummaryDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get attention summary")
            .WithSummary("Admin dashboard 'Needs Attention' counts — one number per queue (update requests, complaints, pending images/offers, settlements, premium/campaign/announcement expiries)")
            .WithOpenApi();

        app.MapGet("/partner-adoption", async (IMediator mediator, [FromQuery] int? activeWindowDays,
                    CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new GetPartnerAdoptionRequest(activeWindowDays ?? 30), cancellation)))
            .Produces<PartnerAdoptionDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get partner adoption")
            .WithSummary("Partner-app adoption funnel: every station with its owner's stage (NoOwner / DefaultOwner / NeverUsedApp / Inactive / Active) plus totals")
            .WithDescription("Admin only. isDefaultOwner is true when the owner account holds the Admin role. ownerUsesPartnerApp/Web mean the owner has ever signed into that client. ownerLastLoginAt / ownerLastSeenAt count PARTNER clients only. lastPartnerActivityAt is the newest QR, update request or offer by the owner. activeWindowDays defaults to 30.")
            .WithOpenApi();

        return app;
    }
}

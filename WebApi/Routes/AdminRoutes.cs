using Application.Admin.Queries.GetAttentionSummary;
using MediatR;

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

        return app;
    }
}

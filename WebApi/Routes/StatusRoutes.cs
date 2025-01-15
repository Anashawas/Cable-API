using Application.Statuses.Queries;
using MediatR;

namespace Cable.Routes;

public static class StatusRoutes
{
    public static IEndpointRouteBuilder MapStatusRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/status")
            .WithTags("Status")
            .MapRoutes();
        return app;
    }


    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/", async (IMediator mediator, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new GetAllStatusesRequest(), cancellation)))
            .Produces<List<GetAllStatusesDto>>()
            .WithName("Get all statuses")
            .WithSummary(" Get all statuses of the application")
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .RequireAuthorization()
            .ProducesInternalServerError()
            .WithOpenApi();
        return app;
    }
}
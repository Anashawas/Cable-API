using Application.NotificationTypes.Queries.GetAllNotificationTypes;
using Cable.WebApi.OpenAPI;
using MediatR;

namespace Cable.Routes;

public static class NotificationTypeRoutes
{
    public static IEndpointRouteBuilder MapNotificationTypeRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/notification-types")
            .WithTags("Notification Types")
            .MapRoutes();
        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/", async (IMediator mediator, CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new GetAllNotificationTypesRequest(), cancellationToken)))
            .Produces<List<NotificationTypeDto>>()
            .ProducesInternalServerError()
            .WithName("Get All Notification Types")
            .WithSummary("Get all active notification types")
            .WithOpenApi();

        return app;
    }
}

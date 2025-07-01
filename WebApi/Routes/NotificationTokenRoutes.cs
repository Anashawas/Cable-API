using Application.NotificationTokens.Commands.RefreshNotificationToken;
using Application.NotificationTokens.Commands.RegisterNotificationToken;
using MediatR;

namespace Cable.Routes;

public static class NotificationTokenRoutes
{
    public static IEndpointRouteBuilder MapNotificationTokenRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/notification-token")
            .WithTags("Notification-Token")
            .MapRoutes();
        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapPost("/RegisterNotificationToken",
                async (IMediator mediator, RegisterNotificationTokenCommand request, CancellationToken cancellation) =>
                    Results.Ok(await mediator.Send(request, cancellation)))
            .Produces<int>()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .RequireAuthorization()
            .ProducesInternalServerError()
            .WithName("Register notification token")
            .WithSummary("Register notification token for specific user")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the register token";
                return op;
            });

        app.MapPut("/RefreshNotificationToken",
                async (IMediator mediator, RefreshNotificationTokenCommand request, CancellationToken cancellation) =>
                    await mediator.Send(request, cancellation))
            .Produces(200)
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .RequireAuthorization()
            .ProducesInternalServerError()
            .WithName("Refresh notification token")
            .WithSummary("Refresh notification token for specific user")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });


        return app;
    }
}
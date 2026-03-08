using Application.NotificationTokens.Commands.RefreshNotificationToken;
using Cable.WebApi.OpenAPI;
using MediatR;

namespace Cable.Routes;

public static class NotificationTokenRoutes
{
    public static IEndpointRouteBuilder MapNotificationTokenRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/notification-token")
            .WithTags("Notification Token")
            .MapRoutes();
        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        // Upsert notification token (register or update)
        app.MapPut("/", async (
                IMediator mediator,
                RefreshNotificationTokenCommand request,
                CancellationToken cancellationToken) =>
            {
                await mediator.Send(request, cancellationToken);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Upsert Notification Token")
            .WithSummary("Register or update FCM notification token for authenticated user")
            .WithDescription("If user doesn't have a token for the specified app type, it will be created. If user already has a token for the app type, it will be updated. Each user can have one active FCM token per app type (UserApp or StationApp).")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.RequestBody.Description = "FCM token, device information, and app type (1 = UserApp, 2 = StationApp)";
                return op;
            });

        return app;
    }
}
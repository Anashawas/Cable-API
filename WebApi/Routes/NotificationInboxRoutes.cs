using Application.NotificationInbox.Commands.AddNotification;
using Application.NotificationInbox.Commands.DeleteNotification;
using Application.NotificationInbox.Commands.MarkAllAsRead;
using Application.NotificationInbox.Commands.MarkAsRead;
using Application.NotificationInbox.Commands.SendNotificationByFilter;
using Application.NotificationInbox.Queries.GetNotificationById;
using Application.NotificationInbox.Queries.GetUnreadCount;
using Application.NotificationInbox.Queries.GetUserNotifications;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class NotificationInboxRoutes
{
    public static IEndpointRouteBuilder MapNotificationInboxRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/notifications")
            .WithTags("Notification Inbox")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        // Get user's notifications with pagination
        app.MapGet("/", async (
                IMediator mediator,
                [FromQuery] int pageNumber = 1,
                [FromQuery] int pageSize = 20,
                [FromQuery] bool? isRead = null,
                CancellationToken cancellationToken = default) =>
                Results.Ok(await mediator.Send(
                    new GetUserNotificationsRequest(pageNumber, pageSize, isRead),
                    cancellationToken)))
            .Produces<GetUserNotificationsDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get User Notifications")
            .WithSummary("Get paginated list of user's notifications")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Description = "Page number (default: 1)";
                op.Parameters[1].Description = "Page size (default: 20, max: 100)";
                op.Parameters[2].Description = "Filter by read status (null = all, true = read only, false = unread only)";
                return op;
            });

        // Get unread notification count
        app.MapGet("/unread-count", async (
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(
                    new GetUnreadNotificationCountRequest(),
                    cancellationToken)))
            .Produces<UnreadCountDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get Unread Count")
            .WithSummary("Get count of unread notifications for current user")
            .WithOpenApi();

        // Get notification by ID
        app.MapGet("/{id:int}", async (
                IMediator mediator,
                [FromRoute] int id,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(
                    new GetNotificationByIdRequest(id),
                    cancellationToken)))
            .Produces<NotificationDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get Notification By Id")
            .WithSummary("Get a specific notification by ID")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Description = "Notification ID";
                return op;
            });

        // Mark notification as read
        app.MapPut("/{id:int}/mark-as-read", async (
                IMediator mediator,
                [FromRoute] int id,
                CancellationToken cancellationToken) =>
            {
                await mediator.Send(new MarkNotificationAsReadCommand(id), cancellationToken);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Mark Notification As Read")
            .WithSummary("Mark a specific notification as read")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Description = "Notification ID";
                return op;
            });

        // Mark all notifications as read
        app.MapPut("/mark-all-as-read", async (
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(
                    new MarkAllNotificationsAsReadCommand(),
                    cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Mark All As Read")
            .WithSummary("Mark all user's notifications as read")
            .WithOpenApi(op =>
            {
                op.Responses["200"].Description = "Number of notifications marked as read";
                return op;
            });

        // Delete notification (soft delete)
        app.MapDelete("/{id:int}", async (
                IMediator mediator,
                [FromRoute] int id,
                CancellationToken cancellationToken) =>
            {
                await mediator.Send(new DeleteNotificationCommand(id), cancellationToken);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete Notification")
            .WithSummary("Delete a notification (soft delete)")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Description = "Notification ID";
                return op;
            });

        // Admin-only endpoint: Send notification to multiple users
        app.MapPost("/", async (
                IMediator mediator,
                SendNotificationCommand request,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Send Notification to Users")
            .WithSummary("Send notification to specific users or all users registered in a Firebase app")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.RequestBody.Description = "Notification details with UserIds, AppType (1=UserApp/Cable, 2=StationApp/CableStation), and optional DeepLink/Data";
                op.Responses["200"].Description = "Returns the number of notifications successfully sent and saved to inbox for the specified app type";
                return op;
            });

        // Admin-only endpoint: Send notification by filters (CarType, CarModel, City)
        app.MapPost("/send-by-filter", async (
                IMediator mediator,
                SendNotificationByCategoryCommand request,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<SendNotificationByFilterResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Send Notification By Filter")
            .WithSummary("Send notification to users filtered by CarTypeId, CarModelId, or City for a specific Firebase app")
            .WithDescription("Send notifications to users matching specific filters. At least one filter (CarTypeId, CarModelId, or City) must be provided. Notifications are sent via Firebase to the specified AppType (1=UserApp/Cable, 2=StationApp/CableStation). Only users with registered tokens for the specified app type will receive notifications. Inbox records are created for successful sends.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.RequestBody.Description = "Notification details with filters (at least one required) and AppType (1=UserApp, 2=StationApp)";
                op.Responses["200"].Description = "Returns the number of targeted users and successfully sent notifications for the specified app type";
                return op;
            });

        return app;
    }
}

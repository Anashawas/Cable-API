using Application.NotificationTemplates;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class NotificationTemplateRoutes
{
    public static IEndpointRouteBuilder MapNotificationTemplateRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/notification-templates")
            .WithTags("Notification Templates")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/",
                async (IMediator mediator, [FromQuery] int? notificationTypeId, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetNotificationTemplatesRequest(notificationTypeId),
                        cancellationToken)))
            .Produces<List<NotificationTemplateDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get notification templates")
            .WithSummary("F5: body suggestions per notification type — the partner compose sheet shows them as chips. Optional ?notificationTypeId= filter")
            .WithOpenApi();

        app.MapPost("/",
                async (IMediator mediator, CreateNotificationTemplateCommand request,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Create notification template")
            .WithSummary("Admin: add a suggested body for a notification type")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPut("/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdateNotificationTemplateCommand request,
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
            .WithName("Update notification template")
            .WithSummary("Admin: edit a suggested body")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapDelete("/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new DeleteNotificationTemplateCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete notification template")
            .WithSummary("Admin: remove a suggested body")
            .WithOpenApi();

        return app;
    }
}

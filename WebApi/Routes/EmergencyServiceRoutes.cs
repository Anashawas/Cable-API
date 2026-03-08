using Application.EmergencyServices.Commands.AddEmergencyService;
using Application.EmergencyServices.Commands.DeleteEmergencyService;
using Application.EmergencyServices.Commands.UpdateEmergencyService;
using Application.EmergencyServices.Queries.GetAllEmergencyServices;
using Cable.Requests.EmergencyServices;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class EmergencyServiceRoutes
{
    public static IEndpointRouteBuilder MapEmergencyServiceRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/emergency-services")
            .WithTags("Emergency Services")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/GetAllEmergencyServices",
                async (IMediator mediator, [FromQuery] bool? isActive, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetAllEmergencyServicesRequest(isActive), cancellationToken)))
            .Produces<List<GetAllEmergencyServicesDto>>()
            .ProducesInternalServerError()
            .WithName("Get All Emergency Services")
            .WithSummary("Get all emergency services")
            .WithDescription("Returns all emergency services. Optional filter by IsActive status. Results are sorted by SortOrder and then by Title.")
            .WithOpenApi();

        app.MapPost("/AddEmergencyService",
                async (IMediator mediator, AddEmergencyServiceRequest request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new AddEmergencyServiceCommand(
                        request.Title,
                        request.Description,
                        request.ImageUrl,
                        request.SubscriptionType,
                        request.PriceDetails,
                        request.ActionUrl,
                        request.OpenFrom,
                        request.OpenTo,
                        request.PhoneNumber,
                        request.WhatsAppNumber,
                        request.IsActive,
                        request.SortOrder
                    ), cancellationToken)))
            .Produces<int>()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Add Emergency Service")
            .WithSummary("Create a new emergency service")
            .WithDescription("Creates a new emergency service entry. Returns the ID of the created service.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        app.MapPut("/UpdateEmergencyService/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdateEmergencyServiceRequest request, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new UpdateEmergencyServiceCommand(
                        id,
                        request.Title,
                        request.Description,
                        request.ImageUrl,
                        request.SubscriptionType,
                        request.PriceDetails,
                        request.ActionUrl,
                        request.OpenFrom,
                        request.OpenTo,
                        request.PhoneNumber,
                        request.WhatsAppNumber,
                        request.IsActive,
                        request.SortOrder
                    ), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Update Emergency Service")
            .WithSummary("Update an existing emergency service")
            .WithDescription("Updates all fields of an existing emergency service.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the emergency service to update";
                op.RequestBody.Required = true;
                return op;
            });

        app.MapDelete("/DeleteEmergencyService/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new DeleteEmergencyServiceCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
        
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete Emergency Service")
            .WithSummary("Delete an emergency service")
            .WithDescription("Soft-deletes an emergency service (marks as deleted).")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the emergency service to delete";
                return op;
            });

        return app;
    }
}

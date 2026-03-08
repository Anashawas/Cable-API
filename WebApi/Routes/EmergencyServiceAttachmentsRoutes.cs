using Application.Common.Models;
using Application.EmergencyServiceAttachments.Commands.AddEmergencyServiceAttachment;
using Application.EmergencyServiceAttachments.Commands.DeleteEmergencyServiceAttachment;
using Application.EmergencyServiceAttachments.Queries.GetAllEmergencyServiceAttachmentsById;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class EmergencyServiceAttachmentsRoutes
{
    public static IEndpointRouteBuilder MapEmergencyServiceAttachmentsRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/emergencyServiceAttachments")
            .WithTags("Emergency Service Attachments")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/GetAllEmergencyServiceAttachmentsById/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new GetAllEmergencyServiceAttachmentsByIdRequest(id), cancellationToken)))
            .Produces<List<UploadFile>>()
            .ProducesInternalServerError()
            .WithName("Get All Emergency Service Attachments By Id")
            .WithSummary("Get all attachments for an emergency service")
            .WithDescription("Returns all attachment files associated with the specified emergency service.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the emergency service";
                return op;
            });

        app.MapPost("/AddEmergencyServiceAttachment/{id:int}",
                async (IMediator mediator, [FromRoute] int id, IFormFileCollection files,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new AddEmergencyServiceAttachmentCommand(id, files), cancellationToken)))
            .Produces<int[]>()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .DisableAntiforgery()
            .WithName("Add Emergency Service Attachment")
            .WithSummary("Add attachments to an emergency service")
            .WithDescription("Upload one or more attachment files for an emergency service. Returns the IDs of the created attachments.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the emergency service";
                return op;
            });

        app.MapDelete("/DeleteEmergencyServiceAttachment/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new DeleteEmergencyServiceAttachmentCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete Emergency Service Attachments")
            .WithSummary("Delete all attachments for an emergency service")
            .WithDescription("Deletes all attachment files associated with the specified emergency service.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the emergency service";
                return op;
            });

        return app;
    }
}

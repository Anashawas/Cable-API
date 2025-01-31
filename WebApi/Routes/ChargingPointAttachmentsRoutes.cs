using Application.ChargingPointAttachments.Command;
using Application.ChargingPointAttachments.Command.DeleteChargingPointAttachment;
using Application.ChargingPointAttachments.Queries.GetAllChargingPointAttachmentsById;
using Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class ChargingPointAttachmentsRoutes
{
    public static IEndpointRouteBuilder MapChargingPointAttachmentsRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/chargingPointAttchments")
            .WithTags()
            .MapRoute();

        return app;
    }

    private static RouteGroupBuilder MapRoute(this RouteGroupBuilder app)
    {
        app.MapGet("GetAllChargingPointAttachmentsByIdRequest/{id:int}",
                async (IMediator mediator, int id, CancellationToken cancellationToken) =>
                    Results.Ok(
                        await mediator.Send(new GetAllChargingPointAttachmentsByIdRequest(id), cancellationToken)))
            .Produces<List<UploadFile>>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .WithName("Get All Charging Point Attachments By Id")
            .WithSummary("Get All Charging Point Attachments By Id")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                return op;
            });

        app.MapPost("AddChargingPointAttachmentCommand/{id:int}",
                async (IMediator mediator, [FromRoute] int id, IFormFileCollection files,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new AddChargingPointAttachmentCommand(id, files),
                        cancellationToken)))
            .Produces<int[]>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Add Charging Point Attachment Command")
            .WithSummary("Add Charging Point Attachment Command")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            }).DisableAntiforgery();

        app.MapDelete("DeleteChargingPointAttachmentCommand/{id:int}",
                async (IMediator mediator, int id, CancellationToken cancellationToken)
                    => await mediator.Send(new DeleteChargingPointAttachmentCommand(id), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Delete Charging Point Attachment Command")
            .WithSummary("Delete Charging Point Attachment Command")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                return op;
            });

        return app;
    }
}
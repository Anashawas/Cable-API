using Application.Common.Models;
using Application.OfferAttachments.Commands.AddOfferAttachment;
using Application.OfferAttachments.Commands.DeleteOfferAttachment;
using Application.OfferAttachments.Queries.GetOfferAttachments;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class OfferAttachmentRoutes
{
    public static IEndpointRouteBuilder MapOfferAttachmentRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/offerAttachments")
            .WithTags("Offer Attachments")
            .MapRoute();

        return app;
    }

    private static RouteGroupBuilder MapRoute(this RouteGroupBuilder app)
    {
        app.MapGet("GetOfferAttachments/{id:int}",
                async (IMediator mediator, int id, CancellationToken cancellationToken) =>
                    Results.Ok(
                        await mediator.Send(new GetOfferAttachmentsRequest(id), cancellationToken)))
            .Produces<List<UploadFile>>()
            .WithName("Get Offer Attachments By Offer Id")
            .WithSummary("Retrieves all attachments for a specific offer")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                return op;
            });

        app.MapPost("AddOfferAttachment/{id:int}",
                async (IMediator mediator, [FromRoute] int id, IFormFileCollection files,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new AddOfferAttachmentCommand(id, files),
                        cancellationToken)))
            .Produces<int[]>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Add Offer Attachment")
            .WithSummary("Upload attachments for a specific offer")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            }).DisableAntiforgery();

        app.MapDelete("DeleteOfferAttachments/{id:int}",
                async (IMediator mediator, int id, CancellationToken cancellationToken)
                    => await mediator.Send(new DeleteOfferAttachmentCommand(id), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Delete Offer Attachments")
            .WithSummary("Delete all attachments for a specific offer")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                return op;
            });

        return app;
    }
}

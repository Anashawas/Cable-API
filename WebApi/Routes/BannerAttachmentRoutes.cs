using Application.Banners.Commands.DeleteBanner;
using Application.BannersAttachments.Commands;
using Application.BannersAttachments.Commands.DeleteBannerAttachment;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class BannerAttachmentRoutes
{
    public static IEndpointRouteBuilder MapBannerAttachmentRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/bannerAttachment")
            .WithTags("Banner Attachment")
            .MapGroup();
        return app;
    }

    private static RouteGroupBuilder MapGroup(this RouteGroupBuilder app)
    {
        app.MapPost("/AddBanner/{id:int}",
                async (IMediator mediator, [FromRoute] int id,IFormFileCollection files, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new AddBannerAttachmentsCommand (id,files), cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .DisableAntiforgery()
            .WithName("Add Banner Attachment ")
            .WithSummary("Add a new banner attachment ")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the banner";
                return op;
            });

        app.MapDelete("/DeleteBanner/{id:int}",
                async (IMediator mediator, int id, CancellationToken cancellationToken) =>
                    await mediator.Send(new DeleteBannerAttachmentCommand(id), cancellationToken))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Delete Banner Attachments")
            .WithSummary("Delete banner attachments")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the banner";
                return op;
            });
        return app;
    }
}
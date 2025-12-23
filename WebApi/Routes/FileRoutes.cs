using Application.BannersAttachments.Queries.GetAllBannerAttachmentsById;

using Application.Common.Interfaces;
using Cable.Core.Emuns;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Infrastructrue.Options;

namespace Cable.Routes;

public static class FileRoutes
{
    public static IEndpointRouteBuilder MapFileRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/files")
            .WithTags("Files")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/GetAllBannerAttachmentsById/{id:int}",async ([FromRoute] int id,
                IMediator mediator, CancellationToken cancellationToken) =>
                await mediator.Send(new GetAllBannerAttachmentsByIdRequest(id), cancellationToken)
            )
            .RequireAuthorization()
            .Produces<List<string>>(200)
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get All Banner Attachments By Id")
            .WithSummary("Retrieves all banner attachments by ID with authentication");
        app.MapGet("/{folder}/{fileName}",
                ([FromRoute] string folder, [FromRoute] string fileName, 
                    IOptions<UploadFileOptions> uploadFileOptions, ICurrentUserService currentUserService) =>
                {
                    if (!Enum.TryParse<UploadFileFolders>(folder, true, out var uploadFolder))
                    {
                        return Results.BadRequest("Invalid folder specified");
                    }

                    try
                    {
                        var fileUploadPath = uploadFileOptions.Value.FileUploadPath;
                        var userId = currentUserService.UserId?.ToString() ?? "0";
                        var filePath = Path.Combine(fileUploadPath, folder, userId, fileName);

                        if (!File.Exists(filePath))
                        {
                            return Results.NotFound("File not found");
                        }

                        var contentType = GetContentType(fileName);
                        return Results.File(filePath, contentType, fileName, enableRangeProcessing: true);
                    }
                    catch (Exception)
                    {
                        return Results.Problem("Error retrieving file");
                    }
                })
            .RequireAuthorization()
            .Produces<FileResult>(200)
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get File")
            .WithSummary("Retrieves a file with authentication")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The folder name (e.g., banners, chargingpoints)";
                op.Parameters[1].Required = true;
                op.Parameters[1].Description = "The file name";
                return op;
            });

        return app;
    }

    private static string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }
}
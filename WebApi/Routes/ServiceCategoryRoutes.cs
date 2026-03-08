using Application.ServiceCategories.Commands.CreateServiceCategory;
using Application.ServiceCategories.Commands.DeleteServiceCategory;
using Application.ServiceCategories.Commands.UpdateServiceCategory;
using Application.ServiceCategories.Commands.UploadServiceCategoryIcon;
using Application.ServiceCategories.Queries.GetAllServiceCategories;
using Cable.Requests.ServiceCategories;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class ServiceCategoryRoutes
{
    public static IEndpointRouteBuilder MapServiceCategoryRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/service-categories")
            .WithTags("Service Categories")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        // Get all categories
        app.MapGet("/GetAllServiceCategories",
                async (IMediator mediator, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetAllCategoriesRequest(), cancellationToken)))
            .Produces<List<ServiceCategoryDto>>()
            .ProducesInternalServerError()
            .WithName("Get All Service Categories")
            .WithSummary("Get all active service categories")
            .WithDescription("Returns all active service categories ordered by sort order.")
            .WithOpenApi();

        // Create category
        app.MapPost("/CreateServiceCategory",
                async (IMediator mediator, CreateServiceCategoryRequest request,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new CreateServiceCategoryCommand(
                        request.Name,
                        request.NameAr,
                        request.Description,
                        request.IconUrl,
                        request.SortOrder,
                        request.IsActive
                    ), cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Create Service Category")
            .WithSummary("Create a new service category")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // Update category
        app.MapPut("/UpdateServiceCategory/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdateServiceCategoryRequest request,
                    CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new UpdateServiceCategoryCommand(
                        id,
                        request.Name,
                        request.NameAr,
                        request.Description,
                        request.IconUrl,
                        request.SortOrder,
                        request.IsActive
                    ), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Update Service Category")
            .WithSummary("Update an existing service category")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service category to update";
                op.RequestBody.Required = true;
                return op;
            });

        // Delete category
        app.MapDelete("/DeleteServiceCategory/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new DeleteServiceCategoryCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete Service Category")
            .WithSummary("Delete a service category")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service category to delete";
                return op;
            });

        // Upload category icon
        app.MapPost("/UploadServiceCategoryIcon/{id:int}",
                async (IMediator mediator, [FromForm] IFormFile file, [FromRoute] int id,
                        CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new UploadServiceCategoryIconCommand(file, id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Upload Service Category Icon")
            .WithSummary("Upload an icon for a service category")
            .WithOpenApi()
            .DisableAntiforgery();

        return app;
    }
}

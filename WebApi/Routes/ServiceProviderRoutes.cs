using Application.Common.Models;
using Application.ServiceProviders.Commands.AddServiceProviderAttachment;
using Application.ServiceProviders.Commands.AddToFavoriteService;
using Application.ServiceProviders.Commands.ChangeServiceProviderOwner;
using Application.ServiceProviders.Commands.CreateServiceProvider;
using Application.ServiceProviders.Commands.DeleteServiceProvider;
using Application.ServiceProviders.Commands.DeleteServiceProviderAttachment;
using Application.ServiceProviders.Commands.RateServiceProvider;
using Application.ServiceProviders.Commands.RemoveFromFavoriteService;
using Application.ServiceProviders.Commands.UpdateServiceProvider;
using Application.ServiceProviders.Commands.UploadServiceProviderIcon;
using Application.ServiceProviders.Commands.VerifyServiceProvider;
using Application.Favorites.Queries.CheckIsFavorite;
using Application.ServiceProviders.Queries.CheckIsFavoriteService;
using Application.ServiceProviders.Queries.GetAllServiceProviders;
using Application.ServiceProviders.Queries.GetMyFavoriteServices;
using Application.ServiceProviders.Queries.GetNearbyServiceProviders;
using Application.ServiceProviders.Queries.GetServiceProviderAttachments;
using Application.ServiceProviders.Queries.GetServiceProviderById;
using Application.ServiceProviders.Queries.GetServiceProviderRatings;
using Application.ServiceProviders.Queries.GetServiceProvidersByCategory;
using Cable.Requests.ServiceProviders;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class ServiceProviderRoutes
{
    public static IEndpointRouteBuilder MapServiceProviderRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/service-providers")
            .WithTags("Service Providers")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        // Get all service providers
        app.MapGet("/GetAllServiceProviders",
                async (IMediator mediator, [FromQuery] int? categoryId,
                        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetAllServiceProvidersRequest(categoryId, page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<ServiceProviderDto>>()
            .Produces<PagedResult<ServiceProviderDto>>()
            .ProducesInternalServerError()
            .WithName("Get All Service Providers")
            .WithSummary("Get all service providers")
            .WithDescription("Returns all service providers. Optional filter by category ID.")
            .WithOpenApi();

        // Get service provider by ID
        app.MapGet("/GetServiceProviderById/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetServiceProviderByIdRequest(id), cancellationToken)))
            .Produces<ServiceProviderDto>()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get Service Provider By Id")
            .WithSummary("Get a service provider by ID")
            .WithDescription("Returns a single service provider and increments its visitor count.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider";
                return op;
            });

        // Get by category
        app.MapGet("/GetByCategory/{categoryId:int}",
                async (IMediator mediator, [FromRoute] int categoryId,
                        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetProvidersByCategoryRequest(categoryId, page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<ServiceProviderDto>>()
            .Produces<PagedResult<ServiceProviderDto>>()
            .ProducesInternalServerError()
            .WithName("Get Service Providers By Category")
            .WithSummary("Get service providers by category")
            .WithDescription("Returns all service providers in a specific category.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service category";
                return op;
            });

        // Get nearby
        app.MapGet("/GetNearby",
                async (IMediator mediator, [FromQuery] double latitude, [FromQuery] double longitude,
                        [FromQuery] double? radiusKm, [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetNearbyProvidersRequest(latitude, longitude, radiusKm ?? 10, page, pageSize),
                        cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<ServiceProviderDto>>()
            .Produces<PagedResult<ServiceProviderDto>>()
            .ProducesInternalServerError()
            .WithName("Get Nearby Service Providers")
            .WithSummary("Get nearby service providers")
            .WithDescription("Returns service providers within a specified radius (default 10 km).")
            .WithOpenApi();

        // Get my favorites
        app.MapGet("/GetMyFavorites",
                async (IMediator mediator, [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetMyFavoriteServicesRequest(page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<ServiceProviderDto>>()
            .Produces<PagedResult<ServiceProviderDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get My Favorite Service Providers")
            .WithSummary("Get current user's favorite service providers")
            .WithOpenApi();

        // Get ratings
        app.MapGet("/GetRatings/{serviceProviderId:int}",
                async (IMediator mediator, [FromRoute] int serviceProviderId, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetProviderRatingsRequest(serviceProviderId),
                        cancellationToken)))
            .Produces<List<ServiceProviderRatingDto>>()
            .ProducesInternalServerError()
            .WithName("Get Service Provider Ratings")
            .WithSummary("Get all ratings for a service provider")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider";
                return op;
            });

        // Create service provider
        app.MapPost("/CreateServiceProvider",
                async (IMediator mediator, CreateServiceProviderRequest request,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new CreateServiceProviderCommand(
                        request.Name,
                        request.ServiceCategoryId,
                        request.StatusId,
                        request.Description,
                        request.Phone,
                        request.OwnerPhone,
                        request.Address,
                        request.CountryName,
                        request.CityName,
                        request.Latitude,
                        request.Longitude,
                        request.Price,
                        request.PriceDescription,
                        request.FromTime,
                        request.ToTime,
                        request.MethodPayment,
                        request.HasOffer,
                        request.OfferDescription,
                        request.Service,
                        request.Note,
                        request.WhatsAppNumber,
                        request.WebsiteUrl
                    ), cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Create Service Provider")
            .WithSummary("Create a new service provider")
            .WithDescription("Creates a new service provider. The current user becomes the owner.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // Update service provider
        app.MapPut("/UpdateServiceProvider/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdateServiceProviderRequest request,
                    CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new UpdateServiceProviderCommand(
                        id,
                        request.Name,
                        request.ServiceCategoryId,
                        request.StatusId,
                        request.Description,
                        request.Phone,
                        request.OwnerPhone,
                        request.Address,
                        request.CountryName,
                        request.CityName,
                        request.Latitude,
                        request.Longitude,
                        request.Price,
                        request.PriceDescription,
                        request.FromTime,
                        request.ToTime,
                        request.MethodPayment,
                        request.IsVerified,
                        request.HasOffer,
                        request.OfferDescription,
                        request.Service,
                        request.Note,
                        request.WhatsAppNumber,
                        request.WebsiteUrl
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
            .WithName("Update Service Provider")
            .WithSummary("Update an existing service provider")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider to update";
                op.RequestBody.Required = true;
                return op;
            });

        // Delete service provider
        app.MapDelete("/DeleteServiceProvider/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new DeleteServiceProviderCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete Service Provider")
            .WithSummary("Delete a service provider")
            .WithDescription("Soft-deletes a service provider.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider to delete";
                return op;
            });

        // Verify service provider
        app.MapPut("/VerifyServiceProvider/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new VerifyServiceProviderCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Verify Service Provider")
            .WithSummary("Verify a service provider (admin)")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider to verify";
                return op;
            });

        // Rate service provider
        app.MapPost("/RateServiceProvider/{serviceProviderId:int}",
                async (IMediator mediator, [FromRoute] int serviceProviderId, RateServiceProviderRequest request,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new RateServiceProviderCommand(serviceProviderId, request.Rating, request.Comment),
                        cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Rate Service Provider")
            .WithSummary("Rate a service provider")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider to rate";
                op.RequestBody.Required = true;
                return op;
            });

        // Check if favorited — mirrors GET /api/favorites/check/{id} for stations
        app.MapGet("/CheckIsFavorite/{serviceProviderId:int}",
                async (IMediator mediator, [FromRoute] int serviceProviderId,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new CheckIsFavoriteServiceRequest(serviceProviderId),
                        cancellationToken)))
            .Produces<CheckIsFavoriteDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Check Service Provider Is Favorite")
            .WithSummary("Check whether a service provider is in the caller's favorites")
            .WithDescription("Returns the same shape as the charging-point check (isFavorite + favoriteId), so a client can share one model across both. Lets a detail screen render its favorite state without fetching the full favorites list.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider";
                return op;
            });

        // Add to favorites
        app.MapPost("/AddToFavorites/{serviceProviderId:int}",
                async (IMediator mediator, [FromRoute] int serviceProviderId,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new AddToFavoriteServiceCommand(serviceProviderId),
                        cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Add Service Provider To Favorites")
            .WithSummary("Add a service provider to favorites")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider";
                return op;
            });

        // Remove from favorites
        app.MapDelete("/RemoveFromFavorites/{serviceProviderId:int}",
                async (IMediator mediator, [FromRoute] int serviceProviderId,
                    CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new RemoveFromFavoriteServiceCommand(serviceProviderId), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Remove Service Provider From Favorites")
            .WithSummary("Remove a service provider from favorites")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider";
                return op;
            });

        // Upload service provider icon
        app.MapPost("/UploadServiceProviderIcon/{id:int}",
                async (IMediator mediator, [FromForm] IFormFile file, [FromRoute] int id,
                        CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new UploadServiceProviderIconCommand(file, id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Upload Service Provider Icon")
            .WithSummary("Upload an icon for a service provider")
            .WithDescription("Uploads an icon image. Replaces the existing icon if one exists.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider";
                return op;
            })
            .DisableAntiforgery();

        // Get service provider attachments
        app.MapGet("/GetAttachments/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetServiceProviderAttachmentsRequest(id), cancellationToken)))
            .Produces<List<UploadFile>>()
            .ProducesInternalServerError()
            .WithName("Get Service Provider Attachments")
            .WithSummary("Get all attachments for a service provider")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider";
                return op;
            });

        // Add service provider attachments
        app.MapPost("/AddAttachments/{id:int}",
                async (IMediator mediator, [FromRoute] int id, IFormFileCollection files,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new AddServiceProviderAttachmentCommand(id, files),
                        cancellationToken)))
            .Produces<int[]>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Add Service Provider Attachments")
            .WithSummary("Upload attachments for a service provider")
            .WithDescription("Uploads one or more files as attachments for a service provider.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider";
                return op;
            })
            .DisableAntiforgery();

        // Delete service provider attachments
        app.MapDelete("/DeleteAttachments/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new DeleteServiceProviderAttachmentCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete Service Provider Attachments")
            .WithSummary("Delete all attachments for a service provider")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider";
                return op;
            });

        // Change service provider owner (admin)
        app.MapPatch("/ChangeOwner/{serviceProviderId:int}",
                async (IMediator mediator, [FromRoute] int serviceProviderId, ChangeServiceProviderOwnerRequest request,
                        CancellationToken cancellationToken) =>
                    await mediator.Send(new ChangeServiceProviderOwnerCommand(serviceProviderId, request.NewOwnerId), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Change Service Provider Owner")
            .WithSummary("Changes the owner of a service provider (admin)")
            .WithDescription("Transfers ownership of a service provider to a different user. New owner must have Provider role.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider";
                op.RequestBody.Required = true;
                return op;
            });

        return app;
    }
}

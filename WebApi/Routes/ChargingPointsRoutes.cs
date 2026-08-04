using Application.ChargingPoints.Commands.AddChargingPoint;
using Application.ChargingPoints.Commands.ChangeChargingPointOwner;
using Application.ChargingPoints.Commands.DeleteChargingPoint;
using Application.ChargingPoints.Commands.UpdateChangingPointStatus;
using Application.ChargingPoints.Commands.UpdateChargingPoint;
using Application.ChargingPoints.Commands.UpdateChargingPointLocation;
using Application.ChargingPoints.Commands.SetStationPremium;
using Application.ChargingPoints.Commands.UpdateChargingPointVisitorsCount;
using Application.ChargingPoints.Commands.UploadChargingPointIcon;
using Application.ChargingPoints.Queries;
using Application.ChargingPoints.Queries.GetAllChargingPoints;
using Application.ChargingPoints.Queries.GetAllChargingPointsByUser;
using Application.ChargingPoints.Queries.GetChargingPointById;
using Application.ChargingPoints.Queries.GetChargingPointsPaged;
using Application.ChargingPoints.Queries.GetMyChargingPoints;
using Application.ChargingPoints.Queries.GetStationPremiumHistory;
using Application.Common.Models;
using Cable.Requests.ChargingPoints;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class ChargingPointsRoutes
{
    public static IEndpointRouteBuilder MapChargingPointsRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/charging-points")
            .WithTags("Charging Points")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapPost("/GetAllChargingPoints",
                async (GetAllChargingPointsRequest request, IMediator mediator, CancellationToken cancellation) =>
                {
                    // Opt-in server-side filtering/paging (A2b): any new field switches to
                    // the paged envelope. Legacy calls keep the plain array unchanged.
                    var usePaged = request.Search != null || request.StatusId.HasValue
                                   || request.ChargerBrandId.HasValue || request.IsVerified.HasValue
                                   || request.PlugTypeId.HasValue || request.Sort != null
                                   || request.Page.HasValue || request.PageSize.HasValue;

                    if (usePaged)
                        return Results.Ok(await mediator.Send(new GetChargingPointsPagedRequest(
                            request.Search, request.ChargerPointTypeId, request.CityName,
                            request.StatusId, request.ChargerBrandId, request.IsVerified,
                            request.PlugTypeId, request.Sort, request.Page, request.PageSize), cancellation));

                    return Results.Ok(await mediator.Send(request, cancellation));
                })
            .Produces<List<GetAllChargingPointsDto>>()
            .Produces<PagedResult<GetAllChargingPointsDto>>()
            .ProducesInternalServerError()
            .WithName("Get all charging points")
            .WithSummary("Stations list. Legacy body returns a plain array; any of search/statusId/chargerBrandId/isVerified/plugTypeId/sort/page/pageSize returns { items, totalCount, page, pageSize }.")
            .WithOpenApi();

        app.MapPost("GetAllChargingPointByUserId",
                async (IMediator mediator, GetAllChargingPointsByUserRequest request,
                        CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(request, cancellationToken);
                    return Results.Ok(request.Page.HasValue || request.PageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<GetAllChargingPointsDto>>()
            .Produces<PagedResult<GetAllChargingPointsDto>>()
            .ProducesInternalServerError()
            .WithName("Get all charging points by user id")
            .WithSummary("Get all charging points by user id of the application")
            .WithOpenApi();

        app.MapPost("GetMyChargingPoints",
                async (IMediator mediator, GetMyChargingPointsRequest request,
                        CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(request, cancellationToken);
                    return Results.Ok(request.Page.HasValue || request.PageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<GetAllChargingPointsDto>>()
            .Produces<PagedResult<GetAllChargingPointsDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get my charging points")
            .WithSummary("Get all charging points owned by the currently logged-in user")
            .WithDescription("Returns all charging points where the current user is the owner. Optional filters for charger point type and city name.")
            .WithOpenApi();


        app.MapPost("UploadChargingPoint/{id:int}",
                async (IMediator mediator,[FromForm] IFormFile file,[FromRoute] int id,
                        CancellationToken cancellationToken) =>
                    await mediator.Send(new UploadChargingPointIconCommand(file,id) ,cancellationToken))
            .Produces(200)
            .ProducesInternalServerError()
            .WithName("Upload charging point icon")
            .WithSummary("Upload charging point icon")
            .WithOpenApi()
            .DisableAntiforgery();


        app.MapGet("/GetNearest",
                async (IMediator mediator, [FromQuery] double lat, [FromQuery] double lng,
                        [FromQuery] string? premium, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new Application.ChargingPoints.Queries.GetNearestStations.GetNearestStationsRequest(
                            lat, lng, premium ?? "all"), cancellationToken)))
            .Produces<List<Application.ChargingPoints.Queries.GetNearestStations.NearestStationDto>>()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Get nearest stations")
            .WithSummary("Nearest stations for the home screen, within the admin nearby-radius, distance-sorted. premium = true | false | all")
            .WithDescription("isPremium derives from the paid premium dates (warn-only expiry). Each item carries distanceKm.")
            .WithOpenApi();

        app.MapGet("/GetNearestPremium",
                async (IMediator mediator, [FromQuery] double lat, [FromQuery] double lng,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new Application.ChargingPoints.Queries.GetNearestStations.GetNearestStationsRequest(
                            lat, lng, "true"), cancellationToken)))
            .Produces<List<Application.ChargingPoints.Queries.GetNearestStations.NearestStationDto>>()
            .ProducesInternalServerError()
            .WithName("Get nearest premium stations")
            .WithSummary("Nearest PREMIUM stations (paid home cards), distance-sorted within the admin nearby-radius")
            .WithOpenApi();

        app.MapGet("/GetNearestNormal",
                async (IMediator mediator, [FromQuery] double lat, [FromQuery] double lng,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new Application.ChargingPoints.Queries.GetNearestStations.GetNearestStationsRequest(
                            lat, lng, "false"), cancellationToken)))
            .Produces<List<Application.ChargingPoints.Queries.GetNearestStations.NearestStationDto>>()
            .ProducesInternalServerError()
            .WithName("Get nearest normal stations")
            .WithSummary("Nearest NON-premium stations (second card / fallback), distance-sorted within the admin nearby-radius")
            .WithOpenApi();

        app.MapGet("/view-image/pending",
                async (IMediator mediator, [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new Application.ChargingPoints.Queries.GetPendingViewImages.GetPendingViewImagesRequest(page, pageSize),
                        cancellationToken)))
            .Produces<Application.Common.Models.PagedResult<Application.ChargingPoints.Queries.GetPendingViewImages.PendingViewImageDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get pending view images")
            .WithSummary("Admin: review queue of stations whose promo image awaits approval")
            .WithOpenApi();

        app.MapPost("/{id:int}/view-image",
                async (IMediator mediator, [FromRoute] int id, [FromForm] IFormFile file,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new Application.ChargingPoints.Commands.StationViewImage.UploadStationViewImageCommand(id, file),
                        cancellationToken)))
            .Produces<Application.ChargingPoints.Commands.StationViewImage.ViewImageResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Upload station view image")
            .WithSummary("Upload/REPLACE the single premium-card promo image (owner/worker → pending review; admin → approved)")
            .WithOpenApi()
            .DisableAntiforgery();

        app.MapPut("/{id:int}/view-image/review",
                async (IMediator mediator, [FromRoute] int id, [FromQuery] bool approve,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new Application.ChargingPoints.Commands.StationViewImage.ReviewStationViewImageCommand(id, approve),
                        cancellationToken)))
            .Produces<Application.ChargingPoints.Commands.StationViewImage.ViewImageResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Review station view image")
            .WithSummary("Admin: approve or reject the station's promo image (?approve=true|false). Only approved images are served to B2C")
            .WithOpenApi();

        app.MapDelete("/{id:int}/view-image",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(
                        new Application.ChargingPoints.Commands.StationViewImage.DeleteStationViewImageCommand(id),
                        cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete station view image")
            .WithSummary("Remove the station's promo image (owner, worker, or admin)")
            .WithOpenApi();

        app.MapGet("/GetChargingPointById/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new GetChargingPointByIdRequest(id), cancellationToken)))
            .Produces<GetChargingPointByIdDto>()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get charging point by id")
            .WithSummary("Get charging point by id of the application")
            .WithOpenApi();

        app.MapPost("/AddChargingPoint",
                async (IMediator mediator,
                        AddChargingPointCommand request,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        request,
                        cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .DisableAntiforgery()
            .WithName("Add charging point")
            .WithSummary("Add charging point of the application")
            .WithOpenApi();


        app.MapDelete("/DeleteChargingPoint/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                await mediator.Send(new DeleteChargingPointCommand(id), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete charging point")
            .WithSummary("Delete charging point of the application")
            .WithOpenApi();

        app.MapPut("/UpdateChargingPoint/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdateChargingPointRequest request,
                        CancellationToken cancellationToken) =>
                    await mediator.Send(
                        new UpdateChargingPointCommand(id, request.Name, request.Note, request.CountryName,
                            request.CityName, request.Phone, request.MethodPayment,
                            request.Price, request.FromTime, request.ToTime, request.ChargerSpeed,
                            request.ChargersCount, request.Latitude, request.Longitude,
                            request.ChargerPointTypeId, request.StatusId,
                            request.StationTypeId, request.OwnerPhone,request.IsVerified, request.HasOffer, request.Service, request.OfferDescription,request.Address, request.PlugTypeIds, request.ChargerBrands ),
                        cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Update charging point")
            .WithSummary("Update charging point of the application")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the charging point";
                op.RequestBody.Required = true;
                return op;
            });

        app.MapPut("UpdateChargingPointLocation/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdateChargingPointLocationRequest request,
                        CancellationToken cancellationToken) =>
                    await mediator.Send(
                        new UpdateChargingPointLocationCommand(id, request.Latitude, request.Longitude),
                        cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Update charging point location")
            .WithSummary("Update charging point location of the application")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the charging point";
                op.RequestBody.Required = true;
                return op;
            });

        app.MapPatch("UpdateChargingPointVisitorsCount/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                await mediator.Send(
                    new UpdateChargingPointVisitorsCountCommand(id), cancellationToken))
            .Produces(200)
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Update charging point visitors count")
            .WithSummary("Update charging point visitors count of the application")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the charging point";
                return op;
            });

        app.MapPatch("UpdateChargingPointStatus/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdateChargingPointStatusRequest request,
                        CancellationToken cancellationToken) =>
                    await mediator.Send(new UpdateChargingPointStatusCommand(id, request.StatusId), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Update charging point status")
            .WithSummary("Update charging point status of the application")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the charging point";
                op.RequestBody.Required = true;
                return op;
            });


        // Set / renew premium subscription (admin) — records payment + expiry dates
        app.MapPatch("/{id:int}/premium",
                async (IMediator mediator, [FromRoute] int id, SetStationPremiumRequest request,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new SetStationPremiumCommand(id, request.PaymentDate, request.ExpiresAt,
                            request.Amount, request.Note), cancellationToken)))
            .Produces<SetStationPremiumResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Set Station Premium")
            .WithSummary("Records a premium payment for a station (payment date + expiry date) and marks it Premium")
            .WithDescription("Appends a row to the premium payment history, updates the station's premium dates and sets its station type to Premium. Call again to renew.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the charging point";
                op.RequestBody.Required = true;
                return op;
            });

        // Premium payment history (admin/owner)
        app.MapGet("/{id:int}/premium-history",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetStationPremiumHistoryRequest(id), cancellationToken)))
            .Produces<StationPremiumHistoryDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get Station Premium History")
            .WithSummary("Gets the premium payment history and current premium dates for a station")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the charging point";
                return op;
            });

        // Change charging point owner (admin)
        app.MapPatch("/ChangeOwner/{chargingPointId:int}",
                async (IMediator mediator, [FromRoute] int chargingPointId, ChangeChargingPointOwnerRequest request,
                        CancellationToken cancellationToken) =>
                    await mediator.Send(new ChangeChargingPointOwnerCommand(chargingPointId, request.NewOwnerId), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Change Charging Point Owner")
            .WithSummary("Changes the owner of a charging point (admin)")
            .WithDescription("Transfers ownership of a charging point to a different user. New owner must have Provider role.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the charging point";
                op.RequestBody.Required = true;
                return op;
            });

        return app;
    }

}
using Application.ChargingPoints.Commands.AddChargingPoint;
using Application.ChargingPoints.Commands.DeleteChargingPoint;
using Application.ChargingPoints.Commands.UpdateChangingPointStatus;
using Application.ChargingPoints.Commands.UpdateChargingPoint;
using Application.ChargingPoints.Commands.UpdateChargingPointLocation;
using Application.ChargingPoints.Commands.UpdateChargingPointVisitorsCount;
using Application.ChargingPoints.Commands.UploadChargingPointIcon;
using Application.ChargingPoints.Queries;
using Application.ChargingPoints.Queries.GetAllChargingPoints;
using Application.ChargingPoints.Queries.GetAllChargingPointsByUser;
using Application.ChargingPoints.Queries.GetChargingPointById;
using Cable.Requests.ChargingPoints;
using Hangfire;
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
                    Results.Ok(await mediator.Send(request, cancellation)))
            .Produces<List<GetAllChargingPointsDto>>()
            .ProducesInternalServerError()
            .WithName("Get all charging points")
            .WithSummary(" Get all charging points of the application")
            .WithOpenApi();

        app.MapPost("GetAllChargingPointByUserId",
                async (IMediator mediator, GetAllChargingPointsByUserRequest request,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<List<GetAllChargingPointsDto>>()
            .ProducesInternalServerError()
            .WithName("Get all charging points by user id")
            .WithSummary("Get all charging points by user id of the application")
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
                            request.StationTypeId, request.OwnerPhone,request.IsVerified, request.HasOffer, request.Service, request.OfferDescription,request.Address, request.PlugTypeIds ),
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


        return app;
    }
}
using Application.CarsManagement.Cars.Commands.AddCar;
using Application.CarsManagement.Cars.Commands.DeleteCar;
using Application.CarsManagement.Cars.Commands.UpdateCar;
using Application.CarsManagement.Cars.Queries.GetAllCars;
using Application.CarsManagement.CarsModels.Commands.AddCarModal;
using Application.CarsManagement.CarsModels.Commands.DeleteCarModel;
using Application.CarsManagement.CarsModels.Commands.UpdateCarModel;
using Application.CarsManagement.CarsModels.Queries.GetAllCarsModels;
using Application.CarsManagement.CarsTypes.Commands.AddCarTypeCommand;
using Application.CarsManagement.CarsTypes.Commands.DeleteCarType;
using Application.CarsManagement.CarsTypes.Commands.UpdateCarType;
using Application.CarsManagement.CarsTypes.Queries.GetAllCarsTypes;
using Application.CarsManagement.UserCars.Commands.AddUserCar;
using Application.CarsManagement.UserCars.Commands.DeleteUserCar;
using Application.CarsManagement.UserCars.Commands.UpdateUserCar;
using Application.CarsManagement.UserCars.Queries.GetAllUserUserCars;
using Cable.Requests.CarsManagement;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class CarManagementRoutes
{
    public static IEndpointRouteBuilder MapCarManagementRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/carmanagement")
            .WithTags("Car Management")
            .MapCarTypesRoutes()
            .MapCarModelsRoutes()
            .MapCarsRoutes()
            .MapUserCarsRoutes()
            ;
        return app;
    }

    private static RouteGroupBuilder MapUserCarsRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/GetAllUserCars",
                async ( IMediator mediator, CancellationToken cancellation) =>
                    Results.Ok(await mediator.Send(new GetAllUserCarsRequest (), cancellation)))
            .Produces<List<GetAllUserCarsDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get all user cars")
            .WithSummary(" Get all  user cars")
            .WithOpenApi();


        app.MapPost("/AddUserCar",
                async (IMediator mediator, AddUserCarCommand request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Add user car")
            .WithSummary("Add user car")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the user car";
                return op;
            });

        app.MapDelete("/DeleteUserCar/{id}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                await mediator.Send(new DeleteUserCarCommand(id), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete user car")
            .WithSummary("Delete user car of the application")
            .WithOpenApi();

        app.MapPut("/UpdateUserCar",
                async (IMediator mediator, UpdateUserCarCommand request,
                        CancellationToken cancellationToken) =>
                    await mediator.Send(
                        request, cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Update user car")
            .WithSummary("Update user car")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        return app;
    }

    private static RouteGroupBuilder MapCarModelsRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/GetAllCarModels",
                async (IMediator mediator, CancellationToken cancellation) =>
                    Results.Ok(await mediator.Send(new GetAllCarsModelsRequest(), cancellation)))
            .Produces<List<GetAllCarsModelsDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get all  car models ")
            .WithSummary(" Get all car models")
            .WithOpenApi();

        app.MapPost("/AddCarModel",
                async (IMediator mediator, AddCarModelCommand request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Add car model")
            .WithSummary("Add car model")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the car model";
                return op;
            });
        app.MapDelete("/DeleteCarModel/{id}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                await mediator.Send(new DeleteCarModelCommand(id), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete car model")
            .WithSummary("Delete car model")
            .WithOpenApi();

        app.MapPut("/UpdateCarModel/{id:int}",
                async ([FromRoute] int id, IMediator mediator, UpdateCarModelRequest request,
                        CancellationToken cancellationToken) =>
                    await mediator.Send(
                        new UpdateCarModelCommand(id, request.Name, request.CarTypeId), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Update car model")
            .WithSummary("Update car model")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the car model";
                op.RequestBody.Required = true;
                return op;
            });


        return app;
    }

    private static RouteGroupBuilder MapCarTypesRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/GetAllCarTypes",
                async (IMediator mediator, CancellationToken cancellation) =>
                    Results.Ok(await mediator.Send(new GetAllCarsTypesRequest(), cancellation)))
            .Produces<List<GetAllCarsTypesDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get all car types ")
            .WithSummary(" Get all  car types ")
            .WithOpenApi();

        app.MapPost("/AddCarType",
                async (IMediator mediator, AddCarTypeCommand request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Add car type ")
            .WithSummary("Add car type")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the car type";
                return op;
            });

        app.MapDelete("/DeleteCarType/{id}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                await mediator.Send(new DeleteCarTypeCommand(id), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete car type")
            .WithSummary("Delete car type")
            .WithOpenApi();


        app.MapPut("/UpdateCarType/{id:int}",
                async ([FromRoute] int id, IMediator mediator, UpdateCarTypeRequest request,
                        CancellationToken cancellationToken) =>
                    await mediator.Send(
                        new UpdateCarTypeCommand(id, request.Name), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Update car type")
            .WithSummary("Update car type")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the car type";
                op.RequestBody.Required = true;
                return op;
            });

        return app;
    }

    private static RouteGroupBuilder MapCarsRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/GetAllCars",
                async (IMediator mediator, CancellationToken cancellation) =>
                    Results.Ok(await mediator.Send(new GetAllCarsRequest(), cancellation)))
            .Produces<List<GetAllCarsDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get all cars ")
            .WithSummary("Get all cars")
            .WithOpenApi();


        app.MapPost("/AddCar",
                async (IMediator mediator, AddCarCommand request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Add car ")
            .WithSummary("Add car ")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the car";
                return op;
            });
        app.MapDelete("/DeleteCar/{id}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                await mediator.Send(new DeleteCarCommand(id), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete car ")
            .WithSummary("Delete car")
            .WithOpenApi();

        app.MapPut("/UpdateCar/{id:int}",
                async ([FromRoute] int id, IMediator mediator, UpdateCarRequest request,
                        CancellationToken cancellationToken) =>
                    await mediator.Send(
                        new UpdateCarCommand(id, request.CarModelId), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Update car ")
            .WithSummary("Update car by id")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the car";
                op.RequestBody.Required = true;
                return op;
            });

        return app;
    }
}
using Application.PlugTypes.Commands.AddPlugType;
using Application.PlugTypes.Commands.DeletePlugType;
using Application.PlugTypes.Commands.UpdateplugType;
using Application.PlugTypes.Queries;
using Cable.Requests.ChargingPoints;
using Cable.Requests.PlugTypes;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class PlugTypesRoutes
{
    public static IEndpointRouteBuilder MapPlugTypesRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/plug-types")
            .WithTags("Plug Types")
            .MapRoutes();

        return app;
    }
    
    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/GetAllPlugTypes", async (IMediator mediator, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new GetAllPlugTypesRequest(), cancellation)))
            .Produces<List<GetAllPlugTypesDto>>()
            .WithName("Get all plug types")
            .WithSummary(" Get all plug types of the application")
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .RequireAuthorization()
            .ProducesInternalServerError()
            .WithOpenApi();

        app.MapPost("/AddPlugType", async (IMediator mediator, AddPlugTypeCommand request, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(request, cancellation)))
            .Produces<int>()
            .WithName("Add plug type")
            .WithSummary("Add a new plug type")
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .RequireAuthorization()
            .ProducesInternalServerError()
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the plug type";
                return op;
            });

        app.MapPut("/UpdatePlugType/{id}", async (IMediator mediator,[FromRoute] int id ,UpdatePlugTypeRequest request, CancellationToken cancellation) =>
               await mediator.Send(new UpdatePlugTypeCommand(id,request.Name,request.SerialNumber), cancellation))
            .Produces<int>()
            .WithName("Update plug type")
            .WithSummary("Update a plug type")
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .RequireAuthorization()
            .ProducesInternalServerError()
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the plug type";
                return op;
            });
        
        app.MapDelete("DeletePlugType/{id}", async (IMediator mediator, [FromRoute] int id, CancellationToken cancellation) =>
                await mediator.Send(new DeletePlugTypeCommand(id), cancellation))
            .Produces<int>()
            .WithName("Delete plug type")
            .WithSummary("Delete a plug type")
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .RequireAuthorization()
            .ProducesInternalServerError()
            .WithOpenApi(op =>
            {
                op.Responses["200"].Description = "The id of the plug type";
                return op;
            });
        
        return app;
    }
}
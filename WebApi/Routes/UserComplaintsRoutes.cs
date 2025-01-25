using Application.UserComplaints.Command.AddUserComplaint;
using Application.UserComplaints.Command.DeleteUserComplaint;
using Application.UserComplaints.Command.UpdateUserComplaint;
using Application.UserComplaints.Queries.GetAllComplaintsByChargingPointId;
using Application.UserComplaints.Queries.GetAllUserComplaints;
using Cable.Requests.UserComplaints;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class UserComplaintsRoutes
{
    public static IEndpointRouteBuilder MapUserComplaintsRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/usercomplaints")
            .WithTags("User Complaints")
            .MapRoutes();
        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/GetAllUserComplaints",
                async (IMediator mediator, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetAllUserComplaintsRequest(), cancellationToken)))
            .Produces<List<GetUserComplaintsDto>>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithSummary("Get all user complaints")
            .WithName("Get all user complaints")
            .WithOpenApi();

        app.MapGet("/GetUserComplaintsById/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new GetAllComplaintsByChargingPointIdRequest(id), cancellationToken)))
            .Produces<List<GetUserComplaintsDto>>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithSummary("Get user complaints by id")
            .WithName("Get user complaints by id")
            .WithOpenApi();

        app.MapPost("/AddUserComplaint",
                async (IMediator mediator, [FromBody] AddUserComplaintCommand command,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(command, cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithSummary("Add user complaint")
            .WithName("Add user complaint")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the user complaint";
                return op;
            });

        app.MapPatch("/UpdateUserComplaint/{id:int}",
                async (IMediator mediator, int id, [FromBody] UpdateUserComplaintRequest request,
                        CancellationToken cancellationToken) =>
                    await mediator.Send(new UpdateUserComplaintCommand(id, request.Note), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithSummary("Update user complaint")
            .WithName("Update user complaint")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the user complaint";
                return op;
            });

        app.MapDelete("/DeleteUserComplaint/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                await mediator.Send(new DeleteUserComplaintCommand(id), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithSummary("Delete user complaint")
            .WithName("Delete user complaint")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                return op;
            });
        return app;
    }
}
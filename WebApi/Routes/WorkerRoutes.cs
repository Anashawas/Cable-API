using Application.Workers.Commands.CreateWorker;
using Application.Workers.Commands.DeleteWorker;
using Application.Workers.Commands.SetWorkerActive;
using Application.Workers.Commands.SetWorkerPrivileges;
using Cable.Core.Constants;
using Application.Workers.Queries.GetWorkerByProvider;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class WorkerRoutes
{
    public record WorkerPrivilegesRequest(List<string> Privileges);

    public static IEndpointRouteBuilder MapWorkerRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/workers")
            .WithTags("Workers")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        // Get the worker assigned to a provider (owner / provider app).
        app.MapGet("/", async (
                IMediator mediator,
                [FromQuery] string providerType,
                [FromQuery] int    providerId,
                CancellationToken  cancellationToken) =>
                Results.Ok(await mediator.Send(
                    new GetWorkerByProviderRequest(providerType, providerId),
                    cancellationToken)))
            .Produces<WorkerDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get Worker By Provider")
            .WithSummary("Get the worker assigned to a ServiceProvider or ChargingPoint")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Description = "ProviderType: 'ServiceProvider' or 'ChargingPoint'";
                op.Parameters[1].Description = "Id of the ServiceProvider or ChargingPoint";
                return op;
            });

        // Owner creates + assigns a worker account.
        app.MapPost("/", async (
                IMediator mediator,
                CreateWorkerCommand request,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<CreateWorkerResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Create Worker")
            .WithSummary("Owner: create a worker account and assign it to a provider")
            .WithDescription("Creates a Worker-role account (email + password + phone) and links it to the provider. One worker per provider. Only the provider owner may call this.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // Owner activates / deactivates the worker.
        app.MapPatch("/{providerManagerId:int}/active", async (
                IMediator mediator,
                [FromRoute] int providerManagerId,
                [FromQuery] bool isActive,
                CancellationToken cancellationToken) =>
            {
                await mediator.Send(new SetWorkerActiveCommand(providerManagerId, isActive), cancellationToken);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Set Worker Active")
            .WithSummary("Owner: activate or deactivate the worker (also toggles login)")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Description = "The ProviderManager (worker assignment) Id";
                op.Parameters[1].Description = "true = activate, false = deactivate";
                return op;
            });

        // Owner chooses what the worker may see / do.
        app.MapPut("/{providerManagerId:int}/privileges", async (
                IMediator mediator,
                [FromRoute] int providerManagerId,
                WorkerPrivilegesRequest body,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new SetWorkerPrivilegesCommand(providerManagerId, body.Privileges ?? []), cancellationToken)))
            .Produces<WorkerDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Set Worker Privileges")
            .WithSummary("Owner: what the worker may see and do in the partner app. Keys: " + string.Join(", ", WorkerPrivileges.All) + ". Empty list = station details only; all keys = everything (including privileges added later).")
            .WithOpenApi();

        app.MapGet("/privileges", () => Results.Ok(WorkerPrivileges.All))
            .Produces<string[]>()
            .WithName("List Worker Privileges")
            .WithSummary("The privilege keys an owner can grant a worker.")
            .WithOpenApi();

        // Owner deletes the worker (frees the provider for a new one).
        app.MapDelete("/{providerManagerId:int}", async (
                IMediator mediator,
                [FromRoute] int providerManagerId,
                CancellationToken cancellationToken) =>
            {
                await mediator.Send(new DeleteWorkerCommand(providerManagerId), cancellationToken);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete Worker")
            .WithSummary("Owner: remove the worker and deactivate its account");

        return app;
    }
}

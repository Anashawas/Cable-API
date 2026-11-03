using Application.Workers.Commands.ResetWorkerTempPassword;
using Application.Workers.WorkerAuth;
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
    public record WorkerTempPasswordRequest(string? TempPassword);

    public static IEndpointRouteBuilder MapWorkerRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/workers")
            .WithTags("Workers")
            .MapRoutes()
            .MapCredentialRoutes();

        // Admin support (WORKER_TRACKER §6.4): the same two actions for Cable's team.
        app.MapGroup("/api/admin/workers")
            .WithTags("Workers (admin)")
            .MapCredentialRoutes(" (admin)");

        return app;
    }

    private static RouteGroupBuilder MapCredentialRoutes(this RouteGroupBuilder app, string nameSuffix = "")
    {
        app.MapPost("/{providerManagerId:int}/reset-temp-password", async (
                IMediator mediator,
                [FromRoute] int providerManagerId,
                [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] WorkerTempPasswordRequest? body,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new ResetWorkerTempPasswordCommand(providerManagerId, body?.TempPassword), cancellationToken)))
            .Produces<WorkerTempPasswordResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Reset Worker Temp Password" + nameSuffix)
            .WithSummary("Owner or admin: \"share the details again\" — a new temporary password (optionally your own, 8+ characters) replaces the old one, the forced change comes back, trusted devices are forgotten and the worker's partner sessions end. Returns the password once plus the ready share message.")
            .WithOpenApi();

        app.MapPost("/{providerManagerId:int}/unlock", async (
                IMediator mediator,
                [FromRoute] int providerManagerId,
                CancellationToken cancellationToken) =>
            {
                await mediator.Send(new UnlockWorkerLoginCommand(providerManagerId), cancellationToken);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Unlock Worker Login" + nameSuffix)
            .WithSummary("Owner or admin: clears the 15-minute login lock and the failed-attempt counter.")
            .WithOpenApi();

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
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("Create Worker")
            .WithSummary("Owner: add a worker (the whole wizard in one call) — name, phone, optional email, privileges, optional own temp password. Returns the temporary password ONCE with the ready share message. One account per phone: a driver keeps their account and only gets a partner credential. 409 codes: WORKER_LIMIT_REACHED, PHONE_ALREADY_A_WORKER (one station per worker), OWNER_CANNOT_BE_WORKER, ACCOUNT_NOT_ELIGIBLE.")
            .WithDescription("The worker then signs in with POST /api/provider/worker-login (phone + temporary password → OTP → forced password change).")
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

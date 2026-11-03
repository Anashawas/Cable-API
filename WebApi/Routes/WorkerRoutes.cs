using Application.Workers;
using Application.Workers.Commands.ResetWorkerTempPassword;
using Application.Workers.Commands.WorkerAssignment;
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
    public record WorkerPhoneRequest(string Phone);

    public static IEndpointRouteBuilder MapWorkerRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/workers")
            .WithTags("Workers")
            .MapRoutes()
            .MapCredentialRoutes();

        // Admin support (WORKER_TRACKER §6.4): read-only list plus the support actions for Cable's team.
        app.MapGroup("/api/admin/workers")
            .WithTags("Workers (admin)")
            .MapCredentialRoutes(" (admin)");

        return app;
    }

    private static RouteGroupBuilder MapCredentialRoutes(this RouteGroupBuilder app, string nameSuffix = "")
    {
        if (nameSuffix.Length > 0)
        {
            app.MapGet("/", async (
                    IMediator mediator,
                    [FromQuery] string providerType,
                    [FromQuery] int providerId,
                    CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetWorkersByProviderRequest(providerType, providerId), cancellationToken)))
                .Produces<List<WorkerDto>>()
                .RequireAuthorization()
                .ProducesUnAuthorized()
                .ProducesForbidden()
                .ProducesInternalServerError()
                .WithName("List Workers" + nameSuffix)
                .WithSummary("Admin: the workers of a provider with login state, status and effective privileges (read-only).")
                .WithOpenApi();
        }

        app.MapPatch("/{providerManagerId:int}/phone", async (
                IMediator mediator,
                [FromRoute] int providerManagerId,
                WorkerPhoneRequest body,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new ChangeWorkerPhoneCommand(providerManagerId, body.Phone), cancellationToken)))
            .Produces<WorkerDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesInternalServerError()
            .WithName("Change Worker Phone" + nameSuffix)
            .WithSummary("Owner or admin: change the worker's phone. 409 PHONE_IN_USE when another account has it. Ends the worker's partner sessions; the next login (on the new phone) needs an OTP, which verifies it.")
            .WithOpenApi();

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
        // The workers of a provider (owner; admins too).
        app.MapGet("/", async (
                IMediator mediator,
                [FromQuery] string providerType,
                [FromQuery] int    providerId,
                CancellationToken  cancellationToken) =>
                Results.Ok(await mediator.Send(
                    new GetWorkersByProviderRequest(providerType, providerId),
                    cancellationToken)))
            .Produces<List<WorkerDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("List Workers")
            .WithSummary("Owner: the workers of a ServiceProvider or ChargingPoint — a LIST now ([] when none), oldest first. Each row: privileges (granted), effectivePrivileges (granted ∩ plan), status active | suspended | suspendedByPlan, loginStatus pendingFirstLogin | active | locked, keepOnFree.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Description = "ProviderType: 'ServiceProvider' or 'ChargingPoint'";
                op.Parameters[1].Description = "Id of the ServiceProvider or ChargingPoint";
                return op;
            });

        // The worker's own station and effective privileges (polled by the partner app).
        app.MapGet("/me", async (IMediator mediator, CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new GetMyWorkerAssignmentRequest(), cancellationToken)))
            .Produces<WorkerSelfDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("My Worker Assignment")
            .WithSummary("Worker: my station, status (active | suspended | suspendedByPlan) and the privileges I can actually use (granted ∩ the station's plan) plus the granted ones the plan locks. 404 NOT_A_WORKER for anyone else. Poll it every minute as the app does today.")
            .WithOpenApi();

        // The wizard's step 1.
        app.MapGet("/check-phone", async (
                IMediator mediator,
                [FromQuery] string providerType,
                [FromQuery] int providerId,
                [FromQuery] string phone,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new CheckWorkerPhoneRequest(providerType, providerId, phone), cancellationToken)))
            .Produces<WorkerPhoneCheckDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Check Worker Phone")
            .WithSummary("Owner: can this phone be added here? { canAdd, reason, message } — reason WORKER_LIMIT_REACHED | PHONE_ALREADY_A_WORKER | OWNER_CANNOT_BE_WORKER | ACCOUNT_NOT_ELIGIBLE | INVALID_PHONE. Says nothing about whether the phone has a Cable account.")
            .WithOpenApi();

        // Decision 38: who stays when the plan drops to one worker.
        app.MapPut("/{providerManagerId:int}/keep-on-free", async (
                IMediator mediator,
                [FromRoute] int providerManagerId,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new SetKeepOnFreeCommand(providerManagerId), cancellationToken)))
            .Produces<List<WorkerDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesInternalServerError()
            .WithName("Keep Worker On Free")
            .WithSummary("Owner: this worker is the one who stays active when the station's plan allows only one (default: the oldest active). Applies at once and returns the list.")
            .WithOpenApi();

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
            .WithSummary("Owner: suspend or resume the worker's assignment. Suspending ends their partner-app sessions at once (next request → 401); the account itself — and the Cable app, if they are a driver — is untouched.")
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
            .WithSummary("Owner: what the worker may see and do in the partner app. Keys: " + string.Join(", ", WorkerPrivileges.All) + ". Empty list = station details only. Always stored explicitly (a key added later is never on by itself). The answer's effectivePrivileges = granted ∩ the station's plan.")
            .WithOpenApi();

        app.MapGet("/privileges", async (
                IMediator mediator,
                [FromQuery] string? providerType,
                [FromQuery] int? providerId,
                CancellationToken cancellationToken) =>
                providerType is not null && providerId is int id
                    ? Results.Ok<object>(await mediator.Send(new GetWorkerPrivilegeCatalogRequest(providerType, id), cancellationToken))
                    : Results.Ok<object>(WorkerPrivileges.All))
            .Produces<List<WorkerPrivilegeOption>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .WithName("List Worker Privileges")
            .WithSummary("With providerType + providerId (owner or admin): every key with locked and reason (needsPremium | needsConnect | needsLoyalty) for this station's plan — the owner's switches. Without them: the plain key list.")
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
            .WithSummary("Owner: remove the worker — the assignment and partner credential are removed and their partner sessions end. A driver's account is untouched; a wizard-created account is deactivated and comes back if the phone is added again.");

        return app;
    }
}

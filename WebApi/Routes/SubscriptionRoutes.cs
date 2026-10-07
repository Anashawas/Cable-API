using Application.Common.Models;
using Application.Subscriptions.Commands;
using Application.Subscriptions.Queries;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

/// <summary>
/// Payment tracking for station premium, banner runs and service-provider
/// premium. Offline money (CliQ / cash) is RECORDED here, never collected —
/// this is not a payment gateway. Every mutation is admin-only inside its
/// handler; the receipt download additionally allows the item's owner.
/// </summary>
public static class SubscriptionRoutes
{
    public static IEndpointRouteBuilder MapSubscriptionRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/subscriptions")
            .WithTags("Subscriptions & Payments")
            .MapRoutes();

        return app;
    }

    public record VoidPaymentRequest(string Reason);
    public record SwitchRequest(bool On);
    public record GraceRequest(string? GraceMode, int? GraceDays);
    public record UpdatePaymentRequest(
        decimal? Amount, Cable.Core.Enums.PaymentMethod? Method, string? Note,
        string? ReceiptImageFileName, Application.Subscriptions.PaymentRecorder.PayerInput? Payer);
    public record PayerRequest(int? UserAccountId, string? Name, string? Phone, bool HasWhatsApp, string? Email, string? Note, bool OptOut = false);

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        // ---- Payments -------------------------------------------------------

        app.MapPost("/payments", async (IMediator mediator, RecordPaymentCommand request, CancellationToken ct) =>
                Results.Ok(await mediator.Send(request, ct)))
            .Produces<RecordPaymentResult>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Record payment")
            .WithSummary("Record an offline payment (CliQ / cash) and extend the subscription")
            .WithDescription("entityType: StationPremium | Banner | ServiceProviderPremium. The period is computed from planMonths: a renewal while the current period still runs extends FROM ITS EXPIRY; otherwise from paidDate. paidDate is Jordan local time. payer: pass payerId to reuse, userAccountId to link the owner (name/phone come from the user), or name/phone for someone else — a phone match reuses the existing payer. A branded PDF receipt is generated and stored; receiptDownloadPath points at it. receiptError is set if the PDF failed but the payment was still recorded.")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPut("/payments/{id:int}", async (IMediator mediator, [FromRoute] int id, UpdatePaymentRequest body, CancellationToken ct) =>
            {
                await mediator.Send(new UpdatePaymentCommand(id, body.Amount, body.Method, body.Note, body.ReceiptImageFileName, body.Payer), ct);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Update payment")
            .WithSummary("Correct amount / method / note / payer / proof of a payment; the receipt PDF is regenerated")
            .WithDescription("Dates are not editable — they define the period the money bought. A wrong date is a void followed by a new record.")
            .WithOpenApi();

        app.MapPost("/payments/{id:int}/void", async (IMediator mediator, [FromRoute] int id, VoidPaymentRequest body, CancellationToken ct) =>
            {
                await mediator.Send(new VoidPaymentCommand(id, body.Reason), ct);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Void payment")
            .WithSummary("Void a mistaken payment (kept for audit); the subscription's expiry rolls back to the latest remaining period")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPost("/payments/{id:int}/proof", async (IMediator mediator, [FromRoute] int id, [FromForm] IFormFile file, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new UploadPaymentProofCommand(id, file), ct)))
            .DisableAntiforgery()
            .Produces<string>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Upload payment proof")
            .WithSummary("Attach the CliQ screenshot / proof image to a payment; returns its URL")
            .WithOpenApi();

        app.MapGet("/payments/{id:int}/receipt", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
            {
                var file = await mediator.Send(new GetPaymentReceiptRequest(id), ct);
                return Results.File(file.Content, "application/pdf", file.FileName);
            })
            .Produces(200, contentType: "application/pdf")
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Download payment receipt")
            .WithSummary("The generated PDF receipt — admin, or the owner of the paid-for station / provider")
            .WithOpenApi();

        // ---- Subscriptions --------------------------------------------------

        app.MapGet("/{entityType}/{entityId:int}", async (IMediator mediator, [FromRoute] string entityType, [FromRoute] int entityId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetSubscriptionRequest(entityType, entityId), ct)))
            .Produces<SubscriptionDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Get subscription")
            .WithSummary("The subscription and payment history for one item (e.g. StationPremium/179)")
            .WithDescription("status is computed: Active | ExpiringSoon (≤7 days) | InGrace (past expiry, still on) | Expired | SwitchedOff. isOn is what the consumer app effectively sees. 404 when the item has never had a payment.")
            .WithOpenApi();

        app.MapPatch("/{id:int}/switch", async (IMediator mediator, [FromRoute] int id, SwitchRequest body, CancellationToken ct) =>
            {
                await mediator.Send(new SwitchSubscriptionCommand(id, body.On), ct);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Switch subscription")
            .WithSummary("Manually switch a subscription off (ends premium / the banner run now) or back on")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPatch("/{id:int}/grace", async (IMediator mediator, [FromRoute] int id, GraceRequest body, CancellationToken ct) =>
            {
                await mediator.Send(new SetSubscriptionGraceCommand(id, body.GraceMode, body.GraceDays), ct);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Set subscription grace")
            .WithSummary("Override the post-expiry rule for one subscription: Manual, AfterDays + graceDays, or null to inherit the global default")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        // ---- Payers ---------------------------------------------------------

        app.MapGet("/payers", async (IMediator mediator, [FromQuery] string? q, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
            {
                var paged = await mediator.Send(new SearchPayersRequest(q, page, pageSize), ct);
                return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
            })
            .Produces<List<PayerDto>>().Produces<PagedResult<PayerDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Search payers")
            .WithSummary("Previous payers, searchable by name or phone (matches linked users too)")
            .WithOpenApi();

        app.MapPost("/payers", async (IMediator mediator, PayerRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new UpsertPayerCommand(null, body.UserAccountId, body.Name, body.Phone, body.HasWhatsApp, body.Email, body.Note, body.OptOut), ct)))
            .Produces<int>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Create payer")
            .WithSummary("Create a payer (or reuse the one already linked to that user / phone); returns its id")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPut("/payers/{id:int}", async (IMediator mediator, [FromRoute] int id, PayerRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new UpsertPayerCommand(id, body.UserAccountId, body.Name, body.Phone, body.HasWhatsApp, body.Email, body.Note, body.OptOut), ct)))
            .Produces<int>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Update payer")
            .WithSummary("Edit a payer. For a user-linked payer only hasWhatsApp / note / optOut apply — name, phone and email come from the user")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        // ---- Dashboard + settings ------------------------------------------

        app.MapGet("/renewals", async (IMediator mediator, [FromQuery] int? withinDays, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetRenewalsDashboardRequest(withinDays ?? 30), ct)))
            .Produces<RenewalsDashboardDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Renewals dashboard")
            .WithSummary("Expiring-soon, lapsed-but-still-on and expired subscriptions, plus 12 months of collection totals by method and plan")
            .WithOpenApi();

        app.MapGet("/settings/grace", async (IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetSubscriptionGraceSettingsRequest(), ct)))
            .Produces<SubscriptionGraceSettingsDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Get grace settings")
            .WithSummary("Global post-expiry rule. Default is Manual: nothing switches off until an admin does it")
            .WithOpenApi();

        app.MapPut("/settings/grace", async (IMediator mediator, UpdateSubscriptionGraceSettingsCommand request, CancellationToken ct) =>
            {
                await mediator.Send(request, ct);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Update grace settings")
            .WithSummary("Set the global rule: Manual, or AfterDays with graceDays — the daily job then switches lapsed subscriptions off")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        return app;
    }
}

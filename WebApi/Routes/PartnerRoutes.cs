using Application.Common.Models;
using Application.Partners.Commands.CancelPartnerTransaction;
using Application.Partners.Commands.ConfirmPartnerTransaction;
using Application.Partners.Commands.CreatePartnerAgreement;
using Application.Partners.Commands.DeactivatePartnerAgreement;
using Application.Partners.Commands.InitiatePartnerTransaction;
using Application.Partners.Commands.SetProviderCreditLimit;
using Application.Partners.Commands.UpdatePartnerAgreement;
using Application.Partners.Queries.GetActivePartners;
using Application.Partners.Queries.GetAllPartnerAgreements;
using Application.Partners.Queries.GetMyPartnerTransactionById;
using Application.Partners.Queries.GetMyPartnerTransactions;
using Application.Partners.Queries.GetPartnerById;
using Application.Partners.Queries.GetProviderBalance;
using Application.Partners.Queries.GetProviderPartnerAgreement;
using Application.Partners.Queries.GetProviderPartnerTransactionById;
using Application.Partners.Queries.GetProviderPartnerTransactions;
using Application.Partners.Queries.PreviewPartnerCode;
using Cable.Requests.Partners;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class PartnerRoutes
{
    public static IEndpointRouteBuilder MapPartnerRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/partners")
            .WithTags("Partners")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        // ==========================================
        // User Endpoints
        // ==========================================

        // Get active partners
        app.MapGet("/GetActivePartners",
                async (IMediator mediator, [FromQuery] string? providerType,
                        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetActivePartnersRequest(providerType, page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<PartnerDto>>()
            .Produces<PagedResult<PartnerDto>>()
            .ProducesInternalServerError()
            .WithName("Get Active Partners")
            .WithSummary("Get all active partner locations")
            .WithDescription("Returns all active partner agreements. Optional filter by provider type.")
            .WithOpenApi();

        // Get partner by ID
        app.MapGet("/GetPartnerById/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetPartnerByIdRequest(id), cancellationToken)))
            .Produces<PartnerDetailDto>()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get Partner By Id")
            .WithSummary("Get partner agreement details")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the partner agreement";
                return op;
            });

        // Preview a scanned partner QR code (read-only — shown for confirmation)
        app.MapGet("/PreviewPartnerCode",
                async (IMediator mediator, [FromQuery] string code, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new PreviewPartnerCodeRequest(code), cancellationToken)))
            .Produces<PreviewPartnerCodeResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Preview Partner Code")
            .WithSummary("Resolve a scanned partner QR code to its details, without completing it")
            .WithDescription("Call immediately after the user scans, to populate the confirmation sheet: provider name, amount, currency, commission, points to be awarded, and the seconds left before the code expires. Awards nothing and does not complete the transaction — call ScanPartnerCode once the user confirms. CanConfirm is false with a BlockReason when the user's loyalty account is blocked, so the block surfaces before they commit. Returns 404 if the code is unknown or no longer awaiting a scan, and a validation error if it has expired.")
            .WithOpenApi();

        // Scan partner QR code (user confirms → transaction completes, points awarded)
        app.MapPost("/ScanPartnerCode",
                async (IMediator mediator, [FromQuery] string code, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new ScanPartnerCodeCommand(code), cancellationToken)))
            .Produces<ScanPartnerCodeResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Scan Partner Code")
            .WithSummary("Confirm a scanned partner QR code to complete the transaction and earn points")
            .WithDescription("Called once the user confirms the details returned by PreviewPartnerCode. Validates the code, completes the transaction, updates the settlement, and awards loyalty points — all in one database transaction. Safe to call directly without a preview; the same validation runs either way.")
            .WithOpenApi();

        // Get my partner transactions
        app.MapGet("/GetMyTransactions",
                async (IMediator mediator, [FromQuery] int? status,
                        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetMyPartnerTransactionsRequest(status, page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<PartnerTransactionDto>>()
            .Produces<PagedResult<PartnerTransactionDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get My Partner Transactions")
            .WithSummary("Get current user's partner transactions")
            .WithDescription("Returns the current user's partner transaction history. Optional filter by status.")
            .WithOpenApi();

        // Get my partner transaction by ID
        app.MapGet("/GetMyTransactionById/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetMyPartnerTransactionByIdRequest(id), cancellationToken)))
            .Produces<PartnerTransactionDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get My Partner Transaction By Id")
            .WithSummary("Get a specific partner transaction by ID (user)")
            .WithDescription("Returns a single partner transaction by ID. Only returns transactions belonging to the current user.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the partner transaction";
                return op;
            });

        // ==========================================
        // Provider Endpoints
        // ==========================================

        // Create partner transaction (provider generates QR code with amount)
        app.MapPost("/provider/CreateTransaction",
                async (IMediator mediator, InitiatePartnerTransactionRequest request,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new InitiatePartnerTransactionCommand(
                        request.PartnerAgreementId, request.TransactionAmount, request.CurrencyCode
                    ), cancellationToken)))
            .Produces<InitiatePartnerTransactionResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Create Partner Transaction")
            .WithSummary("Generate a transaction code with amount (provider)")
            .WithDescription("Provider creates a transaction with the payment amount. Returns a PTR-XXXXXX code that is shown as QR for the user to scan.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // Cancel partner transaction (provider cancels before user scans)
        app.MapPost("/provider/CancelTransaction/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new CancelPartnerTransactionCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Cancel Partner Transaction")
            .WithSummary("Cancel an initiated partner transaction (provider)")
            .WithDescription("Provider cancels a transaction before the user scans it.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the transaction to cancel";
                return op;
            });

        // Get provider partner transactions
        app.MapGet("/provider/GetProviderTransactions",
                async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId,
                        [FromQuery] int? month, [FromQuery] int? year,
                        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(
                        new GetProviderPartnerTransactionsRequest(providerType, providerId, month, year, page, pageSize),
                        cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<ProviderPartnerTransactionDto>>()
            .Produces<PagedResult<ProviderPartnerTransactionDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get Provider Partner Transactions")
            .WithSummary("Get partner transactions for a specific provider")
            .WithOpenApi();

        // Get provider partner transaction by ID
        app.MapGet("/provider/GetTransactionById/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetProviderPartnerTransactionByIdRequest(id),
                        cancellationToken)))
            .Produces<ProviderPartnerTransactionDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get Provider Partner Transaction By Id")
            .WithSummary("Get a specific partner transaction by ID (provider)")
            .WithDescription("Returns a single partner transaction by ID. Only returns transactions created by the current provider staff.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the partner transaction";
                return op;
            });

        // Get provider's partnership agreement
        app.MapGet("/provider/GetMyAgreement",
                async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new GetProviderPartnerAgreementRequest(providerType, providerId), cancellationToken)))
            .Produces<ProviderPartnerAgreementDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get Provider Partner Agreement")
            .WithSummary("Get the partnership agreement for a specific provider")
            .WithOpenApi();

        // ==========================================
        // Admin Endpoints
        // ==========================================

        // Create partner agreement
        app.MapPost("/admin/CreatePartnerAgreement",
                async (IMediator mediator, CreatePartnerAgreementRequest request,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new CreatePartnerAgreementCommand(
                        request.ProviderType, request.ProviderId,
                        request.CommissionPercentage, request.PointsRewardPercentage,
                        request.PointsConversionRateId, request.CodeExpirySeconds,
                        request.MinimumTransactionAmount, request.Note
                    ), cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Create Partner Agreement")
            .WithSummary("Register a provider as a Cable partner (admin)")
            .WithDescription("Admin creates a permanent partnership with commission and points rates.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // Update partner agreement
        app.MapPut("/admin/UpdatePartnerAgreement/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdatePartnerAgreementRequest request,
                        CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new UpdatePartnerAgreementCommand(
                        id, request.CommissionPercentage, request.PointsRewardPercentage,
                        request.PointsConversionRateId, request.CodeExpirySeconds,
                        request.MinimumTransactionAmount, request.Note, request.IsActive
                    ), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Update Partner Agreement")
            .WithSummary("Update a partner agreement (admin)")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the partner agreement";
                op.RequestBody.Required = true;
                return op;
            });

        // Deactivate partner agreement
        app.MapPut("/admin/DeactivatePartnerAgreement/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new DeactivatePartnerAgreementCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Deactivate Partner Agreement")
            .WithSummary("Deactivate a partner agreement (admin)")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the partner agreement to deactivate";
                return op;
            });

        // Get all partner agreements (admin)
        app.MapGet("/admin/GetAllPartnerAgreements",
                async (IMediator mediator, [FromQuery] bool? isActive,
                        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetAllPartnerAgreementsRequest(isActive, page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<AdminPartnerAgreementDto>>()
            .Produces<PagedResult<AdminPartnerAgreementDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get All Partner Agreements")
            .WithSummary("Get all partner agreements (admin)")
            .WithDescription("Returns all partner agreements. Optional filter by active status.")
            .WithOpenApi();

        // Set provider credit limit (admin)
        app.MapPut("/admin/SetCreditLimit",
                async (IMediator mediator, SetProviderCreditLimitRequest request,
                    CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new SetProviderCreditLimitCommand(
                        request.ProviderType, request.ProviderId, request.CreditLimit
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
            .WithName("Set Provider Credit Limit")
            .WithSummary("Admin sets or updates a provider's credit limit (null = unlimited)")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // Get provider balance (admin)
        app.MapGet("/admin/GetProviderBalance",
                async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId,
                    CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetProviderBalanceRequest(providerType, providerId),
                        cancellationToken)))
            .Produces<ProviderBalanceDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get Provider Balance")
            .WithSummary("Get a provider's credit limit, current balance, and recent payments")
            .WithOpenApi();

        return app;
    }
}

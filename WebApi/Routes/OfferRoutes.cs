using Application.Offers.Commands.ApproveOffer;
using Application.Offers.Commands.CancelOfferTransaction;
using Application.Offers.Commands.ConfirmOfferTransaction;
using Application.Offers.Commands.DeactivateOffer;
using Application.Offers.Commands.InitiateOfferTransaction;
using Application.Offers.Commands.ProposeOffer;
using Application.Offers.Commands.UploadOfferImage;
using Application.Offers.Commands.RejectOffer;
using Application.Offers.Commands.UpdateOffer;
using Application.Offers.Commands.AddWalletDeposit;
using Application.Offers.Commands.UpdateSettlementStatus;
using Application.Offers.Queries.GetActiveOffers;
using Application.Offers.Queries.PreviewOfferCode;
using Application.Offers.Queries.GetBestMatchOffer;
using Application.Offers.Queries.GetMyOfferTransactions;
using Application.Offers.Queries.GetOfferById;
using Application.Offers.Queries.GetOffersForProvider;
using Application.Offers.Queries.GetPendingOffers;
using Application.Offers.Queries.GetWalletBalance;
using Application.Offers.Queries.GetWalletHistory;
using Application.Offers.Queries.GetProviderSettlement;
using Application.Offers.Queries.GetProviderSettlements;
using Application.Common.Models;
using Application.Offers.Commands.UpdateSettlementStatusBatch;
using Application.Offers.Queries.GetProviderTransactions;
using Application.Offers.Queries.GetSettlements;
using Application.Offers.Queries.GetSettlementsCsv;
using Application.Offers.Queries.GetSettlementsPaged;
using Application.Offers.Queries.GetSettlementSummary;
using Application.Offers.Queries.GetSettlementTransactions;
using Cable.Requests.Offers;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class OfferRoutes
{
    public static IEndpointRouteBuilder MapOfferRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/offers")
            .WithTags("Offers")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        // ==========================================
        // User Endpoints
        // ==========================================

        // Get active offers
        app.MapGet("/GetActiveOffers",
                async (IMediator mediator, [FromQuery] string? providerType,
                        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetActiveOffersRequest(providerType, page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<OfferDto>>()
            .Produces<PagedResult<OfferDto>>()
            .ProducesInternalServerError()
            .WithName("Get Active Offers")
            .WithSummary("Get all active approved offers")
            .WithDescription("Returns currently active and approved offers. Optional filter by provider type.")
            .WithOpenApi();

        // Best offer for a given city + points balance
        app.MapGet("/GetBestMatchOffer",
                async (IMediator mediator, [FromQuery] string city, [FromQuery] int points,
                        [FromQuery] int? alternativesLimit, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new GetBestMatchOfferRequest(city, points, alternativesLimit), cancellationToken)))
            .Produces<GetBestMatchOfferResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Get Best Match Offer")
            .WithSummary("Best offer in a city for a given points balance")
            .WithDescription("Send a city and a points balance; returns the offer that balance best covers, plus alternatives. Best match is the MOST EXPENSIVE offer the points already cover, so the user gets the most value rather than the cheapest item. If nothing is affordable yet, the nearest offer above the balance is returned with isAffordable=false and the shortfall in pointsDifference, giving the app a target to show instead of an empty screen. City accepts Arabic or English (عمّان, Amman, al zarqa) and resolves to one of the 12 canonical Jordanian cities; an unrecognised city returns an empty result with resolvedCity=null rather than an error. Covers both charging points and service providers.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "City name, Arabic or English (e.g. Amman, عمّان)";
                op.Parameters[1].Required = true;
                op.Parameters[1].Description = "The user's current points balance";
                op.Parameters[2].Description = "Max alternatives to return (default 5)";
                return op;
            });

        // Get offer by ID
        app.MapGet("/GetOfferById/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetOfferByIdRequest(id), cancellationToken)))
            .Produces<OfferDto>()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get Offer By Id")
            .WithSummary("Get an offer by ID")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the offer";
                return op;
            });

        // Preview a scanned offer QR code (read-only — shown for confirmation)
        app.MapGet("/PreviewOfferCode",
                async (IMediator mediator, [FromQuery] string code, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new PreviewOfferCodeRequest(code), cancellationToken)))
            .Produces<PreviewOfferCodeResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Preview Offer Code")
            .WithSummary("Resolve a scanned offer QR code to its details, without redeeming it")
            .WithDescription("Call immediately after the user scans, to populate the confirmation sheet: offer title, image, provider name, the points it will cost, the monetary value, the user's current balance, and the seconds left before the code expires. Deducts nothing and completes nothing - call ScanOfferCode once the user confirms. CanConfirm is false with a BlockReason when the user cannot complete it: loyalty account blocked, not enough points (pointsShortfall says how many are missing), or the per-user usage limit for this offer already reached. Returns 404 if the code is unknown or no longer awaiting a scan, and a validation error if it has expired.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The offer code from QR scan (e.g., CBL-7X9K2M)";
                return op;
            });

        // Scan offer QR code (user scans to redeem offer and spend points)
        app.MapPost("/ScanOfferCode",
                async (IMediator mediator, [FromQuery] string code, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new ScanOfferCodeCommand(code), cancellationToken)))
            .Produces<ScanOfferCodeResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Scan Offer Code")
            .WithSummary("Scan an offer QR code to redeem offer and spend points")
            .WithDescription("User scans the QR code shown by the provider. Deducts loyalty points from user's wallet and completes the transaction.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The offer code from QR scan (e.g., CBL-7X9K2M)";
                return op;
            });

        // Get my transactions
        app.MapGet("/GetMyTransactions",
                async (IMediator mediator, [FromQuery] int? status,
                        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetMyOfferTransactionsRequest(status, page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<OfferTransactionDto>>()
            .Produces<PagedResult<OfferTransactionDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get My Offer Transactions")
            .WithSummary("Get current user's offer transactions")
            .WithDescription("Returns the current user's offer transaction history. Optional filter by status.")
            .WithOpenApi();

        // ==========================================
        // Provider Endpoints
        // ==========================================

        // Propose offer
        app.MapPost("/ProposeOffer",
                async (IMediator mediator, ProposeOfferRequest request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new ProposeOfferCommand(
                        request.Title, request.TitleAr, request.Description, request.DescriptionAr,
                        request.ProviderType, request.ProviderId,
                        request.PointsCost, request.MonetaryValue, request.CurrencyCode,
                        request.MaxUsesPerUser, request.MaxTotalUses, request.OfferCodeExpirySeconds,
                        request.ImageUrl, request.ValidFrom, request.ValidTo, request.PointsPriceValue
                    ), cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Propose Offer")
            .WithSummary("Propose a new offer (requires admin approval)")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // Upload offer image
        app.MapPost("UploadOfferImage/{id:int}",
                async (IMediator mediator, [FromForm] IFormFile file, [FromRoute] int id,
                        CancellationToken cancellationToken) =>
                    await mediator.Send(new UploadOfferImageCommand(file, id), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesInternalServerError()
            .WithName("Upload offer image")
            .WithSummary("Upload or replace offer image")
            .WithOpenApi()
            .DisableAntiforgery();

        // Get offers for my provider
        app.MapGet("/GetOffersForProvider",
                async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId,
                        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetOffersForProviderRequest(providerType, providerId, page, pageSize),
                        cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<OfferDto>>()
            .Produces<PagedResult<OfferDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get Offers For Provider")
            .WithSummary("Get all offers for a specific provider")
            .WithOpenApi();

        // Provider creates transaction (generates QR code for offer redemption)
        app.MapPost("/provider/CreateTransaction",
                async (IMediator mediator, InitiateOfferTransactionRequest request,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new InitiateOfferTransactionCommand(
                        request.OfferId
                    ), cancellationToken)))
            .Produces<InitiateOfferTransactionResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Create Offer Transaction")
            .WithSummary("Provider creates a transaction and generates an offer code")
            .WithDescription("Provider staff selects the offer. System generates a CBL code for the customer to scan and redeem using their points.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // Provider cancels transaction before user scans
        app.MapPost("/provider/CancelTransaction/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new CancelOfferTransactionCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Cancel Offer Transaction")
            .WithSummary("Provider cancels an initiated offer transaction before user scans")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the transaction to cancel";
                return op;
            });

        // Get provider transactions
        app.MapGet("/GetProviderTransactions",
                async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId,
                        [FromQuery] int? month, [FromQuery] int? year,
                        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(
                        new GetProviderTransactionsRequest(providerType, providerId, month, year, page, pageSize),
                        cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<OfferTransactionDto>>()
            .Produces<PagedResult<OfferTransactionDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get Provider Transactions")
            .WithSummary("Get transactions for a specific provider")
            .WithOpenApi();

        // Get provider settlement
        app.MapGet("/GetProviderSettlement",
                async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId,
                        [FromQuery] int year, [FromQuery] int month,
                        [FromQuery] int periodType, [FromQuery] int week,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new GetProviderSettlementRequest(providerType, providerId, year, month, periodType, week),
                        cancellationToken)))
            .Produces<ProviderSettlementDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get Provider Settlement")
            .WithSummary("Get settlement details for a provider in a specific period")
            .WithDescription("PeriodType: 1=Monthly, 2=Weekly. For monthly: provide year+month. For weekly: provide year+week.")
            .WithOpenApi();

        // ==========================================
        // Admin Endpoints
        // ==========================================

        // Approve offer
        app.MapPut("/ApproveOffer/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new ApproveOfferCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Approve Offer")
            .WithSummary("Approve a pending offer (admin)")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the offer to approve";
                return op;
            });

        // Reject offer
        app.MapPut("/RejectOffer/{id:int}",
                async (IMediator mediator, [FromRoute] int id, RejectOfferRequest request,
                    CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new RejectOfferCommand(id, request.Note), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Reject Offer")
            .WithSummary("Reject a pending offer (admin)")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the offer to reject";
                op.RequestBody.Required = true;
                return op;
            });

        // Update offer
        app.MapPut("/UpdateOffer/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdateOfferRequest request,
                    CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new UpdateOfferCommand(
                        id, request.Title, request.TitleAr, request.Description, request.DescriptionAr,
                        request.ProviderType, request.ProviderId,
                        request.PointsCost, request.MonetaryValue, request.CurrencyCode,
                        request.MaxUsesPerUser, request.MaxTotalUses, request.OfferCodeExpirySeconds,
                        request.ImageUrl, request.ValidFrom, request.ValidTo, request.IsActive, request.PointsPriceValue
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
            .WithName("Update Offer")
            .WithSummary("Update an existing offer (admin)")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the offer to update";
                op.RequestBody.Required = true;
                return op;
            });

        // Deactivate offer
        app.MapPut("/DeactivateOffer/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new DeactivateOfferCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Deactivate Offer")
            .WithSummary("Deactivate an offer (admin)")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the offer to deactivate";
                return op;
            });

        // Get pending offers
        app.MapGet("/GetPendingOffers",
                async (IMediator mediator, [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetPendingOffersRequest(page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<OfferDto>>()
            .Produces<PagedResult<OfferDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get Pending Offers")
            .WithSummary("Get all offers pending approval (admin)")
            .WithOpenApi();

        // Get all settlements
        app.MapGet("/GetSettlements",
                async (IMediator mediator, [FromQuery] int? status, [FromQuery] int? month,
                        [FromQuery] int? year, [FromQuery] int? periodType, [FromQuery] int? week,
                        [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                {
                    // Opt-in server-side search/paging (C6): any new param switches to
                    // the paged envelope. Legacy calls keep the plain array unchanged.
                    if (search != null || page.HasValue || pageSize.HasValue)
                        return Results.Ok(await mediator.Send(new GetSettlementsPagedRequest(
                            status, month, year, periodType, week, search, page, pageSize), cancellationToken));

                    return Results.Ok(await mediator.Send(new GetSettlementsRequest(status, month, year, periodType, week),
                        cancellationToken));
                })
            .Produces<List<ProviderSettlementDto>>()
            .Produces<PagedResult<ProviderSettlementDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get Settlements")
            .WithSummary("Get all settlements (admin)")
            .WithDescription("Filters: status, month, year, periodType (1=Monthly, 2=Weekly), week. Adding search/page/pageSize returns { items, totalCount, page, pageSize }. Rows include currentWalletBalance.")
            .WithOpenApi();

        // C4 — CSV export honoring the same filters
        app.MapGet("/GetSettlementsCsv",
                async (IMediator mediator, [FromQuery] int? status, [FromQuery] int? month,
                        [FromQuery] int? year, [FromQuery] int? periodType, [FromQuery] int? week,
                        [FromQuery] string? search, CancellationToken cancellationToken) =>
                {
                    var csv = await mediator.Send(new GetSettlementsCsvRequest(
                        status, month, year, periodType, week, search), cancellationToken);
                    return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "settlements.csv");
                })
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Export Settlements Csv")
            .WithSummary("Export settlements as CSV (admin) honoring the same filters as GetSettlements")
            .WithOpenApi();

        // C1 — the transaction line-items behind one settlement
        app.MapGet("/GetSettlementTransactions",
                async (IMediator mediator, [FromQuery] int settlementId, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetSettlementTransactionsRequest(settlementId), cancellationToken)))
            .Produces<SettlementTransactionsDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Get Settlement Transactions")
            .WithSummary("Admin: the completed offer/partner transactions behind one settlement (for verification and dispute resolution)")
            .WithOpenApi();

        // C3 — bulk settlement status update
        app.MapPut("/UpdateSettlementStatusBatch",
                async (IMediator mediator, UpdateSettlementStatusBatchCommand request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<UpdateSettlementStatusBatchResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Update Settlement Status Batch")
            .WithSummary("Admin marks multiple settlements Paid/Disputed/Pending in one call; per-id failures are reported, Paid stays locked")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // Get settlement summary
        app.MapGet("/GetSettlementSummary",
                async (IMediator mediator, [FromQuery] int? month, [FromQuery] int? year,
                        [FromQuery] int? periodType, [FromQuery] int? week,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetSettlementSummaryRequest(month, year, periodType, week),
                        cancellationToken)))
            .Produces<SettlementSummaryDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get Settlement Summary")
            .WithSummary("Get settlement summary dashboard (admin)")
            .WithDescription("Optional filters: month, year, periodType (1=Monthly, 2=Weekly), week.")
            .WithOpenApi();

        // Get settlements for a specific provider
        app.MapGet("/GetProviderSettlements",
                async (IMediator mediator,
                        [FromQuery] string providerType, [FromQuery] int providerId,
                        [FromQuery] int? status, [FromQuery] int? year, [FromQuery] int? week,
                        [FromQuery] bool unpaidOnly, [FromQuery] bool hasDebt,
                        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(
                        new GetProviderSettlementsRequest(providerType, providerId, status, year, week, unpaidOnly, hasDebt, page, pageSize),
                        cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<ProviderSettlementDto>>()
            .Produces<PagedResult<ProviderSettlementDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Get Provider Settlements")
            .WithSummary("Get all settlements for a specific provider")
            .WithDescription("Returns all settlements for a provider. Filters: status (1=Pending, 3=Paid, 4=Disputed), year, week, unpaidOnly (true = only Pending/Disputed settlements).")
            .WithOpenApi();

        // Update settlement status
        app.MapPut("/UpdateSettlementStatus/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdateSettlementStatusRequest request,
                    CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new UpdateSettlementStatusCommand(id, request.Status, request.Note),
                        cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Update Settlement Status")
            .WithSummary("Update settlement status (admin)")
            .WithDescription("Mark settlement as Paid or Disputed. When marking as Paid, full commission is deducted from WalletBalance (can go negative = debt). Cannot mark as Paid while settlement week is still active. Once Paid, settlement is locked.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the settlement";
                op.RequestBody.Required = true;
                return op;
            });

        // Add Wallet Deposit (admin records payment to provider wallet)
        app.MapPost("/AddWalletDeposit",
                async (IMediator mediator, AddWalletDepositRequest request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new AddWalletDepositCommand(
                        request.ProviderType, request.ProviderId, request.Amount,
                        request.TransactionType, request.Note), cancellationToken)))
            .Produces<AddWalletDepositResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Add Wallet Deposit")
            .WithSummary("Record provider wallet deposit (admin)")
            .WithDescription("Records a deposit, refund, or adjustment to a provider's wallet balance. TransactionType: 1=Deposit, 3=Refund, 4=Adjustment. Positive balance = credit, negative = debt.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // Get Wallet balance
        app.MapGet("/GetWalletBalance",
                async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new GetWalletBalanceRequest(providerType, providerId), cancellationToken)))
            .Produces<WalletBalanceDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get Wallet Balance")
            .WithSummary("Get provider's wallet balance")
            .WithDescription("Returns the current wallet balance (positive = credit, negative = debt), total deposited, and total deducted for a provider.")
            .WithOpenApi();

        // Get Wallet history
        app.MapGet("/GetWalletHistory",
                async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId,
                        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(
                        new GetWalletHistoryRequest(providerType, providerId, page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<WalletTransactionDto>>()
            .Produces<PagedResult<WalletTransactionDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get Wallet History")
            .WithSummary("Get provider's wallet transaction history")
            .WithDescription("Returns all wallet transactions (deposits, deductions, refunds, adjustments) for a provider.")
            .WithOpenApi();

        return app;
    }
}

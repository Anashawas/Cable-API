using Application.Offers.Commands.ApproveOffer;
using Application.Offers.Commands.CancelOfferTransaction;
using Application.Offers.Commands.ConfirmOfferTransaction;
using Application.Offers.Commands.DeactivateOffer;
using Application.Offers.Commands.GenerateSettlement;
using Application.Offers.Commands.InitiateOfferTransaction;
using Application.Offers.Commands.ProposeOffer;
using Application.Offers.Commands.RejectOffer;
using Application.Offers.Commands.UpdateOffer;
using Application.Offers.Commands.UpdateSettlementStatus;
using Application.Offers.Queries.GetActiveOffers;
using Application.Offers.Queries.GetMyOfferTransactions;
using Application.Offers.Queries.GetOfferById;
using Application.Offers.Queries.GetOffersForProvider;
using Application.Offers.Queries.GetPendingOffers;
using Application.Offers.Queries.GetProviderSettlement;
using Application.Offers.Queries.GetProviderTransactions;
using Application.Offers.Queries.GetSettlements;
using Application.Offers.Queries.GetSettlementSummary;
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
                async (IMediator mediator, [FromQuery] string? providerType, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetActiveOffersRequest(providerType), cancellationToken)))
            .Produces<List<OfferDto>>()
            .ProducesInternalServerError()
            .WithName("Get Active Offers")
            .WithSummary("Get all active approved offers")
            .WithDescription("Returns currently active and approved offers. Optional filter by provider type.")
            .WithOpenApi();

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
                async (IMediator mediator, [FromQuery] int? status, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetMyOfferTransactionsRequest(status), cancellationToken)))
            .Produces<List<OfferTransactionDto>>()
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
                        request.MaxUsesPerUser, request.MaxTotalUses, request.OfferCodeExpiryMinutes,
                        request.ImageUrl, request.ValidFrom, request.ValidTo
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

        // Get offers for my provider
        app.MapGet("/GetOffersForProvider",
                async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetOffersForProviderRequest(providerType, providerId),
                        cancellationToken)))
            .Produces<List<OfferDto>>()
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
                        [FromQuery] int? month, [FromQuery] int? year, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new GetProviderTransactionsRequest(providerType, providerId, month, year),
                        cancellationToken)))
            .Produces<List<OfferTransactionDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get Provider Transactions")
            .WithSummary("Get transactions for a specific provider")
            .WithOpenApi();

        // Get provider settlement
        app.MapGet("/GetProviderSettlement",
                async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId,
                        [FromQuery] int year, [FromQuery] int month, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new GetProviderSettlementRequest(providerType, providerId, year, month),
                        cancellationToken)))
            .Produces<ProviderSettlementDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get Provider Settlement")
            .WithSummary("Get settlement details for a provider in a specific month")
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
                        request.MaxUsesPerUser, request.MaxTotalUses, request.OfferCodeExpiryMinutes,
                        request.ImageUrl, request.ValidFrom, request.ValidTo, request.IsActive
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
                async (IMediator mediator, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetPendingOffersRequest(), cancellationToken)))
            .Produces<List<OfferDto>>()
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
                        [FromQuery] int? year, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetSettlementsRequest(status, month, year),
                        cancellationToken)))
            .Produces<List<ProviderSettlementDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get Settlements")
            .WithSummary("Get all settlements (admin)")
            .WithDescription("Returns all settlements. Optional filter by status, month, year.")
            .WithOpenApi();

        // Get settlement summary
        app.MapGet("/GetSettlementSummary",
                async (IMediator mediator, [FromQuery] int? month, [FromQuery] int? year,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetSettlementSummaryRequest(month, year),
                        cancellationToken)))
            .Produces<SettlementSummaryDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get Settlement Summary")
            .WithSummary("Get settlement summary dashboard (admin)")
            .WithOpenApi();

        // Update settlement status
        app.MapPut("/UpdateSettlementStatus/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdateSettlementStatusRequest request,
                    CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new UpdateSettlementStatusCommand(id, request.Status, request.PaidAmount,
                        request.Note), cancellationToken);
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
            .WithDescription("Mark settlement as Invoiced, Paid, or Disputed.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the settlement";
                op.RequestBody.Required = true;
                return op;
            });

        // Generate settlement
        app.MapPost("/GenerateSettlement",
                async (IMediator mediator, GenerateSettlementRequest request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GenerateSettlementCommand(request.Year, request.Month),
                        cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Generate Settlement")
            .WithSummary("Generate monthly settlements (admin)")
            .WithDescription("Generates settlement records for all providers with completed transactions in the given month.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        return app;
    }
}

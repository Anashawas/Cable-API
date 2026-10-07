using Application.Loyalty.Commands.ManageBoosts;
using Application.Loyalty.Queries.GetLoyaltyBoosts;
using Application.Loyalty.Commands.AdminAdjustPoints;
using Application.Loyalty.Commands.BlockProviderFromLoyalty;
using Application.Loyalty.Commands.BlockUserFromLoyalty;
using Application.Loyalty.Commands.CancelRedemption;
using Application.Loyalty.Commands.CreateReward;
using Application.Loyalty.Commands.CreateSeason;
using Application.Loyalty.Commands.EndSeason;
using Application.Loyalty.Commands.FulfillRedemption;
using Application.Loyalty.Commands.RedeemReward;
using Application.Loyalty.Commands.UnblockProviderFromLoyalty;
using Application.Loyalty.Commands.UnblockUserFromLoyalty;
using Application.Loyalty.Commands.UpdateReward;
using Application.Common.Models;
using Application.Loyalty;
using Application.Loyalty.Commands.BulkAwardPoints;
using Application.Loyalty.Commands.ReverseTransaction;
using Application.Loyalty.Queries.GetAllPointsTransactions;
using Application.Loyalty.Queries.GetAllRedemptions;
using Application.Loyalty.Queries.GetAllSeasons;
using Application.Loyalty.Queries.GetAllTiers;
using Application.Loyalty.Queries.GetBlockedProviders;
using Application.Loyalty.Queries.GetBlockedUsers;
using Application.Loyalty.Queries.GetFlaggedActivity;
using Application.Loyalty.Queries.GetLoyaltySummary;
using Application.Loyalty.Queries.GetProviderActivity;
using Application.Loyalty.Queries.GetRewardPerformance;
using Application.Loyalty.Queries.GetUpcomingExpiries;
using Application.Loyalty.Queries.GetAvailableRewards;
using Application.Loyalty.Queries.GetCurrentSeason;
using Application.Loyalty.Queries.GetLeaderboard;
using Application.Loyalty.Queries.GetMyLoyaltyAccount;
using Application.Loyalty.Queries.GetMyPointsHistory;
using Application.Loyalty.Queries.GetMyRedemptions;
using Application.Loyalty.Queries.GetProviderRedemptions;
using Application.Loyalty.Queries.GetRewardsForProvider;
using Application.Loyalty.Queries.GetSeasonHistory;
using Application.Loyalty.Queries.GetTransactionDetail;
using Application.Loyalty.Queries.GetUserLoyaltyAccount;
using Cable.Requests.Loyalty;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class LoyaltyRoutes
{
    public static IEndpointRouteBuilder MapLoyaltyRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/loyalty")
            .WithTags("Loyalty")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        // ==========================================
        // USER ENDPOINTS
        // ==========================================

        // Get my loyalty account (wallet + tier)
        app.MapGet("/GetMyLoyaltyAccount",
                async (IMediator mediator, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetMyLoyaltyAccountRequest(), cancellationToken)))
            .Produces<LoyaltyAccountDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get My Loyalty Account")
            .WithSummary("Get current user's loyalty account with wallet and tier info")
            .WithOpenApi();

        // Get my points history
        app.MapGet("/GetMyPointsHistory",
                async (IMediator mediator, [FromQuery] int? seasonId, [FromQuery] int? transactionType,
                        [FromQuery] int page, [FromQuery] int pageSize, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetMyPointsHistoryRequest(seasonId, transactionType,
                        page > 0 ? page : 1,
                        pageSize > 0 ? pageSize : 20), cancellationToken)))
            .Produces<List<PointsHistoryDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get My Points History")
            .WithSummary("Get paginated points transaction history. Filter: transactionType 1=Earn, 2=Redeem")
            .WithOpenApi();

        // Get available rewards
        app.MapGet("/GetAvailableRewards",
                async (IMediator mediator, [FromQuery] string? providerType,
                        [FromQuery] int? providerId, [FromQuery] int? categoryId,
                        [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetAvailableRewardsRequest(
                        providerType, providerId, categoryId, page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<RewardDto>>()
            .Produces<PagedResult<RewardDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get Available Rewards")
            .WithSummary("Get all available rewards, optionally filtered by provider or category")
            .WithOpenApi();

        // Get rewards for a specific provider
        app.MapGet("/GetRewardsForProvider/{providerType}/{providerId:int}",
                async (IMediator mediator, [FromRoute] string providerType, [FromRoute] int providerId,
                        [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetRewardsForProviderRequest(
                        providerType, providerId, page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<RewardDto>>()
            .Produces<PagedResult<RewardDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get Rewards For Provider")
            .WithSummary("Get rewards for a specific charging point or service provider")
            .WithOpenApi();

        // Redeem a reward
        app.MapPost("/RedeemReward/{rewardId:int}",
                async (IMediator mediator, [FromRoute] int rewardId, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new RedeemRewardCommand(rewardId), cancellationToken)))
            .Produces<RedeemRewardResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Redeem Reward")
            .WithSummary("Redeem a reward using loyalty points")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the reward to redeem";
                return op;
            });

        // Get my redemptions
        app.MapGet("/GetMyRedemptions",
                async (IMediator mediator, [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetMyRedemptionsRequest(page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<RedemptionDto>>()
            .Produces<PagedResult<RedemptionDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get My Redemptions")
            .WithSummary("Get current user's reward redemption history")
            .WithOpenApi();

        // Get current season
        app.MapGet("/GetCurrentSeason",
                async (IMediator mediator, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetCurrentSeasonRequest(), cancellationToken)))
            .Produces<CurrentSeasonDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get Current Season")
            .WithSummary("Get current season with user's progress and tier info")
            .WithOpenApi();

        // Get season history
        app.MapGet("/GetSeasonHistory",
                async (IMediator mediator, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetSeasonHistoryRequest(), cancellationToken)))
            .Produces<List<SeasonHistoryDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get Season History")
            .WithSummary("Get user's past seasons with final tier and bonus points")
            .WithOpenApi();

        // Get leaderboard
        app.MapGet("/GetLeaderboard",
                async (IMediator mediator, [FromQuery] int top, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetLeaderboardRequest(top > 0 ? top : 10), cancellationToken)))
            .Produces<List<LeaderboardEntryDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get Leaderboard")
            .WithSummary("Get season leaderboard (top users by points)")
            .WithOpenApi();

        // ==========================================
        // ADMIN ENDPOINTS
        // ==========================================

        // Create season
        app.MapPost("/admin/CreateSeason",
                async (IMediator mediator, CreateSeasonRequest request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new CreateSeasonCommand(
                        request.Name,
                        request.Description,
                        request.StartDate,
                        request.EndDate,
                        request.ActivateImmediately
                    ), cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Create Season")
            .WithSummary("Admin creates a new loyalty season")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // End current season
        app.MapPost("/admin/EndSeason",
                async (IMediator mediator, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new EndSeasonCommand(), cancellationToken)))
            .Produces<EndSeasonResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("End Season")
            .WithSummary("Admin ends the current season and awards tier bonuses")
            .WithOpenApi();

        // Admin adjust points
        app.MapPost("/admin/AdjustPoints",
                async (IMediator mediator, AdminAdjustPointsRequest request, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new AdminAdjustPointsCommand(
                        request.UserId,
                        request.Points,
                        request.Note,
                        request.ReasonCode
                    ), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Admin Adjust Points")
            .WithSummary("Admin manually adjusts a user's loyalty points")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // Block user from loyalty
        app.MapPost("/admin/BlockUser",
                async (IMediator mediator, BlockUserFromLoyaltyRequest request,
                    CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new BlockUserFromLoyaltyCommand(
                        request.UserId,
                        request.Reason,
                        request.BlockUntil
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
            .WithName("Block User From Loyalty")
            .WithSummary("Admin blocks a user from earning/redeeming loyalty points")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // Unblock user from loyalty
        app.MapPost("/admin/UnblockUser/{userId:int}",
                async (IMediator mediator, [FromRoute] int userId, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new UnblockUserFromLoyaltyCommand(userId), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Unblock User From Loyalty")
            .WithSummary("Admin unblocks a user from the loyalty system")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the user to unblock";
                return op;
            });

        // Block provider from loyalty
        app.MapPost("/admin/BlockProvider",
                async (IMediator mediator, BlockProviderFromLoyaltyRequest request,
                    CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new BlockProviderFromLoyaltyCommand(
                        request.ProviderType,
                        request.ProviderId,
                        request.Reason,
                        request.BlockUntil
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
            .WithName("Block Provider From Loyalty")
            .WithSummary("Admin blocks a provider from creating loyalty transactions")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // Unblock provider from loyalty
        app.MapPost("/admin/UnblockProvider/{providerType}/{providerId:int}",
                async (IMediator mediator, [FromRoute] string providerType, [FromRoute] int providerId,
                    CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new UnblockProviderFromLoyaltyCommand(providerType, providerId),
                        cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Unblock Provider From Loyalty")
            .WithSummary("Admin unblocks a provider from the loyalty system")
            .WithOpenApi();

        // Create reward
        app.MapPost("/admin/CreateReward",
                async (IMediator mediator, CreateRewardRequest request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new CreateRewardCommand(
                        request.Name,
                        request.Description,
                        request.PointsCost,
                        request.RewardType,
                        request.RewardValue,
                        request.ProviderType,
                        request.ProviderId,
                        request.ServiceCategoryId,
                        request.MaxRedemptions,
                        request.ImageUrl,
                        request.ValidFrom,
                        request.ValidTo
                    ), cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Create Reward")
            .WithSummary("Admin creates a new loyalty reward with optional provider link")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // Update reward
        app.MapPut("/admin/UpdateReward/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdateRewardRequest request,
                    CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new UpdateRewardCommand(
                        id,
                        request.Name,
                        request.Description,
                        request.PointsCost,
                        request.RewardType,
                        request.RewardValue,
                        request.ProviderType,
                        request.ProviderId,
                        request.ServiceCategoryId,
                        request.MaxRedemptions,
                        request.ImageUrl,
                        request.IsActive,
                        request.ValidFrom,
                        request.ValidTo
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
            .WithName("Update Reward")
            .WithSummary("Admin updates an existing loyalty reward")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the reward to update";
                op.RequestBody.Required = true;
                return op;
            });

        // Fulfill redemption
        app.MapPatch("/admin/FulfillRedemption/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new FulfillRedemptionCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Fulfill Redemption")
            .WithSummary("Admin/Owner marks a reward redemption as fulfilled")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the redemption to fulfill";
                return op;
            });

        // Cancel redemption
        app.MapPatch("/admin/CancelRedemption/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new CancelRedemptionCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Cancel Redemption")
            .WithSummary("Admin cancels a redemption and refunds points")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the redemption to cancel";
                return op;
            });

        // Get provider redemptions (admin)
        app.MapGet("/admin/GetProviderRedemptions",
                async (IMediator mediator, [FromQuery] string? providerType,
                        [FromQuery] int? providerId, [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetProviderRedemptionsRequest(
                        providerType, providerId, page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<ProviderRedemptionDto>>()
            .Produces<PagedResult<ProviderRedemptionDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get Provider Redemptions")
            .WithSummary("Admin/Owner views redemptions at a specific provider")
            .WithOpenApi();

        // ==========================================
        // ADMIN VISIBILITY ENDPOINTS (Cable-Admin portal)
        // ==========================================

        // A1 — Full loyalty account snapshot for any user
        app.MapGet("/admin/GetUserLoyaltyAccount/{userId:int}",
                async (IMediator mediator, [FromRoute] int userId, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetUserLoyaltyAccountRequest(userId), cancellationToken)))
            .Produces<LoyaltyAccountDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Admin Get User Loyalty Account")
            .WithSummary("Admin views the full loyalty account of any user")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the user";
                return op;
            });

        // A2 — Full points ledger for any user (paged)
        app.MapGet("/admin/GetUserPointsHistory/{userId:int}",
                async (IMediator mediator, [FromRoute] int userId,
                        [FromQuery] int? transactionType, [FromQuery] int? seasonId,
                        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
                        [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetAllPointsTransactionsRequest(
                        transactionType, userId, seasonId, null, null, from, to,
                        page ?? 1, pageSize ?? 20), cancellationToken)))
            .Produces<PagedResult<AdminPointsHistoryDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Admin Get User Points History")
            .WithSummary("Admin views the full points ledger of any user (paged; filters: transactionType, seasonId, from, to)")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the user";
                return op;
            });

        // C1 — Global points feed across all users (paged)
        app.MapGet("/admin/GetAllPointsTransactions",
                async (IMediator mediator,
                        [FromQuery] int? transactionType, [FromQuery] int? userId,
                        [FromQuery] int? seasonId, [FromQuery] string? providerType,
                        [FromQuery] int? providerId,
                        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
                        [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetAllPointsTransactionsRequest(
                        transactionType, userId, seasonId, providerType, providerId, from, to,
                        page ?? 1, pageSize ?? 20), cancellationToken)))
            .Produces<PagedResult<AdminPointsHistoryDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Admin Get All Points Transactions")
            .WithSummary("Admin global loyalty ledger across all users (paged; filters: transactionType, userId, seasonId, providerType, providerId, from, to)")
            .WithOpenApi();

        // B3 — Full detail of any single transaction (Offer | Partner | Redemption)
        app.MapGet("/admin/GetTransactionDetail",
                async (IMediator mediator, [FromQuery] string activityType, [FromQuery] int id,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetTransactionDetailRequest(activityType, id), cancellationToken)))
            .Produces<AdminTransactionDetailDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Admin Get Transaction Detail")
            .WithSummary("Admin opens any single transaction (activityType: Offer | Partner | Redemption) with user, provider and actor details")
            .WithOpenApi();

        // G — List all seasons (the portal's missing GetAllSeasons)
        app.MapGet("/admin/GetAllSeasons",
                async (IMediator mediator, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetAllSeasonsRequest(), cancellationToken)))
            .Produces<List<AdminSeasonDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Admin Get All Seasons")
            .WithSummary("Admin lists all loyalty seasons")
            .WithOpenApi();

        // B1 — unified activity feed at one provider (Offer | Partner | Redemption)
        app.MapGet("/admin/GetProviderActivity",
                async (IMediator mediator,
                        [FromQuery] string providerType, [FromQuery] int providerId,
                        [FromQuery] string? activityType,
                        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
                        [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetProviderActivityRequest(
                        providerType, providerId, activityType, from, to,
                        page ?? 1, pageSize ?? 20), cancellationToken)))
            .Produces<PagedResult<ProviderActivityDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Admin Get Provider Activity")
            .WithSummary("Admin unified activity feed at one provider — offers, partner transactions and redemptions in one paged list")
            .WithOpenApi();

        // D1 — all reward redemptions across users/providers
        app.MapGet("/admin/GetAllRedemptions",
                async (IMediator mediator,
                        [FromQuery] int? status, [FromQuery] string? providerType,
                        [FromQuery] int? providerId, [FromQuery] int? userId,
                        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
                        [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetAllRedemptionsRequest(
                        status, providerType, providerId, userId, from, to,
                        page ?? 1, pageSize ?? 20), cancellationToken)))
            .Produces<PagedResult<AdminRedemptionDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Admin Get All Redemptions")
            .WithSummary("Admin lists all reward redemptions (paged; filters: status, providerType, providerId, userId, from, to)")
            .WithOpenApi();

        // I1 — program-health dashboard (outstanding liability + KPIs)
        app.MapGet("/admin/GetLoyaltySummary",
                async (IMediator mediator,
                        [FromQuery] int? seasonId,
                        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetLoyaltySummaryRequest(seasonId, from, to), cancellationToken)))
            .Produces<LoyaltySummaryDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Admin Get Loyalty Summary")
            .WithSummary("Admin loyalty program dashboard: outstanding points liability + issued/redeemed/expired KPIs, daily series, top earners")
            .WithOpenApi();

        // H1 — tier ladder (read-only)
        app.MapGet("/admin/GetAllTiers",
                async (IMediator mediator, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetAllTiersRequest(), cancellationToken)))
            .Produces<List<TierDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Admin Get All Tiers")
            .WithSummary("Admin lists the loyalty tier ladder (thresholds, multipliers, season bonuses)")
            .WithOpenApi();

        // F1 — blocked users list
        app.MapGet("/admin/GetBlockedUsers",
                async (IMediator mediator, [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetBlockedUsersRequest(page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<BlockedUserDto>>()
            .Produces<PagedResult<BlockedUserDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Admin Get Blocked Users")
            .WithSummary("Admin lists all users currently blocked from loyalty")
            .WithOpenApi();

        // F2 — blocked providers list
        app.MapGet("/admin/GetBlockedProviders",
                async (IMediator mediator, [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetBlockedProvidersRequest(page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<BlockedProviderDto>>()
            .Produces<PagedResult<BlockedProviderDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Admin Get Blocked Providers")
            .WithSummary("Admin lists all providers currently blocked from loyalty")
            .WithOpenApi();

        // I2 — per-reward performance
        app.MapGet("/admin/GetRewardPerformance",
                async (IMediator mediator, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
                        [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetRewardPerformanceRequest(from, to, page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<RewardPerformanceDto>>()
            .Produces<PagedResult<RewardPerformanceDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Admin Get Reward Performance")
            .WithSummary("Admin per-reward performance: redemptions, points spent, remaining stock")
            .WithOpenApi();

        // H2 — upcoming point expiries
        app.MapGet("/admin/GetUpcomingExpiries",
                async (IMediator mediator, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetUpcomingExpiriesRequest(from, to), cancellationToken)))
            .Produces<UpcomingExpiriesDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Admin Get Upcoming Expiries")
            .WithSummary("Admin views points expiring in the window (default next 90 days), totalled per user")
            .WithOpenApi();

        // K2 — adjustment reason codes
        app.MapGet("/admin/GetAdjustmentReasons",
                (IMediator _) => Results.Ok(AdjustmentReasons.All))
            .Produces<List<AdjustmentReasonDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Admin Get Adjustment Reasons")
            .WithSummary("Reason codes accepted by AdjustPoints (reasonCode field)")
            .WithOpenApi();

        // L1 — fraud/abuse review queue
        app.MapGet("/admin/GetFlaggedActivity",
                async (IMediator mediator, [FromQuery] int? windowHours,
                        [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(
                        new GetFlaggedActivityRequest(windowHours ?? 24, Page: page, PageSize: pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<FlaggedActivityDto>>()
            .Produces<PagedResult<FlaggedActivityDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Admin Get Flagged Activity")
            .WithSummary("Admin review queue of suspicious loyalty activity (velocity heuristics)")
            .WithOpenApi();

        // J1 — bulk award points to a segment
        app.MapPost("/admin/BulkAwardPoints",
                async (IMediator mediator, BulkAwardPointsCommand request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<BulkAwardPointsResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Admin Bulk Award Points")
            .WithSummary("Admin awards points to a user segment (carTypeId / carModelId / city / tierId); blocked accounts are skipped")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // K1 — reverse a wrong transaction (points side, audited, idempotent)
        app.MapPost("/admin/ReverseTransaction",
                async (IMediator mediator, ReverseTransactionCommand request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<ReverseTransactionResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Admin Reverse Transaction")
            .WithSummary("Admin reverses a wrong transaction (activityType: Offer | Partner | Redemption | PointsAdjustment) — compensating audited ledger entry; provider wallets/settlements are not modified")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // ==========================================
        // BOOSTS (admin) — multiplied points campaigns
        // ==========================================

        app.MapGet("/boosts",
                // Nullable so both filters are genuinely optional: a non-nullable
                // bool is bound as REQUIRED and a bare GET /boosts 500s.
                async (IMediator mediator, [FromQuery] bool? activeOnly, [FromQuery] bool? currentOnly,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new GetLoyaltyBoostsRequest(activeOnly ?? false, currentOnly ?? false), cancellationToken)))
            .Produces<List<LoyaltyBoostDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get Loyalty Boosts")
            .WithSummary("Admin: list points-multiplier campaigns, with bonus points spent to date")
            .WithDescription("activeOnly filters on the IsActive flag; currentOnly on the date window. Each row reports BonusPointsSpent and BoostedTransactions.")
            .WithOpenApi();

        app.MapGet("/boosts/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetLoyaltyBoostByIdRequest(id), cancellationToken)))
            .Produces<LoyaltyBoostDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get Loyalty Boost By Id")
            .WithSummary("Admin: one campaign with its targeted providers and spend to date")
            .WithOpenApi();

        app.MapPost("/boosts",
                async (IMediator mediator, CreateLoyaltyBoostCommand command,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(command, cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Create Loyalty Boost")
            .WithSummary("Admin: create a points-multiplier campaign")
            .WithDescription("Multiplier > 1. StartsAt/EndsAt are UTC. DailyStartMinute/DailyEndMinute are minutes from midnight in JORDAN local time (start > end crosses midnight). DaysOfWeekMask bit 0 = Sunday.")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPut("/boosts/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdateLoyaltyBoostCommand command,
                        CancellationToken cancellationToken) =>
                {
                    await mediator.Send(command with { Id = id }, cancellationToken);
                    return Results.NoContent();
                })
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Update Loyalty Boost")
            .WithSummary("Admin: edit a campaign that has not started yet")
            .WithDescription("Rejected once StartsAt has passed — a running campaign is a record of the terms customers were given. Use deactivate to end one early.")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPut("/boosts/{id:int}/deactivate",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new DeactivateLoyaltyBoostCommand(id), cancellationToken);
                    return Results.NoContent();
                })
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Deactivate Loyalty Boost")
            .WithSummary("Admin: end a campaign immediately, including mid-flight")
            .WithOpenApi();

        return app;
    }
}

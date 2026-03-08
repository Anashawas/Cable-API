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
using Application.Loyalty.Queries.GetAvailableRewards;
using Application.Loyalty.Queries.GetCurrentSeason;
using Application.Loyalty.Queries.GetLeaderboard;
using Application.Loyalty.Queries.GetMyLoyaltyAccount;
using Application.Loyalty.Queries.GetMyPointsHistory;
using Application.Loyalty.Queries.GetMyRedemptions;
using Application.Loyalty.Queries.GetProviderRedemptions;
using Application.Loyalty.Queries.GetRewardsForProvider;
using Application.Loyalty.Queries.GetSeasonHistory;
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
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetAvailableRewardsRequest(
                        providerType, providerId, categoryId), cancellationToken)))
            .Produces<List<RewardDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get Available Rewards")
            .WithSummary("Get all available rewards, optionally filtered by provider or category")
            .WithOpenApi();

        // Get rewards for a specific provider
        app.MapGet("/GetRewardsForProvider/{providerType}/{providerId:int}",
                async (IMediator mediator, [FromRoute] string providerType, [FromRoute] int providerId,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetRewardsForProviderRequest(
                        providerType, providerId), cancellationToken)))
            .Produces<List<RewardDto>>()
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
                async (IMediator mediator, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetMyRedemptionsRequest(), cancellationToken)))
            .Produces<List<RedemptionDto>>()
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
                        request.Note
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
                        [FromQuery] int? providerId, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetProviderRedemptionsRequest(
                        providerType, providerId), cancellationToken)))
            .Produces<List<ProviderRedemptionDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get Provider Redemptions")
            .WithSummary("Admin/Owner views redemptions at a specific provider")
            .WithOpenApi();

        return app;
    }
}

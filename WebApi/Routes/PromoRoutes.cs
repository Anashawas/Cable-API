using Application.Common.Models;
using Application.Promos;
using Application.Promos.Commands.ClaimPromo;
using Application.Promos.Commands.ManagePromos;
using Application.Promos.Queries.GetMyPromos;
using Application.Promos.Queries.PromoAdmin;
using Cable.Core.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Cable.Routes;

/// <summary>Promo codes — "Add code", "My codes" and the admin campaigns (Docs/Sprint5-GiftsAndPromos.md §2).
/// Applying a promo to a scan lives on PartnerRoutes (PreviewPartnerCode / ScanPartnerCode with userPromoId).</summary>
public static class PromoRoutes
{
    public record ClaimBody(string Code);
    public record SetActiveBody(bool IsActive);
    public record GenerateCodesBody(int Count);
    public record AssignBody(PromoAssignTarget Target, List<string>? Cities, List<int>? UserIds, List<string>? Phones);
    public record RevokeBody(string? Reason);
    public record CreatedBody(int Id);

    public static IEndpointRouteBuilder MapPromoRoutes(this IEndpointRouteBuilder app)
    {
        // ------------------------------------------------------------------ user app
        var user = app.MapGroup("/api/promos").WithTags("Promos");

        user.MapPost("/claim", async (IMediator mediator, ClaimBody body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new ClaimPromoCommand(body.Code), ct)))
            .Produces<MyPromoDto>().RequireAuthorization().ProducesUnAuthorized().ProducesValidationProblem()
            .ProducesCableValidationProblem(StatusCodes.Status422UnprocessableEntity).ProducesInternalServerError()
            .WithName("Claim promo code")
            .WithSummary("\"Add code\": adds the promo to My codes. Case-insensitive, spaces and dashes ignored. 422 code: PROMO_NOT_FOUND | PROMO_EXPIRED | PROMO_ALREADY_CLAIMED | PROMO_LIMIT_REACHED | PROMO_NOT_ELIGIBLE | PROMO_NOT_STARTED | PROMO_ALREADY_USED | TOO_MANY_ATTEMPTS (10 wrong codes per hour; retryAfterMinutes).")
            .WithOpenApi();

        user.MapGet("/my", async (IMediator mediator, [FromQuery] string? status, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetMyPromosRequest(RouteEnums.Parse<UserPromoStatus>(status, "status")), ct)))
            .Produces<List<MyPromoDto>>().RequireAuthorization().ProducesUnAuthorized().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Get my promos")
            .WithSummary("\"My codes\" tab: status = Available (default, soonest to expire first) | Used (with usedAt, partner, savedJod) | Expired. expiresInDays / expiringSoon come from the server.")
            .WithOpenApi();

        user.MapGet("/my/count", async (IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetMyPromosCountRequest(), ct)))
            .Produces<MyPromosCountDto>().RequireAuthorization().ProducesUnAuthorized().ProducesInternalServerError()
            .WithName("Get my promos count")
            .WithSummary("{ \"available\": n } — the dot on \"My codes\".")
            .WithOpenApi();

        // ------------------------------------------------------------------ admin portal
        var admin = app.MapGroup("/api/admin/promos").WithTags("Promos (Admin)");

        admin.MapGet("/", async (IMediator mediator, [FromQuery] bool? isActive, [FromQuery] string? search,
                    [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
                {
                    var paged = await mediator.Send(new GetPromoCampaignsRequest(isActive, search, page, pageSize), ct);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<AdminPromoCampaignDto>>().Produces<PagedResult<AdminPromoCampaignDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Admin list promo campaigns")
            .WithSummary("Admin: campaigns with claims / uses / codes and Cable's budget and spend. search matches titles and the public code. Paged envelope when page or pageSize is sent.")
            .WithOpenApi();

        admin.MapGet("/{id:int}", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetPromoCampaignByIdRequest(id), ct)))
            .Produces<AdminPromoCampaignDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Admin get promo campaign")
            .WithSummary("Admin: one campaign for the edit form (effectLocked = true after the first use).")
            .WithOpenApi();

        admin.MapPost("/", async (IMediator mediator, PromoCampaignInput body, CancellationToken ct) =>
                Results.Ok(new CreatedBody(await mediator.Send(new CreatePromoCampaignCommand(body), ct))))
            .Produces<CreatedBody>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin create promo campaign")
            .WithSummary("Admin: create a campaign. A discount with cableSharePercent < 100 must use Selected (or None) stations and providers — All is refused. A discount Cable pays part of needs cableBudgetJod (and maxDiscountJod for a percent).")
            .WithOpenApi();

        admin.MapPut("/{id:int}", async (IMediator mediator, [FromRoute] int id, PromoCampaignInput body, CancellationToken ct) =>
                {
                    await mediator.Send(new UpdatePromoCampaignCommand(id, body), ct);
                    return Results.Ok();
                })
            .Produces(200).RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin update promo campaign")
            .WithSummary("Admin: replace the campaign's fields. After the first use the effect (type, value, cap, Cable's share) can't change; the code mode can't change once codes exist; the budget can't drop below what Cable already paid.")
            .WithOpenApi();

        admin.MapPatch("/{id:int}/active", async (IMediator mediator, [FromRoute] int id, SetActiveBody body, CancellationToken ct) =>
                {
                    await mediator.Send(new SetPromoCampaignActiveCommand(id, body.IsActive), ct);
                    return Results.Ok();
                })
            .Produces(200).RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin set promo campaign active")
            .WithSummary("Admin: { \"isActive\": true | false }. A campaign whose budget is used up can't be switched on until the budget is raised.")
            .WithOpenApi();

        admin.MapPost("/{id:int}/codes", async (IMediator mediator, [FromRoute] int id, GenerateCodesBody body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GeneratePromoCodesCommand(id, body.Count), ct)))
            .Produces<GeneratePromoCodesResult>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound()
            .ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin generate promo codes")
            .WithSummary("Admin (Unique mode): { \"count\": 1–5000 } unique PRM- + 8-character codes, random and non-sequential, without 0 O 1 I L.")
            .WithOpenApi();

        admin.MapGet("/{id:int}/codes/export", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
                {
                    var csv = await mediator.Send(new ExportPromoCodesCsvRequest(id), ct);
                    return Results.File(csv, "text/csv; charset=utf-8", $"promo-{id}-codes.csv");
                })
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Admin export promo codes")
            .WithSummary("Admin: the campaign's unique codes as CSV (UTF-8 with BOM) — claimed by whom and when, and the promo's status.")
            .WithOpenApi();

        admin.MapPost("/{id:int}/assign", async (IMediator mediator, [FromRoute] int id, AssignBody body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new AssignPromoCommand(id, body.Target, body.Cities, body.UserIds, body.Phones), ct)))
            .Produces<AssignPromoResult>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound()
            .ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin assign promo")
            .WithSummary("Admin: put the promo in users' My codes with a push. target All | City (cities) | Users (userIds and / or phones). Users who already hold it, reached the per-user limit or (newUsersOnly) aren't new are skipped.")
            .WithOpenApi();

        admin.MapGet("/{id:int}/report", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetPromoReportRequest(id), ct)))
            .Produces<PromoReportDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Admin promo report")
            .WithSummary("Admin: claims, assigned, uses, the total discount split Cable / partners, bonus points, budget left, and the same per partner.")
            .WithOpenApi();

        var userPromos = app.MapGroup("/api/admin/user-promos").WithTags("Promos (Admin)");

        userPromos.MapGet("/", async (IMediator mediator, [FromQuery] int? userId, [FromQuery] int? campaignId,
                    [FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetAdminUserPromosRequest(userId, campaignId,
                    RouteEnums.Parse<UserPromoStatus>(status, "status"), page, pageSize), ct)))
            .Produces<PagedResult<AdminUserPromoDto>>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Admin list user promos")
            .WithSummary("Admin: users' promos (user profile → codes), filter by userId / campaignId / status, paged (default 20).")
            .WithOpenApi();

        userPromos.MapPatch("/{id:int}/revoke", async (IMediator mediator, [FromRoute] int id,
                    [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RevokeBody? body, CancellationToken ct) =>
                {
                    await mediator.Send(new RevokeUserPromoCommand(id, body?.Reason), ct);
                    return Results.Ok();
                })
            .Produces(200).RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound()
            .ProducesCableValidationProblem(StatusCodes.Status409Conflict).ProducesInternalServerError()
            .WithName("Admin revoke user promo")
            .WithSummary("Admin: take an available promo back from a user (409 PROMO_NOT_AVAILABLE when used / expired / revoked).")
            .WithOpenApi();

        return app;
    }
}

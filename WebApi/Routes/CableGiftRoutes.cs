using Application.CableGifts;
using Application.CableGifts.Commands.ManageGifts;
using Application.CableGifts.Commands.ManageOrders;
using Application.CableGifts.Commands.OrderCableGift;
using Application.CableGifts.Queries.GetCableGifts;
using Application.CableGifts.Queries.GiftAdmin;
using Application.Common.Models;
using Cable.Core;
using Cable.Core.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Cable.Routes;

/// <summary>Cable Gifts — catalogue bought with points, fulfilled by the Cable team (Docs/Sprint5-GiftsAndPromos.md §1).</summary>
public static class CableGiftRoutes
{
    public record OrderGiftBody(string? ContactPhone, string? Note);
    public record SetActiveBody(bool IsActive);
    public record FulfillBody(string? FulfillmentNote);
    public record CancelBody(string? Reason);
    public record CreatedBody(int Id);

    public static IEndpointRouteBuilder MapCableGiftRoutes(this IEndpointRouteBuilder app)
    {
        // ------------------------------------------------------------------ user app
        var user = app.MapGroup("/api/cable-gifts").WithTags("Cable Gifts");

        user.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetCableGiftsRequest(), ct)))
            .Produces<List<CableGiftDto>>().RequireAuthorization().ProducesUnAuthorized().ProducesInternalServerError()
            .WithName("Get Cable gifts")
            .WithSummary("The active gifts for the user's city, each with canOrder / cannotOrderReason (INSUFFICIENT_POINTS | OUT_OF_STOCK | LIMIT_REACHED | GIFT_EXPIRED | GIFT_NOT_AVAILABLE | LOYALTY_BLOCKED), remaining (null = unlimited), myOrdersCount, pointsMissing and the user's balance. Texts in the Accept-Language.")
            .WithOpenApi();

        user.MapGet("/my-orders", async (IMediator mediator, [FromQuery] string? status, [FromQuery] int? page,
                    [FromQuery] int? pageSize, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetMyGiftOrdersRequest(RouteEnums.Parse<GiftOrderStatus>(status, "status"), page, pageSize), ct)))
            .Produces<PagedResult<MyGiftOrderDto>>().RequireAuthorization().ProducesUnAuthorized().ProducesInternalServerError()
            .WithName("Get my gift orders")
            .WithSummary("\"My gifts\", newest first, paged (default 20). status = Pending | Fulfilled | Cancelled (optional). fulfillmentNote (note or voucher code) only once fulfilled; pointsRefunded = true for cancelled orders.")
            .WithOpenApi();

        user.MapGet("/{id:int}", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetCableGiftByIdRequest(id), ct)))
            .Produces<CableGiftDto>().RequireAuthorization().ProducesUnAuthorized().ProducesNotFound().ProducesInternalServerError()
            .WithName("Get Cable gift")
            .WithSummary("One gift with how-to-receive and balanceAfterOrder (\"your balance after buying: 140\").")
            .WithOpenApi();

        user.MapPost("/{id:int}/order", async (IMediator mediator, [FromRoute] int id,
                    [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                    [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] OrderGiftBody? body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new OrderCableGiftCommand(id, body?.ContactPhone, body?.Note, idempotencyKey), ct)))
            .Produces<GiftOrderResult>().RequireAuthorization().ProducesUnAuthorized().ProducesNotFound()
            .ProducesValidationProblem().ProducesCableValidationProblem(StatusCodes.Status422UnprocessableEntity).ProducesInternalServerError()
            .WithName("Order Cable gift")
            .WithSummary("Buy a gift with points (taken now, refunded if cancelled). Send header Idempotency-Key (a UUID per tap): a repeated key returns the same order and never deducts twice. contactPhone defaults to the account phone. 422 with code INSUFFICIENT_POINTS (pointsCost, balance, pointsMissing) | OUT_OF_STOCK | LIMIT_REACHED | GIFT_EXPIRED | GIFT_NOT_AVAILABLE | LOYALTY_BLOCKED.")
            .WithOpenApi();

        // ------------------------------------------------------------------ admin portal
        var admin = app.MapGroup("/api/admin/cable-gifts").WithTags("Cable Gifts (Admin)");

        admin.MapGet("/", async (IMediator mediator, [FromQuery] bool? isActive, [FromQuery] string? search,
                    [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
                {
                    var paged = await mediator.Send(new GetAdminCableGiftsRequest(isActive, search, page, pageSize), ct);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<AdminCableGiftDto>>().Produces<PagedResult<AdminCableGiftDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Admin list Cable gifts")
            .WithSummary("Admin: every gift (inactive too) with both languages, stock / remaining and pending / fulfilled / cancelled order counts. Paged envelope when page or pageSize is sent.")
            .WithOpenApi();

        admin.MapGet("/{id:int}", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetAdminCableGiftByIdRequest(id), ct)))
            .Produces<AdminCableGiftDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Admin get Cable gift")
            .WithSummary("Admin: one gift for the edit form.")
            .WithOpenApi();

        admin.MapPost("/", async (IMediator mediator, CableGiftInput body, CancellationToken ct) =>
                Results.Ok(new CreatedBody(await mediator.Send(new CreateCableGiftCommand(body), ct))))
            .Produces<CreatedBody>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin create Cable gift")
            .WithSummary("Admin: create a gift (names / descriptions / how-to-receive in Arabic and English, giftType Physical | ChargeCredit | Voucher | Other, pointsCost > 0, stock and perUserLimit null = unlimited, dates in Jordan time, cities empty = all). Upload the image next with POST /{id}/image.")
            .WithOpenApi();

        admin.MapPut("/{id:int}", async (IMediator mediator, [FromRoute] int id, CableGiftInput body, CancellationToken ct) =>
                {
                    await mediator.Send(new UpdateCableGiftCommand(id, body), ct);
                    return Results.Ok();
                })
            .Produces(200).RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin update Cable gift")
            .WithSummary("Admin: replace the gift's fields. The stock can't go below the pieces already ordered.")
            .WithOpenApi();

        admin.MapPatch("/{id:int}/active", async (IMediator mediator, [FromRoute] int id, SetActiveBody body, CancellationToken ct) =>
                {
                    await mediator.Send(new SetCableGiftActiveCommand(id, body.IsActive), ct);
                    return Results.Ok();
                })
            .Produces(200).RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Admin set Cable gift active")
            .WithSummary("Admin: { \"isActive\": true | false }.")
            .WithOpenApi();

        admin.MapPost("/{id:int}/image", async (IMediator mediator, [FromRoute] int id, [FromForm] IFormFile files, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new UploadCableGiftImageCommand(id, files), ct)))
            .Produces<CableGiftImageResult>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound()
            .ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin upload Cable gift image")
            .WithSummary("Admin: multipart, form key 'files', one jpg / png / webp up to 5 MB — sets imageUrl and returns it.")
            .WithOpenApi()
            .DisableAntiforgery();

        admin.MapGet("/orders", async (IMediator mediator, [FromQuery] string? status, [FromQuery] int? giftId,
                    [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? search,
                    [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetAdminGiftOrdersRequest(RouteEnums.Parse<GiftOrderStatus>(status, "status"),
                    giftId, from, to, search, page, pageSize), ct)))
            .Produces<PagedResult<AdminGiftOrderDto>>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Admin list gift orders")
            .WithSummary("Admin: orders, newest first, paged (default 20). Filters: status (Pending | Fulfilled | Cancelled), giftId, from / to (Jordan dates, to inclusive), search (order code, phone, user name). Includes the user's phone for the call.")
            .WithOpenApi();

        admin.MapGet("/orders/export", async (IMediator mediator, [FromQuery] string? status, [FromQuery] int? giftId,
                    [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? search, CancellationToken ct) =>
                {
                    var csv = await mediator.Send(new ExportGiftOrdersCsvRequest(RouteEnums.Parse<GiftOrderStatus>(status, "status"),
                        giftId, from, to, search), ct);
                    return Results.File(csv, "text/csv; charset=utf-8", $"gift-orders-{DateTime.UtcNow:yyyyMMdd}.csv");
                })
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Admin export gift orders")
            .WithSummary("Admin: the same filters as the list, as CSV (UTF-8 with BOM, Jordan times).")
            .WithOpenApi();

        admin.MapGet("/orders/pending-count", async (IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetPendingGiftOrdersCountRequest(), ct)))
            .Produces<PendingGiftOrdersCountDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Admin pending gift orders count")
            .WithSummary("Admin dashboard: { \"pendingCount\": n }.")
            .WithOpenApi();

        admin.MapPatch("/orders/{id:int}/fulfill", async (IMediator mediator, [FromRoute] int id,
                    [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] FulfillBody? body, CancellationToken ct) =>
                {
                    await mediator.Send(new FulfillGiftOrderCommand(id, body?.FulfillmentNote), ct);
                    return Results.Ok();
                })
            .Produces(200).RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound()
            .ProducesCableValidationProblem(StatusCodes.Status409Conflict).ProducesInternalServerError()
            .WithName("Admin fulfil gift order")
            .WithSummary("Admin: mark a pending order delivered; fulfillmentNote (note or voucher code) is shown to the user. Push to the user. 409 ORDER_NOT_PENDING otherwise.")
            .WithOpenApi();

        admin.MapPatch("/orders/{id:int}/cancel", async (IMediator mediator, [FromRoute] int id,
                    [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelBody? body, CancellationToken ct) =>
                {
                    await mediator.Send(new CancelGiftOrderCommand(id, body?.Reason), ct);
                    return Results.Ok();
                })
            .Produces(200).RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound()
            .ProducesCableValidationProblem(StatusCodes.Status409Conflict).ProducesInternalServerError()
            .WithName("Admin cancel gift order")
            .WithSummary("Admin: cancel a pending order — all its points go back (points-history line \"gift refund\") and the piece returns to stock. Push to the user. 409 ORDER_NOT_PENDING otherwise.")
            .WithOpenApi();

        return app;
    }
}

/// <summary>Case-insensitive enum query parameters (name or number); an unknown value is a 400.</summary>
internal static class RouteEnums
{
    public static T? Parse<T>(string? value, string name) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Enum.TryParse<T>(value.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)) return parsed;
        throw new DataValidationException(name, $"Unknown {name} '{value}'. Use one of: {string.Join(", ", Enum.GetNames<T>())}");
    }
}

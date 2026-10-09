using Application.Pricing;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

/// <summary>Time-of-use tariff (source of truth) and price alerts — PRICE_ALERTS_BE_SPEC.</summary>
public static class PricingRoutes
{
    public record PriceAlertPrefsRequest(bool Enabled, int LeadMinutes, List<string> Windows);
    public record UpdateTouTariffRequest(List<TouWindowDto> Windows, DateTime? EffectiveFrom, string? Note);
    public record QuietHoursRequest(string QuietFrom, string QuietTo);

    public static IEndpointRouteBuilder MapPricingRoutes(this IEndpointRouteBuilder app)
    {
        var pricing = app.MapGroup("/api/pricing").WithTags("Pricing");

        pricing.MapGet("/tou", async (IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetTouTariffRequest(), ct)))
            .Produces<TouTariffDto>().ProducesNotFound().ProducesInternalServerError()
            .WithName("Get time-of-use tariff")
            .WithSummary("Public: the active charging tariff (fils/kWh per window, minutes from midnight Asia/Amman; endMin > 1440 crosses midnight). Cache it and refresh on every app open; version increases on every change.")
            .WithOpenApi();

        var me = app.MapGroup("/api/users/me/price-alerts").WithTags("Pricing");

        me.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetMyPriceAlertsRequest(), ct)))
            .Produces<PriceAlertPrefsDto>().RequireAuthorization().ProducesUnAuthorized().ProducesInternalServerError()
            .WithName("Get my price alerts")
            .WithSummary("The caller's price-alert preferences; a user who never opted in gets enabled=false.")
            .WithOpenApi();

        me.MapPut("/", async (IMediator mediator, PriceAlertPrefsRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new SetMyPriceAlertsCommand(body.Enabled, body.LeadMinutes, body.Windows ?? []), ct)))
            .Produces<PriceAlertPrefsDto>().RequireAuthorization().ProducesUnAuthorized().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Set my price alerts")
            .WithSummary("leadMinutes must be 15 | 30 | 45 | 60; windows are keys from /api/pricing/tou (unknown key → 400); enabled=false keeps the selection but sends nothing.")
            .WithOpenApi();

        var admin = app.MapGroup("/api/admin/pricing").WithTags("Pricing");

        admin.MapPut("/tou", async (IMediator mediator, UpdateTouTariffRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new UpdateTouTariffCommand(body.Windows, body.EffectiveFrom, body.Note), ct)))
            .Produces<TouTariffDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Update time-of-use tariff")
            .WithSummary("Admin: replace the tariff windows (must cover exactly 24 h, unique keys, prices in fils). Creates a new version; the previous one is kept inactive. Keep the keys stable — user preferences point at them.")
            .WithOpenApi();

        admin.MapGet("/price-alerts/overview", async (IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetPriceAlertAdminOverviewRequest(), ct)))
            .Produces<PriceAlertAdminOverviewDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Price alerts admin overview")
            .WithSummary("Admin page data: active tariff + version history, quiet hours, enabled users, subscribers per window and lead, sends of the last 14 days.")
            .WithOpenApi();

        admin.MapPut("/price-alerts/quiet-hours", async (IMediator mediator, QuietHoursRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new SetPriceAlertQuietHoursCommand(body.QuietFrom, body.QuietTo), ct)))
            .Produces<PriceAlertAdminOverviewDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Set price alert quiet hours")
            .WithSummary("Admin: the span (HH:mm, Asia/Amman, may cross midnight) in which an alert is cancelled rather than sent.")
            .WithOpenApi();

        admin.MapGet("/price-alerts/preview", async (IMediator mediator, [FromQuery] DateTime? at, [FromQuery] int? lookBackMinutes, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new PreviewPriceAlertsRequest(at, lookBackMinutes ?? 10), ct)))
            .Produces<List<PriceAlertPreviewItemDto>>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Preview price alerts")
            .WithSummary("Admin, sends nothing: which alerts the job would fire at the given Jordan wall-clock time (default now), with subscriber counts, quiet-hour flag and the exact texts. Use e.g. at=2026-10-09T16:30 to check the 16:30 peak alert.")
            .WithOpenApi();

        return app;
    }
}

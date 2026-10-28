using Application.Billing;
using Application.Billing.Bundles;
using Application.Billing.Plans;
using Application.Billing.Provider;
using Application.Billing.Subscriptions;
using Application.Billing.UpgradeRequests;
using Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

/// <summary>
/// Partner billing (Sprint 5, Docs/Sprint5-PartnerBilling.md): admin-managed plans, subscription records,
/// upgrade requests, scan bundles, and the partner's "My plan". Built on the existing subscription engine
/// (/api/subscriptions) — payments, receipts, grace and switch-off are unchanged. Admin checks and the
/// owner-only rule (workers never see billing) are enforced inside the handlers.
/// </summary>
public static class BillingRoutes
{
    public static IEndpointRouteBuilder MapBillingRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/admin/plans").WithTags("Billing - Plans (admin)").MapAdminPlanRoutes();
        app.MapGroup("/api/admin/subscriptions").WithTags("Billing - Subscriptions (admin)").MapAdminSubscriptionRoutes();
        app.MapGroup("/api/admin/upgrade-requests").WithTags("Billing - Upgrade requests (admin)").MapAdminUpgradeRequestRoutes();
        app.MapGroup("/api/admin/billing-alerts").WithTags("Billing - Alerts (admin)").MapAdminAlertRoutes();
        app.MapGroup("/api/partners").WithTags("Billing - Scan bundles").MapBundleRoutes();
        app.MapGroup("/api").WithTags("Billing - Partner").MapPartnerBillingRoutes();
        return app;
    }

    public record SetActiveRequest(bool IsActive, string? Note);
    public record ReasonRequest(string Reason);
    public record UpgradeRequest(int PlanId, int PriceOptionId, AdminPaymentInput? Payment, string? Note);
    public record FulfilRequest(int? PaymentId, int? PlanSubscriptionId);
    public record PurchaseBundleRequest(int PlanId, int PriceOptionId, int? UpgradeRequestId, string? Note);
    public record BillingModelRequest(string BillingModel, decimal? MinimumTransactionAmount);
    public record TrialRequest(string ProviderType, int ProviderId, int PlanId);

    private static object Paged<T>(PagedResult<T> paged, int? page, int? pageSize) =>
        page.HasValue || pageSize.HasValue ? paged : paged.Items;

    // ---- Admin: plans ---------------------------------------------------------

    private static RouteGroupBuilder MapAdminPlanRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("", async (IMediator mediator, [FromQuery] string? category, [FromQuery] bool? includeArchived, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetAdminPlansRequest(category, includeArchived ?? false), ct)))
            .Produces<List<PlanDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Admin list plans")
            .WithSummary("Plans by category (Subscription | Loyalty) with current subscriber counts; archived ones only with includeArchived=true")
            .WithOpenApi();

        app.MapGet("/{id:int}", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetAdminPlanRequest(id), ct)))
            .Produces<PlanDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Admin get plan")
            .WithSummary("One plan with price options, points rule and subscriber count")
            .WithOpenApi();

        app.MapPost("", async (IMediator mediator, PlanInput body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new CreatePlanCommand(body), ct)))
            .Produces<PlanSaveResult>()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin create plan")
            .WithSummary("Create a plan. 409 PLAN_KEY_TAKEN; 422 INVALID_TIERS (points rule gaps / overlaps / last tier not open), UNKNOWN_FEATURE")
            .WithDescription("key is fixed after creation (lowercase, digits, _). category: Subscription (features for a period) | Loyalty (scans + points rule). priceOptions: months and/or scans; scans null on a loyalty plan = unlimited. legacyEntityTypes ties a subscription plan to the existing engine (StationPremium, ServiceProviderPremium, OcppConnect).")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPut("/{id:int}", async (IMediator mediator, [FromRoute] int id, PlanInput body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new UpdatePlanCommand(id, body, body.Note), ct)))
            .Produces<PlanSaveResult>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin update plan")
            .WithSummary("Edit a plan; every change is logged field by field. Price / rule edits apply to new subscriptions and renewals only; feature edits apply at once (affectedSubscribers)")
            .WithDescription("priceOptions with an id are updated, without an id are added, and existing ones left out are retired (isActive=false), never deleted. note goes into the change log.")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPatch("/{id:int}/active", async (IMediator mediator, [FromRoute] int id, SetActiveRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new SetPlanActiveCommand(id, body.IsActive, body.Note), ct)))
            .Produces<PlanSaveResult>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin archive plan")
            .WithSummary("Archive (isActive=false: no longer sold, existing subscribers unaffected) or reactivate. There is no delete; the free plan cannot be archived")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapGet("/{id:int}/history", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetPlanHistoryRequest(id), ct)))
            .Produces<List<PlanChangeLogDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Admin plan history")
            .WithSummary("The plan's change log: [{ field, from, to }] and [{ field: features, added, removed }]")
            .WithOpenApi();

        app.MapGet("/{id:int}/subscribers", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetPlanSubscribersRequest(id), ct)))
            .Produces<List<PlanSubscriberDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Admin plan subscribers")
            .WithSummary("Who is on this plan right now")
            .WithOpenApi();

        app.MapGet("/features", async (IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetPlanFeatureCatalogRequest(), ct)))
            .Produces<List<PlanFeatureDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Admin plan feature catalog")
            .WithSummary("The fixed feature keys (in code) with Arabic / English names")
            .WithOpenApi();

        app.MapPut("/feature-matrix", async (IMediator mediator, UpdatePlanFeatureMatrixCommand body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(body, ct)))
            .Produces<List<PlanSaveResult>>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Admin plan feature matrix")
            .WithSummary("Save the feature × plan matrix: { plans: [{ planId, features: [...] }], note }. Applies at once; logged per plan")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPost("/margin-check", async (IMediator mediator, MarginCheckCommand body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(body, ct)))
            .Produces<MarginCheckResult>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin plan margin check")
            .WithSummary("Break-even and recommended scans for a price + points rule (points cost only). amountMix manual, or providerId to use that provider's last 90 days")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPost("/model-compare", async (IMediator mediator, ModelCompareCommand body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(body, ct)))
            .Produces<ModelCompareResult>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin billing model compare")
            .WithSummary("Sales tool (§3.4): the partner's last 90 days of completed scans under the percentage model (count, amount, commission, points and their cost at Billing.PointsPerJod, Cable's margin) next to the same period simulated on a scan bundle (bundles needed, cost, price per scan, points by the plan's rule, Cable's margin) + recommendation")
            .WithDescription("{ providerType, providerId, bundlePlanId?, priceOptionId? }. bundlePlanId default: the partner's current / latest bundle plan, else the first active Loyalty plan; priceOptionId default: the partner's own option, else the one that would have cost least. Codes under the bundle minimum amount are left out of the bundle side (belowMinimumTransactions). 422 PLAN_NOT_APPLICABLE (not a Loyalty plan / none), INVALID_PRICE_OPTION.")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        return app;
    }

    // ---- Admin: subscription records ------------------------------------------

    private static RouteGroupBuilder MapAdminSubscriptionRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("", async (IMediator mediator, [FromQuery] string? providerType, [FromQuery] int? providerId, [FromQuery] int? planId,
                    [FromQuery] string? status, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
                Results.Ok(Paged(await mediator.Send(new GetAdminPlanSubscriptionsRequest(providerType, providerId, planId, status, from, to, page, pageSize), ct), page, pageSize)))
            .Produces<List<PlanSubscriptionDto>>().Produces<PagedResult<PlanSubscriptionDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Admin list plan subscriptions")
            .WithSummary("Subscription records (each purchase, renewal, trial and bundle) with computed status")
            .WithOpenApi();

        app.MapPost("", async (IMediator mediator, CreateAdminPlanSubscriptionCommand body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(body, ct)))
            .Produces<PlanSubscriptionDto>()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin create plan subscription")
            .WithSummary("New subscription / renewal / trial. With payment{…} it is recorded through the existing payment flow (receipt PDF); without it the period is complimentary")
            .WithDescription("Loyalty plans debit the provider wallet (bundlePurchase). 409 ACTIVE_SUBSCRIPTION_EXISTS (another plan of the same category is on — use upgrade), TRIAL_ALREADY_USED; 422 PLAN_ARCHIVED, TRIAL_NOT_AVAILABLE, INVALID_PRICE_OPTION.")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPost("/{id:int}/cancel", async (IMediator mediator, [FromRoute] int id, ReasonRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new CancelPlanSubscriptionCommand(id, body.Reason), ct)))
            .Produces<PlanSubscriptionDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin cancel plan subscription")
            .WithSummary("Cancel a record with a reason; what it switched on in the existing engine is switched off unless a later paid period covers it")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPost("/{id:int}/upgrade", async (IMediator mediator, [FromRoute] int id, UpgradeRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new UpgradePlanSubscriptionCommand(id, body.PlanId, body.PriceOptionId, body.Payment, body.Note), ct)))
            .Produces<PlanSubscriptionDto>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin upgrade plan subscription")
            .WithSummary("Upgrade mid-period: the old record becomes Upgraded and its unused value is credited in JOD on the new price (creditAppliedJod)")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapGet("/{id:int}/break-even", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetPlanSubscriptionBreakEvenRequest(id), ct)))
            .Produces<BreakEvenDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Admin plan subscription break-even")
            .WithSummary("Scans used vs break-even (ok < 80%, warning, over > 100%). Internal to Cable — never shown to the partner")
            .WithOpenApi();

        return app;
    }

    // ---- Admin: upgrade requests -----------------------------------------------

    private static RouteGroupBuilder MapAdminUpgradeRequestRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("", async (IMediator mediator, [FromQuery] string? status, [FromQuery] string? providerType, [FromQuery] int? providerId,
                    [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
                Results.Ok(Paged(await mediator.Send(new GetUpgradeRequestsRequest(status, providerType, providerId, page, pageSize), ct), page, pageSize)))
            .Produces<List<UpgradeRequestDto>>().Produces<PagedResult<UpgradeRequestDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Admin list upgrade requests")
            .WithSummary("Partner upgrade / renew / bundle requests, pending first. status: Pending | Fulfilled | Rejected")
            .WithOpenApi();

        app.MapGet("/pending-count", async (IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetUpgradeRequestsPendingCountRequest(), ct)))
            .Produces<UpgradeRequestsPendingCountDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Admin upgrade requests pending count")
            .WithSummary("{ pendingCount }: requests still Pending — the badge on the admin menu")
            .WithOpenApi();

        app.MapPost("/{id:int}/reject", async (IMediator mediator, [FromRoute] int id, ReasonRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new RejectUpgradeRequestCommand(id, body.Reason), ct)))
            .Produces<UpgradeRequestDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin reject upgrade request")
            .WithSummary("Reject a pending request with a reason")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPost("/{id:int}/fulfil", async (IMediator mediator, [FromRoute] int id, FulfilRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new FulfilUpgradeRequestCommand(id, body.PaymentId, body.PlanSubscriptionId), ct)))
            .Produces<UpgradeRequestDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Admin fulfil upgrade request")
            .WithSummary("Mark a request fulfilled by hand. Normally automatic: recording a payment (POST /api/subscriptions/payments) for that station / provider fulfils its pending request")
            .WithOpenApi();

        return app;
    }

    // ---- Admin: billing alerts --------------------------------------------------

    private static RouteGroupBuilder MapAdminAlertRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("", async (IMediator mediator, [FromQuery] bool? unacknowledgedOnly, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
                Results.Ok(Paged(await mediator.Send(new GetBillingAlertsRequest(unacknowledgedOnly ?? false, page, pageSize), ct), page, pageSize)))
            .Produces<List<BillingAlertDto>>().Produces<PagedResult<BillingAlertDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Admin billing alerts")
            .WithSummary("Unlimited plans over their daily soft cap or monthly break-even, and new upgrade requests. Alerts only — scanning is never stopped")
            .WithOpenApi();

        app.MapPost("/{id:int}/acknowledge", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
            {
                await mediator.Send(new AcknowledgeBillingAlertCommand(id), ct);
                return Results.Ok();
            })
            .Produces(200)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Admin acknowledge billing alert")
            .WithSummary("Mark an alert as reviewed")
            .WithOpenApi();

        return app;
    }

    // ---- Scan bundles on a partner agreement -----------------------------------

    private static RouteGroupBuilder MapBundleRoutes(this RouteGroupBuilder app)
    {
        app.MapPost("/{id:int}/bundles", async (IMediator mediator, [FromRoute] int id, PurchaseBundleRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new PurchaseBundleCommand(id, body.PlanId, body.PriceOptionId, body.UpgradeRequestId, body.Note), ct)))
            .Produces<PlanSubscriptionDto>()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Sell scan bundle")
            .WithSummary("Admin sells a loyalty plan on agreement {id}: the price is debited from the provider wallet (bundlePurchase). Renewal adds the unused scans of the previous bundle")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapGet("/{id:int}/bundles", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetAgreementBundlesRequest(id), ct)))
            .Produces<AgreementBundlesDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Get scan bundles")
            .WithSummary("Billing model, current bundle and bundle history of agreement {id} (admin, or the provider's owner)")
            .WithOpenApi();

        app.MapPatch("/{id:int}/billing-model", async (IMediator mediator, [FromRoute] int id, BillingModelRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new SetBillingModelCommand(id, body.BillingModel, body.MinimumTransactionAmount), ct)))
            .Produces<SetBillingModelResult>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Set billing model")
            .WithSummary("percentage | bundle. Codes created before the switch keep their model (the open settlement closes on percentage); the bundle starts now. A bundle needs a minimum amount (default Billing.BundleMinimumTransactionAmount = 2 JOD)")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        return app;
    }

    // ---- Partner (owner only) -----------------------------------------------------

    private static RouteGroupBuilder MapPartnerBillingRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/plans", async (IMediator mediator, [FromQuery] string? providerType, [FromQuery] string? category, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetPublicPlansRequest(providerType, category), ct)))
            .Produces<List<PublicPlanDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesInternalServerError()
            .WithName("List plans")
            .WithSummary("Visible plans for the comparison table (the apps hide prices and upgrade buttons on iOS)")
            .WithOpenApi();

        app.MapGet("/provider/subscription", async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetProviderSubscriptionRequest(providerType, providerId), ct)))
            .Produces<ProviderSubscriptionDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("My plan")
            .WithSummary("Owner only: tier, state, expiry, features[] from the server, per-type subscriptions, loyalty model / bundle, pending request, trialAvailable per plan")
            .WithOpenApi();

        app.MapGet("/provider/subscription/payments", async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetProviderPaymentsRequest(providerType, providerId), ct)))
            .Produces<List<ProviderPaymentDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("My plan payments")
            .WithSummary("Owner only: recorded payments; the PDF is GET receiptDownloadPath (/api/subscriptions/payments/{id}/receipt)")
            .WithOpenApi();

        app.MapGet("/provider/subscriptions/history", async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetProviderSubscriptionHistoryRequest(providerType, providerId), ct)))
            .Produces<List<PlanSubscriptionDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("My subscription history")
            .WithSummary("Owner only: every subscription, renewal, trial and bundle record")
            .WithOpenApi();

        app.MapPost("/provider/trials", async (IMediator mediator, TrialRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new StartProviderTrialCommand(body.ProviderType, body.ProviderId, body.PlanId), ct)))
            .Produces<PlanSubscriptionDto>()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Start free trial")
            .WithSummary("Owner only. Once per station / provider per plan: 409 TRIAL_ALREADY_USED, ACTIVE_SUBSCRIPTION_EXISTS; 422 TRIAL_NOT_AVAILABLE, PLAN_ARCHIVED")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        app.MapPost("/provider/upgrade-requests", async (IMediator mediator, CreateUpgradeRequestCommand body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(body, ct)))
            .Produces<CreateUpgradeRequestResult>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Request upgrade")
            .WithSummary("Owner only: { providerType, providerId, tier (plan key) | planId, months, priceOptionId, note } → { id, status: Pending }. 409 UPGRADE_REQUEST_PENDING while one is open")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        return app;
    }
}

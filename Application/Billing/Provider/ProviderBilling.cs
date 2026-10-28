using Application.Billing.Subscriptions;
using Application.Subscriptions;
using Application.Subscriptions.Queries;
using Cable.Core;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Billing.Provider;

public record ProviderEntitySubscriptionDto(string EntityType, string Status, bool IsOn, DateTime ExpiresAt, string? PlanKey, int SubscriptionId);

public record ProviderLoyaltyDto(
    bool Enrolled,
    /// <summary>percentage | bundle.</summary>
    string BillingModel,
    /// <summary>The owner's own agreement, so they may see it; null on the bundle model.</summary>
    double? CommissionPercentage,
    decimal? MinimumTransactionAmount,
    LoyaltyBundleDto? Bundle);

public record PendingUpgradeRequestDto(int Id, string Tier, int PlanId, string Kind, int? Months, int? PriceOptionId, DateTime CreatedAt);

/// <summary>GET /api/provider/subscription (§2.4).</summary>
public record ProviderSubscriptionDto(
    string ProviderType,
    int ProviderId,
    /// <summary>Key of the highest plan that is on: free | premium | connect | (admin-created key).</summary>
    string Tier,
    int? TierPlanId,
    string TierName,
    /// <summary>active | expiringSoon | inGrace | expired | switchedOff | none.</summary>
    string State,
    DateTime? ExpiresAt,
    int? DaysLeft,
    DateTime? GraceEndsAt,
    bool IsTrial,
    List<string> Features,
    List<ProviderEntitySubscriptionDto> Subscriptions,
    ProviderLoyaltyDto Loyalty,
    PendingUpgradeRequestDto? PendingUpgradeRequest,
    /// <summary>planKey → can the owner start a free trial of it now.</summary>
    Dictionary<string, bool> TrialAvailable);

public record GetProviderSubscriptionRequest(string ProviderType, int ProviderId) : IRequest<ProviderSubscriptionDto>;

public class GetProviderSubscriptionRequestHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetProviderSubscriptionRequest, ProviderSubscriptionDto>
{
    public async Task<ProviderSubscriptionDto> Handle(GetProviderSubscriptionRequest request, CancellationToken cancellationToken)
    {
        await BillingAccessGuard.EnsureOwnerOrAdminAsync(applicationDbContext, currentUserService, request.ProviderType, request.ProviderId, cancellationToken);
        var s = await ProviderPlanResolver.LoadAsync(applicationDbContext, request.ProviderType, request.ProviderId, cancellationToken);

        var tierPlan = s.TierPlan();
        string state = "none"; DateTime? expiresAt = null; int? daysLeft = null; DateTime? graceEnds = null; var isTrial = false;
        if (tierPlan is not null && tierPlan.Key != PlanKeys.Free)
        {
            var ps = s.PlanState(tierPlan);
            state = PlanSubscriptionState.ToPartnerState(ps.Status);
            expiresAt = ps.ExpiresAt; graceEnds = ps.GraceEndsAt; isTrial = ps.IsTrial;
        }
        else
        {
            // On the free plan: say whether a paid plan lapsed (the home banner shows for expired / switchedOff).
            var lapsed = s.Plans.Where(p => p.Category == PlanCategories.Subscription && p.Key != PlanKeys.Free)
                .Select(p => (Plan: p, State: s.PlanState(p)))
                .Where(x => x.State.ExpiresAt is not null && x.State.Status != "None")
                .MaxBy(x => x.State.ExpiresAt);
            if (lapsed.Plan is not null)
            {
                state = PlanSubscriptionState.ToPartnerState(lapsed.State.Status);
                expiresAt = lapsed.State.ExpiresAt;
            }
        }
        if (expiresAt is { } e) daysLeft = (int)Math.Floor((e - s.Now).TotalDays);

        var subscriptions = s.LegacyRows.Select(row =>
        {
            var g = SubscriptionPeriodCalculator.EffectiveGrace(row, s.Grace);
            return new ProviderEntitySubscriptionDto(row.EntityType, SubscriptionPeriodCalculator.StatusOf(row, g, s.Now),
                SubscriptionPeriodCalculator.IsOn(row, g, s.Now), row.ExpiresAt, PlanCatalog.PlanForEntityType(s.Plans, row.EntityType)?.Key, row.Id);
        }).ToList();

        var agreement = await applicationDbContext.PartnerAgreements.AsNoTracking()
            .Where(a => a.ProviderType == request.ProviderType && a.ProviderId == request.ProviderId && a.IsActive && !a.IsDeleted)
            .OrderByDescending(a => a.Id)
            .Select(a => new { a.BillingModel, a.CommissionPercentage, a.MinimumTransactionAmount })
            .FirstOrDefaultAsync(cancellationToken);
        var model = BillingModels.Normalize(agreement?.BillingModel);
        var bundle = await LoyaltyBundleService.GetDtoAsync(applicationDbContext, request.ProviderType, request.ProviderId, cancellationToken);
        var loyalty = new ProviderLoyaltyDto(agreement is not null, model,
            agreement is not null && model == BillingModels.Percentage ? agreement.CommissionPercentage : null,
            agreement?.MinimumTransactionAmount, bundle);

        var pending = await applicationDbContext.PlanUpgradeRequests.AsNoTracking()
            .Where(r => r.ProviderType == request.ProviderType && r.ProviderId == request.ProviderId && r.Status == UpgradeRequestStatuses.Pending && !r.IsDeleted)
            .OrderByDescending(r => r.Id)
            .Select(r => new PendingUpgradeRequestDto(r.Id, r.PlanKey, r.BillingPlanId, r.Kind, r.Months, r.PriceOptionId, r.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        var trials = s.Plans.Where(p => p.IsActive && p.IsVisible && p.Key != PlanKeys.Free && PlanCatalog.AppliesToProvider(p, request.ProviderType))
            .ToDictionary(p => p.Key, p => ProviderPlanResolver.TrialBlocker(s, p, model) is null);

        return new ProviderSubscriptionDto(
            request.ProviderType, request.ProviderId,
            tierPlan?.Key ?? PlanKeys.Free, tierPlan?.Id, tierPlan is null ? Lang.Pick("مجانية", "Free") : PlanCatalog.Name(tierPlan),
            state, expiresAt, daysLeft, graceEnds, isTrial,
            s.Features(), subscriptions, loyalty, pending, trials);
    }
}

// ---------------------------------------------------------------------------
// Payments (receipts come from the existing /api/subscriptions/payments/{id}/receipt)
// ---------------------------------------------------------------------------

public record ProviderPaymentDto(
    int Id, string ReferenceNo, decimal Amount, string Currency, string Method, DateTime PaidDate,
    DateTime PeriodStart, DateTime PeriodEnd, int? PlanMonths, string EntityType, string? ReceiptDownloadPath);

public record GetProviderPaymentsRequest(string ProviderType, int ProviderId) : IRequest<List<ProviderPaymentDto>>;

public class GetProviderPaymentsRequestHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetProviderPaymentsRequest, List<ProviderPaymentDto>>
{
    public async Task<List<ProviderPaymentDto>> Handle(GetProviderPaymentsRequest request, CancellationToken cancellationToken)
    {
        await BillingAccessGuard.EnsureOwnerOrAdminAsync(applicationDbContext, currentUserService, request.ProviderType, request.ProviderId, cancellationToken);
        var types = BillingProviderTypes.EntityTypesFor(request.ProviderType);

        var rows = await applicationDbContext.Payments.AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsVoid && types.Contains(p.Subscription.EntityType) && p.Subscription.EntityId == request.ProviderId)
            .OrderByDescending(p => p.PaidDate)
            .Select(p => new { p.Id, p.ReferenceNo, p.Amount, p.Currency, p.Method, p.PaidDate, p.PeriodStart, p.PeriodEnd, p.Subscription.EntityType, p.GeneratedReceiptFileName })
            .ToListAsync(cancellationToken);

        return rows.Select(p =>
        {
            var (en, ar) = PaymentRecorder.MethodLabel((PaymentMethod)p.Method);
            var months = (int)Math.Round((p.PeriodEnd - p.PeriodStart).TotalDays / 30.44);
            return new ProviderPaymentDto(p.Id, p.ReferenceNo, p.Amount, p.Currency, Lang.Pick(ar, en), p.PaidDate, p.PeriodStart, p.PeriodEnd,
                months > 0 ? months : null, p.EntityType,
                string.IsNullOrEmpty(p.GeneratedReceiptFileName) ? null : string.Format(SubscriptionDtoMapper.ReceiptPathFormat, p.Id));
        }).ToList();
    }
}

// ---------------------------------------------------------------------------
// History of subscription records
// ---------------------------------------------------------------------------

public record GetProviderSubscriptionHistoryRequest(string ProviderType, int ProviderId) : IRequest<List<PlanSubscriptionDto>>;

public class GetProviderSubscriptionHistoryRequestHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetProviderSubscriptionHistoryRequest, List<PlanSubscriptionDto>>
{
    public async Task<List<PlanSubscriptionDto>> Handle(GetProviderSubscriptionHistoryRequest request, CancellationToken cancellationToken)
    {
        await BillingAccessGuard.EnsureOwnerOrAdminAsync(applicationDbContext, currentUserService, request.ProviderType, request.ProviderId, cancellationToken);
        var rows = await applicationDbContext.PlanSubscriptions.AsNoTracking()
            .Where(r => r.ProviderType == request.ProviderType && r.ProviderId == request.ProviderId && !r.IsDeleted)
            .OrderByDescending(r => r.StartsAt).ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken);
        return await PlanSubscriptionMapper.MapAsync(applicationDbContext, rows, cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Partner starts a free trial (Android / web; the apps hide it on iOS)
// ---------------------------------------------------------------------------

public record StartProviderTrialCommand(string ProviderType, int ProviderId, int PlanId) : IRequest<PlanSubscriptionDto>;

public class StartProviderTrialCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    PlanSubscriptionService service)
    : IRequestHandler<StartProviderTrialCommand, PlanSubscriptionDto>
{
    public async Task<PlanSubscriptionDto> Handle(StartProviderTrialCommand request, CancellationToken cancellationToken)
    {
        await BillingAccessGuard.EnsureOwnerOrAdminAsync(applicationDbContext, currentUserService, request.ProviderType, request.ProviderId, cancellationToken);
        var plan = await PlanCatalog.LoadAsync(applicationDbContext, request.PlanId, cancellationToken);
        var record = plan.Category == PlanCategories.Loyalty
            ? await service.StartTrialAsync(currentUserService.UserId, request.ProviderType, request.ProviderId, plan, byPartner: true, cancellationToken)
            : await service.InTransactionAsync(() => service.StartTrialAsync(currentUserService.UserId, request.ProviderType, request.ProviderId, plan, byPartner: true, cancellationToken), cancellationToken);
        return (await PlanSubscriptionMapper.MapAsync(applicationDbContext, [record], cancellationToken))[0];
    }
}

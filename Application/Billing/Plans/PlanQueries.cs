using Application.Common.Security;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Billing.Plans;

// ---------------------------------------------------------------------------
// Admin: list / one plan
// ---------------------------------------------------------------------------

public record GetAdminPlansRequest(string? Category = null, bool IncludeArchived = false) : IRequest<List<PlanDto>>;

public class GetAdminPlansRequestHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetAdminPlansRequest, List<PlanDto>>
{
    public async Task<List<PlanDto>> Handle(GetAdminPlansRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var category = PlanCategories.Normalize(request.Category);
        var plans = (await PlanCatalog.LoadAllAsync(applicationDbContext, cancellationToken))
            .Where(p => (category == null || p.Category == category) && (request.IncludeArchived || p.IsActive))
            .OrderBy(p => p.Category).ThenBy(p => p.SortOrder).ThenBy(p => p.Id)
            .ToList();
        var counts = (await PlanSubscribers.ListAsync(applicationDbContext, plans, cancellationToken))
            .GroupBy(r => r.PlanId).ToDictionary(g => g.Key, g => g.Select(r => (r.ProviderType, r.ProviderId)).Distinct().Count());
        return plans.Select(p => PlanDtoMapper.Map(p, counts.GetValueOrDefault(p.Id))).ToList();
    }
}

public record GetAdminPlanRequest(int Id) : IRequest<PlanDto>;

public class GetAdminPlanRequestHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetAdminPlanRequest, PlanDto>
{
    public async Task<PlanDto> Handle(GetAdminPlanRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var plan = await PlanCatalog.LoadAsync(applicationDbContext, request.Id, cancellationToken);
        var count = (await PlanSubscribers.ListAsync(applicationDbContext, [plan], cancellationToken)).Count;
        return PlanDtoMapper.Map(plan, count);
    }
}

// ---------------------------------------------------------------------------
// Admin: change log / subscribers / feature catalog
// ---------------------------------------------------------------------------

public record GetPlanHistoryRequest(int Id) : IRequest<List<PlanChangeLogDto>>;

public class GetPlanHistoryRequestHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetPlanHistoryRequest, List<PlanChangeLogDto>>
{
    public async Task<List<PlanChangeLogDto>> Handle(GetPlanHistoryRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var rows = await applicationDbContext.BillingPlanChangeLogs.AsNoTracking()
            .Where(x => x.BillingPlanId == request.Id)
            .OrderByDescending(x => x.ChangedAt).ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
        var userIds = rows.Where(r => r.ChangedByUserId != null).Select(r => r.ChangedByUserId!.Value).Distinct().ToList();
        var names = await applicationDbContext.UserAccounts.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);
        return rows.Select(r => new PlanChangeLogDto(r.Id, r.BillingPlanId, r.ChangedByUserId,
            r.ChangedByUserId is int uid ? names.GetValueOrDefault(uid) : null, r.ChangedAt,
            BillingJson.Read<List<PlanChange>>(r.Changes) ?? [], r.Note)).ToList();
    }
}

public record PlanSubscriberDto(string ProviderType, int ProviderId, string? ProviderName, int? PlanSubscriptionId, int? SubscriptionId,
    string Status, DateTime? ExpiresAt, bool IsTrial, int? ScansRemaining);

public record GetPlanSubscribersRequest(int Id) : IRequest<List<PlanSubscriberDto>>;

public class GetPlanSubscribersRequestHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetPlanSubscribersRequest, List<PlanSubscriberDto>>
{
    public async Task<List<PlanSubscriberDto>> Handle(GetPlanSubscribersRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var plan = await PlanCatalog.LoadAsync(applicationDbContext, request.Id, cancellationToken);
        var rows = await PlanSubscribers.ListAsync(applicationDbContext, [plan], cancellationToken);
        var names = await ProviderNames.ResolveAsync(applicationDbContext, rows.Select(r => (r.ProviderType, r.ProviderId)), cancellationToken);
        return rows.OrderBy(r => r.ExpiresAt ?? DateTime.MaxValue)
            .Select(r => new PlanSubscriberDto(r.ProviderType, r.ProviderId, names.GetValueOrDefault((r.ProviderType, r.ProviderId)),
                r.PlanSubscriptionId, r.SubscriptionId, r.Status, r.ExpiresAt, r.IsTrial, r.ScansRemaining))
            .ToList();
    }
}

public record GetPlanFeatureCatalogRequest : IRequest<List<PlanFeatureDto>>;

public class GetPlanFeatureCatalogRequestHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetPlanFeatureCatalogRequest, List<PlanFeatureDto>>
{
    public async Task<List<PlanFeatureDto>> Handle(GetPlanFeatureCatalogRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        return PlanFeatureCatalog.All.Select(f => new PlanFeatureDto(f.Key, PlanFeatureCatalog.Name(f.Key), f.NameAr, f.NameEn)).ToList();
    }
}

// ---------------------------------------------------------------------------
// Admin: margin calculator (§2b.4b)
// ---------------------------------------------------------------------------

public record MarginCheckCommand(
    decimal PriceJod,
    int? Scans,
    int? Months,
    PointsRule PointsRule,
    List<AmountMixEntry>? AmountMix = null,
    int? ProviderId = null,
    string? ProviderType = null,
    decimal? TargetMarginPercent = null,
    /// <summary>Unlimited plans: the expected average scans per month.</summary>
    int? ExpectedMonthlyScans = null) : IRequest<MarginCheckResult>;

public class MarginCheckCommandHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<MarginCheckCommand, MarginCheckResult>
{
    public async Task<MarginCheckResult> Handle(MarginCheckCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        if (request.PriceJod < 0) throw new DataValidationException("PriceJod", Lang.Pick("السعر لا يكون سالب", "priceJod cannot be negative"));
        if (request.Scans is < 1) throw new DataValidationException("Scans", Lang.Pick("عدد المسحات يجب أن يكون أكبر من صفر", "scans must be at least 1"));
        var reason = PointsRuleValidator.Validate(request.PointsRule);
        if (reason is not null) throw new UnprocessableException(BillingErrorCodes.InvalidTiers, reason);

        var rule = PointsRuleValidator.Canonical(request.PointsRule);
        var pointsPerJod = await BillingSettingsProvider.GetPointsPerJodAsync(applicationDbContext, cancellationToken);
        var margin = request.TargetMarginPercent ?? await BillingSettingsProvider.GetTargetMarginPercentAsync(applicationDbContext, cancellationToken);

        double avg; string source; List<AmountMixEntry> mix;
        if (request.ProviderId is int pid && (request.AmountMix is null || request.AmountMix.Count == 0) && rule.Mode == PointsRule.Tiered)
        {
            var amounts = await BillingQueries.RecentAmountsAsync(applicationDbContext, request.ProviderType ?? BillingProviderTypes.ChargingPoint, pid, cancellationToken);
            if (amounts.Count > 0)
            {
                (avg, mix) = BillingMath.AveragePointsFromAmounts(rule, amounts);
                source = "provider";
            }
            else (avg, source, mix) = BillingMath.AveragePoints(rule, null);
        }
        else (avg, source, mix) = BillingMath.AveragePoints(rule, request.AmountMix);

        return BillingMath.Check(request.PriceJod, request.Scans, request.Months, avg, pointsPerJod, margin, request.ExpectedMonthlyScans, source, mix);
    }
}

// ---------------------------------------------------------------------------
// Partner: visible plans for the comparison table
// ---------------------------------------------------------------------------

public record GetPublicPlansRequest(string? ProviderType = null, string? Category = null) : IRequest<List<PublicPlanDto>>;

public class GetPublicPlansRequestHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetPublicPlansRequest, List<PublicPlanDto>>
{
    public async Task<List<PublicPlanDto>> Handle(GetPublicPlansRequest request, CancellationToken cancellationToken)
    {
        _ = currentUserService.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var category = PlanCategories.Normalize(request.Category);
        return (await PlanCatalog.LoadAllAsync(applicationDbContext, cancellationToken))
            .Where(p => p.IsActive && p.IsVisible
                        && (category == null || p.Category == category)
                        && (string.IsNullOrWhiteSpace(request.ProviderType) || PlanCatalog.AppliesToProvider(p, request.ProviderType)))
            .OrderBy(p => p.Category).ThenBy(p => p.SortOrder)
            .Select(PlanDtoMapper.MapPublic)
            .ToList();
    }
}

/// <summary>Small shared reads.</summary>
public static class BillingQueries
{
    /// <summary>Completed partner transaction amounts of the last 90 days (the margin calculator's real mix).</summary>
    public static async Task<List<decimal>> RecentAmountsAsync(IApplicationDbContext db, string providerType, int providerId, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-90);
        return await db.PartnerTransactions.AsNoTracking()
            .Where(t => t.ProviderType == providerType && t.ProviderId == providerId && !t.IsDeleted
                        && t.Status == (int)PartnerTransactionStatus.Completed && t.CompletedAt >= since && t.TransactionAmount != null)
            .Select(t => t.TransactionAmount!.Value)
            .ToListAsync(ct);
    }
}

public static class ProviderNames
{
    public static async Task<Dictionary<(string, int), string>> ResolveAsync(IApplicationDbContext db, IEnumerable<(string Type, int Id)> keys, CancellationToken ct)
    {
        var list = keys.Distinct().ToList();
        var cp = list.Where(k => k.Type == BillingProviderTypes.ChargingPoint).Select(k => k.Id).ToList();
        var sp = list.Where(k => k.Type == BillingProviderTypes.ServiceProvider).Select(k => k.Id).ToList();
        var result = new Dictionary<(string, int), string>();
        if (cp.Count > 0)
            foreach (var x in await db.ChargingPoints.AsNoTracking().Where(c => cp.Contains(c.Id)).Select(c => new { c.Id, c.Name }).ToListAsync(ct))
                result[(BillingProviderTypes.ChargingPoint, x.Id)] = x.Name;
        if (sp.Count > 0)
            foreach (var x in await db.ServiceProviders.AsNoTracking().Where(c => sp.Contains(c.Id)).Select(c => new { c.Id, c.Name }).ToListAsync(ct))
                result[(BillingProviderTypes.ServiceProvider, x.Id)] = x.Name;
        return result;
    }
}

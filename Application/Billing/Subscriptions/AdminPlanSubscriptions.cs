using Application.Common.Security;
using Application.Subscriptions;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Billing.Subscriptions;

// ---------------------------------------------------------------------------
// List records
// ---------------------------------------------------------------------------

public record GetAdminPlanSubscriptionsRequest(
    string? ProviderType = null, int? ProviderId = null, int? PlanId = null, string? Status = null,
    DateTime? From = null, DateTime? To = null, int? Page = null, int? PageSize = null) : IRequest<PagedResult<PlanSubscriptionDto>>;

public class GetAdminPlanSubscriptionsRequestHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetAdminPlanSubscriptionsRequest, PagedResult<PlanSubscriptionDto>>
{
    public async Task<PagedResult<PlanSubscriptionDto>> Handle(GetAdminPlanSubscriptionsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var q = applicationDbContext.PlanSubscriptions.AsNoTracking().Where(r => !r.IsDeleted);
        if (!string.IsNullOrWhiteSpace(request.ProviderType)) q = q.Where(r => r.ProviderType == request.ProviderType);
        if (request.ProviderId is int pid) q = q.Where(r => r.ProviderId == pid);
        if (request.PlanId is int plid) q = q.Where(r => r.BillingPlanId == plid);
        if (request.From is { } from) { var f = JordanTime.ToUtc(from); q = q.Where(r => r.StartsAt >= f); }
        if (request.To is { } to) { var t = JordanTime.ToUtc(to); q = q.Where(r => r.StartsAt <= t); }

        var rows = await q.OrderByDescending(r => r.StartsAt).ThenByDescending(r => r.Id).ToListAsync(cancellationToken);
        var mapped = await PlanSubscriptionMapper.MapAsync(applicationDbContext, rows, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.Status))
            mapped = mapped.Where(m => string.Equals(m.Status, request.Status, StringComparison.OrdinalIgnoreCase)).ToList();
        return mapped.ToOptionallyPaginated(request.Page, request.PageSize);
    }
}

// ---------------------------------------------------------------------------
// New subscription / renewal / trial (admin)
// ---------------------------------------------------------------------------

public record CreateAdminPlanSubscriptionCommand(
    string ProviderType,
    int ProviderId,
    int PlanId,
    int? PriceOptionId,
    DateTime? StartsAt = null,
    AdminPaymentInput? Payment = null,
    bool IsTrial = false,
    int? UpgradeRequestId = null,
    string? Note = null) : IRequest<PlanSubscriptionDto>;

public class CreateAdminPlanSubscriptionCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    PlanSubscriptionService service)
    : IRequestHandler<CreateAdminPlanSubscriptionCommand, PlanSubscriptionDto>
{
    public async Task<PlanSubscriptionDto> Handle(CreateAdminPlanSubscriptionCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        await BillingAccessGuard.EnsureOwnerOrAdminAsync(applicationDbContext, currentUserService, request.ProviderType, request.ProviderId, cancellationToken);
        var userId = currentUserService.UserId;

        var plan = await PlanCatalog.LoadAsync(applicationDbContext, request.PlanId, cancellationToken);
        PlanCatalog.EnsureSellable(plan, request.ProviderType);

        PlanSubscription record;
        if (request.IsTrial)
            record = plan.Category == PlanCategories.Loyalty
                ? await service.StartTrialAsync(userId, request.ProviderType, request.ProviderId, plan, byPartner: false, cancellationToken)
                : await service.InTransactionAsync(() => service.StartTrialAsync(userId, request.ProviderType, request.ProviderId, plan, byPartner: false, cancellationToken), cancellationToken);
        else if (plan.Category == PlanCategories.Loyalty)
        {
            var option = PlanCatalog.RequireOption(plan, request.PriceOptionId);
            var current = await LoyaltyBundleService.LatestAsync(applicationDbContext, request.ProviderType, request.ProviderId, cancellationToken);
            if (current is not null && current.BillingPlanId != plan.Id && LoyaltyBundleService.IsUsable(current, DateTime.UtcNow))
                PlanSubscriptionService.ThrowFor(BillingErrorCodes.ActiveSubscriptionExists);
            record = await LoyaltyBundleService.PurchaseAsync(applicationDbContext, userId, request.ProviderType, request.ProviderId,
                plan, option, false, null, 0, request.UpgradeRequestId, request.Note, cancellationToken);
        }
        else
        {
            if (plan.Key == PlanKeys.Free)
                throw new DataValidationException("PlanId", Lang.Pick("الباقة المجانية تلقائية ولا تحتاج اشتراك", "The free plan applies automatically; it needs no subscription"));
            var option = PlanCatalog.RequireOption(plan, request.PriceOptionId);
            var snapshot = await ProviderPlanResolver.LoadAsync(applicationDbContext, request.ProviderType, request.ProviderId, cancellationToken);
            PlanSubscriptionService.EnsureNoConflictingPlan(snapshot, plan);
            record = await service.InTransactionAsync(() => service.SubscribeAsync(userId, request.ProviderType, request.ProviderId, plan, option, request.Payment,
                request.StartsAt is { } s ? JordanTime.ToUtc(s) : null,
                new PlanPaymentLink(UpgradeRequestId: request.UpgradeRequestId, Source: PlanSubscriptionSources.Admin), request.Note, cancellationToken), cancellationToken);
        }

        return (await PlanSubscriptionMapper.MapAsync(applicationDbContext, [record], cancellationToken))[0];
    }
}

// ---------------------------------------------------------------------------
// Cancel
// ---------------------------------------------------------------------------

public record CancelPlanSubscriptionCommand(int Id, string Reason) : IRequest<PlanSubscriptionDto>;

public class CancelPlanSubscriptionCommandHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<CancelPlanSubscriptionCommand, PlanSubscriptionDto>
{
    public async Task<PlanSubscriptionDto> Handle(CancelPlanSubscriptionCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new DataValidationException("Reason", Lang.Pick("سبب الإلغاء مطلوب", "A reason is required"));

        var record = await applicationDbContext.PlanSubscriptions.FirstOrDefaultAsync(r => r.Id == request.Id && !r.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException(Lang.Pick($"لم يتم العثور على الاشتراك رقم {request.Id}", $"Subscription record {request.Id} not found"));
        if (PlanSubscriptionStatuses.IsTerminal(record.Status))
            throw new UnprocessableException(BillingErrorCodes.SubscriptionNotActive, Lang.Pick("هذا الاشتراك منتهي من قبل", "This subscription has already ended"));

        var now = DateTime.UtcNow;
        record.Status = PlanSubscriptionStatuses.Cancelled;
        record.CancelledAt = now;
        record.CancelledByUserId = currentUserService.UserId;
        record.CancelReason = request.Reason.Trim();

        // Ending the record ends what it switched on in the existing engine — unless a later paid period covers it.
        if (record.SubscriptionId is int sid && record.PlanCategory == PlanCategories.Subscription)
        {
            var sub = await applicationDbContext.Subscriptions.FirstOrDefaultAsync(s => s.Id == sid && !s.IsDeleted, cancellationToken);
            var laterPeriod = await applicationDbContext.PlanSubscriptions.AnyAsync(r => r.SubscriptionId == sid && r.Id != record.Id && !r.IsDeleted
                && r.Status == PlanSubscriptionStatuses.Active && r.EndsAt > (record.EndsAt ?? now), cancellationToken);
            if (sub is { IsSwitchedOff: false } && !laterPeriod)
                await LegacySubscriptionBridge.SwitchOffAsync(applicationDbContext, sub, currentUserService.UserId, cancellationToken);
        }

        await applicationDbContext.SaveChanges(cancellationToken);
        return (await PlanSubscriptionMapper.MapAsync(applicationDbContext, [record], cancellationToken))[0];
    }
}

// ---------------------------------------------------------------------------
// Upgrade mid-subscription (decision 16: remaining value credited in JOD)
// ---------------------------------------------------------------------------

public record UpgradePlanSubscriptionCommand(int Id, int PlanId, int PriceOptionId, AdminPaymentInput? Payment = null, string? Note = null)
    : IRequest<PlanSubscriptionDto>;

public class UpgradePlanSubscriptionCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    PlanSubscriptionService service)
    : IRequestHandler<UpgradePlanSubscriptionCommand, PlanSubscriptionDto>
{
    public async Task<PlanSubscriptionDto> Handle(UpgradePlanSubscriptionCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var userId = currentUserService.UserId;

        var old = await applicationDbContext.PlanSubscriptions.FirstOrDefaultAsync(r => r.Id == request.Id && !r.IsDeleted, cancellationToken)
                  ?? throw new NotFoundException(Lang.Pick($"لم يتم العثور على الاشتراك رقم {request.Id}", $"Subscription record {request.Id} not found"));
        var snapshot = await ProviderPlanResolver.LoadAsync(applicationDbContext, old.ProviderType, old.ProviderId, cancellationToken);
        var oldState = snapshot.StateOf(snapshot.Records.First(r => r.Id == old.Id));
        if (!oldState.IsOn)
            throw new UnprocessableException(BillingErrorCodes.SubscriptionNotActive,
                Lang.Pick("الترقية لاشتراك فعّال فقط — للمنتهي سجّل اشتراك جديد", "Only an active subscription can be upgraded — start a new one instead"));

        var plan = await PlanCatalog.LoadAsync(applicationDbContext, request.PlanId, cancellationToken);
        PlanCatalog.EnsureSellable(plan, old.ProviderType);
        if (plan.Category != old.PlanCategory)
            throw new DataValidationException("PlanId", Lang.Pick("الترقية ضمن نفس الفئة فقط", "An upgrade stays within the same category"));
        if (plan.Id == old.BillingPlanId)
            throw new DataValidationException("PlanId", Lang.Pick("نفس الباقة — استعمل التجديد", "Same plan — record a renewal instead"));
        var option = PlanCatalog.RequireOption(plan, request.PriceOptionId);

        var credit = old.IsTrial ? 0m : BillingMath.RemainingCredit(old.PricePaidJod + old.CreditAppliedJod, old.StartsAt, old.EndsAt,
            old.ScansIncluded, old.ScansUsed, DateTime.UtcNow);

        PlanSubscription record;
        if (plan.Category == PlanCategories.Loyalty)
        {
            record = await LoyaltyBundleService.PurchaseAsync(applicationDbContext, userId, old.ProviderType, old.ProviderId,
                plan, option, false, old, credit, null, request.Note, cancellationToken);
        }
        else record = await service.InTransactionAsync(async () =>
        {
            old.Status = PlanSubscriptionStatuses.Upgraded;
            old.ModifiedBy = userId;

            // The old plan's engine row ends now, unless the new plan includes it (connect ⊇ premium) —
            // then it is extended to the new end below.
            var oldPlan = snapshot.Plans.FirstOrDefault(p => p.Id == old.BillingPlanId);
            var oldEt = oldPlan is null ? null : PlanCatalog.LegacyEntityTypeFor(oldPlan, old.ProviderType);
            var newEt = PlanCatalog.LegacyEntityTypeFor(plan, old.ProviderType);
            if (oldEt is not null && oldEt != newEt && !PlanCatalog.Includes(plan).Contains(old.BillingPlanId))
            {
                var sub = await applicationDbContext.Subscriptions.FirstOrDefaultAsync(s => s.EntityType == oldEt && s.EntityId == old.ProviderId && !s.IsDeleted, cancellationToken);
                if (sub is { IsSwitchedOff: false })
                    await LegacySubscriptionBridge.SwitchOffAsync(applicationDbContext, sub, userId, cancellationToken);
            }
            await applicationDbContext.SaveChanges(cancellationToken);

            var created = await service.SubscribeAsync(userId, old.ProviderType, old.ProviderId, plan, option, request.Payment, null,
                new PlanPaymentLink(CreditAppliedJod: credit, UpgradedFromId: old.Id, Source: PlanSubscriptionSources.Upgrade),
                request.Note, cancellationToken);
            if (created.UpgradedFromId is null) { created.UpgradedFromId = old.Id; await applicationDbContext.SaveChanges(cancellationToken); }
            return created;
        }, cancellationToken);

        return (await PlanSubscriptionMapper.MapAsync(applicationDbContext, [record], cancellationToken))[0];
    }
}

// ---------------------------------------------------------------------------
// Break-even meter for one subscription (admin only; never shown to the partner)
// ---------------------------------------------------------------------------

public record BreakEvenDto(
    int PlanSubscriptionId,
    /// <summary>total (scan bundle) | month (unlimited: this Jordan month).</summary>
    string Period,
    int ScansUsed,
    int? BreakEvenScans,
    double Percent,
    /// <summary>ok | warning (≥ 80%) | over (&gt; 100%).</summary>
    string State,
    double AvgPointsPerScan,
    decimal CostPerScanJod,
    decimal PriceJod,
    string AvgSource);

public record GetPlanSubscriptionBreakEvenRequest(int Id) : IRequest<BreakEvenDto>;

public class GetPlanSubscriptionBreakEvenRequestHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<GetPlanSubscriptionBreakEvenRequest, BreakEvenDto>
{
    public async Task<BreakEvenDto> Handle(GetPlanSubscriptionBreakEvenRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var record = await applicationDbContext.PlanSubscriptions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.Id && !r.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException(Lang.Pick($"لم يتم العثور على الاشتراك رقم {request.Id}", $"Subscription record {request.Id} not found"));
        return await BreakEvenCalculator.ComputeAsync(applicationDbContext, record, cancellationToken);
    }
}

public static class BreakEvenCalculator
{
    public static async Task<BreakEvenDto> ComputeAsync(IApplicationDbContext db, PlanSubscription r, CancellationToken ct)
    {
        var rule = BillingJson.ReadRule(r.PointsRuleSnapshot);
        var price = r.PricePaidJod + r.CreditAppliedJod;
        var snap = BillingJson.Read<PriceOptionSnapshot>(r.PriceOptionSnapshot);
        if (price <= 0 && snap is not null) price = snap.PriceJod;
        var pointsPerJod = await BillingSettingsProvider.GetPointsPerJodAsync(db, ct);

        // Real average when the bundle has enough history; otherwise the rule with the provider's last 90 days.
        var consumed = await db.PartnerTransactionBundleScans.AsNoTracking()
            .Where(m => m.ConsumedPlanSubscriptionId == r.Id && m.RefundedAt == null)
            .Select(m => new { m.ConsumedAt, m.PartnerTransaction.BasePoints, m.PartnerTransaction.PointsAwarded })
            .ToListAsync(ct);

        double avg; string source;
        if (consumed.Count >= 10) { avg = consumed.Average(c => (double)(c.BasePoints ?? c.PointsAwarded ?? 0)); source = "actual"; }
        else if (rule is null) { avg = 0; source = "none"; }
        else
        {
            var amounts = await Plans.BillingQueries.RecentAmountsAsync(db, r.ProviderType, r.ProviderId, ct);
            if (amounts.Count > 0 && rule.Mode == PointsRule.Tiered) { avg = BillingMath.AveragePointsFromAmounts(rule, amounts).Avg; source = "provider"; }
            else { var a = BillingMath.AveragePoints(rule, null); avg = a.Avg; source = a.Source; }
        }

        var cost = (decimal)avg * BillingMath.PointValueJod(pointsPerJod);
        int? breakEven = cost > 0 ? (int)Math.Floor(price / cost) : null;
        var period = "total";
        var used = r.ScansUsed;

        if (r.ScansIncluded is null && r.EndsAt is { } end)
        {
            // Unlimited: compare this month's scans with the monthly break-even (decision 20's soft cap).
            var months = Math.Max(1, snap?.Months ?? (int)Math.Round((end - r.StartsAt).TotalDays / 30.0));
            breakEven = breakEven is null ? null : (int)Math.Floor(breakEven.Value / (double)months);
            var local = JordanTime.FromUtc(DateTime.UtcNow);
            var monthStart = JordanTime.ToUtc(new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified));
            used = consumed.Count(c => c.ConsumedAt >= monthStart);
            period = "month";
        }

        var (pct, state) = BillingMath.BreakEvenState(used, breakEven);
        return new BreakEvenDto(r.Id, period, used, breakEven, pct, state, Math.Round(avg, 3), Math.Round(cost, 3), price, source);
    }
}

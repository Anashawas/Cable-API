using Application.Common.Security;
using Application.Promos;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Billing.Plans;

// ---------------------------------------------------------------------------
// Admin: «حاسبة المقارنة» (PARTNER_BILLING §3.4, ModelCompare) — a sales tool. The partner's real last 90 days
// of completed scans, priced under the percentage model and simulated on a scan bundle, side by side.
// Points cost only (Billing.PointsPerJod), like the margin calculator; boost / promo points are marketing, not counted.
// ---------------------------------------------------------------------------

/// <param name="BundlePlanId">A Loyalty plan to simulate. Default: the partner's current / latest bundle's plan, else the first active Loyalty plan for that provider type.</param>
/// <param name="PriceOptionId">One of that plan's price options. Default: the option of the partner's own bundle, else the active option that would have cost the partner least for the period.</param>
public record ModelCompareCommand(string ProviderType, int ProviderId, int? BundlePlanId = null, int? PriceOptionId = null) : IRequest<ModelCompareResult>;

/// <summary>The percentage model over the period (what happened).</summary>
/// <param name="CommissionJod">Commission the partner paid (recorded on each code; bundle-billed codes in the period are priced at the agreement's current %).</param>
/// <param name="Points">Base points Cable gave (before boosts / promos; bundle-billed codes priced by the agreement's reward %).</param>
/// <param name="PointsCostJod">Points ÷ Billing.PointsPerJod.</param>
/// <param name="CableMarginJod">Commission − points cost.</param>
/// <param name="SimulatedTransactions">Codes in the period that were bundle-billed, so their percentage figures are simulated.</param>
public record ModelComparePercentageDto(
    int Transactions,
    decimal TotalAmountJod,
    decimal AverageAmountJod,
    double CommissionPercent,
    decimal CommissionJod,
    int Points,
    decimal PointsCostJod,
    decimal CableMarginJod,
    int SimulatedTransactions);

/// <summary>The same period simulated on a scan bundle.</summary>
/// <param name="Scans">Scans per bundle; null = unlimited.</param>
/// <param name="BundlesNeeded">Bundles the period's scans would have used (and periods, for a time-limited option).</param>
/// <param name="CostJod">What the partner would have paid Cable: bundles needed × price.</param>
/// <param name="PricePerScanJod">Price ÷ scans of one bundle (null for unlimited).</param>
/// <param name="EffectivePricePerScanJod">Cost ÷ scans actually used.</param>
/// <param name="ScansUsed">Completed scans at or above the bundle minimum amount (smaller ones get no code under a bundle).</param>
/// <param name="ScansLeftOver">Scans paid for but not used (null for unlimited).</param>
/// <param name="BelowMinimumTransactions">Codes under the bundle minimum amount — not possible under a bundle, left out.</param>
/// <param name="Points">Points the plan's rule gives on those amounts.</param>
/// <param name="CableMarginJod">Cost − points cost.</param>
public record ModelCompareBundleDto(
    int PlanId,
    string PlanKey,
    string PlanName,
    int PriceOptionId,
    decimal PriceJod,
    int? Scans,
    int? Months,
    int BundlesNeeded,
    decimal CostJod,
    decimal? PricePerScanJod,
    decimal? EffectivePricePerScanJod,
    int ScansUsed,
    int? ScansLeftOver,
    decimal MinimumAmountJod,
    int BelowMinimumTransactions,
    int Points,
    decimal PointsCostJod,
    decimal CableMarginJod);

/// <param name="Model">bundle | percentage | noData.</param>
/// <param name="Code">bundleSavesPartner | bundleLosesForCable | percentageCheaperForPartner | noTransactions.</param>
/// <param name="PartnerSavingJod">Percentage commission − bundle cost (positive = the bundle is cheaper for the partner).</param>
/// <param name="CableMarginDifferenceJod">Bundle margin − percentage margin (positive = Cable earns more on the bundle).</param>
public record ModelCompareRecommendationDto(string Model, string Code, string Text, decimal PartnerSavingJod, decimal CableMarginDifferenceJod);

public record ModelCompareResult(
    string ProviderType,
    int ProviderId,
    string? ProviderName,
    string CurrentBillingModel,
    DateTime FromUtc,
    DateTime ToUtc,
    int Days,
    double PointsPerJod,
    decimal PointValueJod,
    ModelComparePercentageDto Percentage,
    ModelCompareBundleDto Bundle,
    ModelCompareRecommendationDto Recommendation);

public class ModelCompareCommandHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<ModelCompareCommand, ModelCompareResult>
{
    public const int Days = 90;

    public async Task<ModelCompareResult> Handle(ModelCompareCommand request, CancellationToken cancellationToken)
    {
        var db = applicationDbContext;
        await AdminRoleGuard.EnsureAdminAsync(db, currentUserService, cancellationToken);
        if (!BillingProviderTypes.IsValid(request.ProviderType))
            throw new DataValidationException("ProviderType", Lang.Pick("نوع المزوّد يجب أن يكون ChargingPoint أو ServiceProvider", "ProviderType must be ChargingPoint or ServiceProvider"));
        var owner = await BillingAccessGuard.OwnerOfAsync(db, request.ProviderType, request.ProviderId, cancellationToken);
        if (!owner.Exists)
            throw new NotFoundException(Lang.Pick($"لم يتم العثور على {request.ProviderType} رقم {request.ProviderId}", $"{request.ProviderType} with id {request.ProviderId} not found"));

        var agreement = await db.PartnerAgreements.AsNoTracking()
            .Where(a => a.ProviderType == request.ProviderType && a.ProviderId == request.ProviderId && !a.IsDeleted)
            .OrderByDescending(a => a.IsActive).ThenByDescending(a => a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var (plan, chosenOption) = await PickBundleAsync(request, cancellationToken);
        var rule = BillingJson.ReadRule(plan.PointsRule);
        var minimum = agreement?.MinimumTransactionAmount is > 0
            ? agreement.MinimumTransactionAmount.Value
            : await BillingSettingsProvider.GetBundleMinimumAmountAsync(db, cancellationToken);
        var pointsPerJod = await BillingSettingsProvider.GetPointsPerJodAsync(db, cancellationToken);

        var to = DateTime.UtcNow;
        var from = to.AddDays(-Days);
        var rows = await db.PartnerTransactions.AsNoTracking()
            .Where(t => t.ProviderType == request.ProviderType && t.ProviderId == request.ProviderId && !t.IsDeleted
                        && t.Status == (int)PartnerTransactionStatus.Completed && t.CompletedAt >= from && t.TransactionAmount != null)
            .Select(t => new ModelCompareMath.Scan(
                t.TransactionAmount!.Value, t.CommissionAmount, t.CommissionPercentage, t.BasePoints ?? t.PointsAwarded,
                t.PointsConversionRate,
                db.PartnerTransactionBundleScans.Any(b => b.PartnerTransactionId == t.Id)))
            .ToListAsync(cancellationToken);

        var percentage = ModelCompareMath.Percentage(rows, agreement?.CommissionPercentage ?? 0, agreement?.PointsRewardPercentage ?? 0, pointsPerJod);
        // No option given (and none recorded): the one that would have cost the partner least for this period.
        var options = chosenOption is not null ? [chosenOption] : plan.PriceOptions.Where(o => o.IsActive && !o.IsDeleted).ToList();
        if (options.Count == 0)
            throw new UnprocessableException(BillingErrorCodes.InvalidPriceOption,
                Lang.Pick("هذه الباقة ما فيها خيار سعر فعّال", "This plan has no active price option"));
        var bundle = options
            .Select(o => ModelCompareMath.Bundle(rows, plan, o, rule, minimum, pointsPerJod, Days))
            .OrderBy(b => b.CostJod).ThenBy(b => b.PriceJod)
            .First();

        return new ModelCompareResult(
            request.ProviderType, request.ProviderId, owner.Name,
            BillingModels.Normalize(agreement?.BillingModel),
            from, to, Days, pointsPerJod, BillingMath.PointValueJod(pointsPerJod),
            percentage, bundle, ModelCompareMath.Recommend(percentage, bundle));
    }

    /// <summary>The plan to simulate and its price option (null = let the period pick the cheapest).</summary>
    private async Task<(BillingPlan Plan, BillingPlanPriceOption? Option)> PickBundleAsync(ModelCompareCommand request, CancellationToken ct)
    {
        var db = applicationDbContext;
        BillingPlan? plan;
        int? recordedOptionId = null;
        if (request.BundlePlanId is int planId)
            plan = await PlanCatalog.LoadAsync(db, planId, ct);
        else
        {
            var latest = await LoyaltyBundleService.LatestAsync(db, request.ProviderType, request.ProviderId, ct);
            var plans = await PlanCatalog.LoadAllAsync(db, ct);
            plan = latest is not null ? plans.FirstOrDefault(p => p.Id == latest.BillingPlanId) : null;
            recordedOptionId = plan is not null ? latest?.PriceOptionId : null;
            plan ??= plans.Where(p => p.Category == PlanCategories.Loyalty && p.IsActive && PlanCatalog.AppliesToProvider(p, request.ProviderType))
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Id).FirstOrDefault();
        }

        if (plan is null || plan.Category != PlanCategories.Loyalty)
            throw new UnprocessableException(BillingErrorCodes.PlanNotApplicable,
                Lang.Pick("اختر باقة ولاء (حزمة مسحات) للمقارنة", "Choose a Loyalty (scan bundle) plan to compare with"));

        if (request.PriceOptionId is not null)
            return (plan, PlanCatalog.RequireOption(plan, request.PriceOptionId, allowInactive: true));
        // The partner's own bundle: the option they bought.
        return (plan, recordedOptionId is not null ? plan.PriceOptions.FirstOrDefault(o => o.Id == recordedOptionId && !o.IsDeleted) : null);
    }
}

/// <summary>Pure arithmetic of the comparison (no database).</summary>
public static class ModelCompareMath
{
    /// <param name="Commission">Recorded commission (null = not recorded).</param>
    /// <param name="BasePoints">Points before boosts / promos as recorded.</param>
    /// <param name="BundleBilled">The code was billed on a scan bundle (its commission / points are not the percentage model's).</param>
    public sealed record Scan(decimal Amount, decimal? Commission, double CommissionPercentage, int? BasePoints, double PointsConversionRate, bool BundleBilled);

    private static decimal Jod(decimal v) => Math.Round(v, 3, MidpointRounding.AwayFromZero);

    private static decimal PointsCost(int points, double pointsPerJod) =>
        pointsPerJod > 0 ? Jod(points / (decimal)pointsPerJod) : 0m;

    public static ModelComparePercentageDto Percentage(IReadOnlyList<Scan> scans, double agreementCommissionPercent,
        double agreementRewardPercent, double pointsPerJod)
    {
        decimal commission = 0;
        var points = 0;
        var simulated = 0;
        foreach (var s in scans)
        {
            if (s.BundleBilled)
            {
                simulated++;
                commission += PromoMath.Commission(s.Amount, agreementCommissionPercent);
                points += PromoMath.Points(s.Amount, agreementRewardPercent, s.PointsConversionRate);
            }
            else
            {
                commission += s.Commission ?? PromoMath.Commission(s.Amount, s.CommissionPercentage);
                points += s.BasePoints ?? 0;
            }
        }

        var total = scans.Sum(s => s.Amount);
        var pointsCost = PointsCost(points, pointsPerJod);
        commission = Jod(commission);
        var percent = scans.Any(s => !s.BundleBilled)
            ? Math.Round(scans.Where(s => !s.BundleBilled).Average(s => s.CommissionPercentage), 2)
            : agreementCommissionPercent;
        return new ModelComparePercentageDto(scans.Count, Jod(total), scans.Count > 0 ? Jod(total / scans.Count) : 0m,
            percent, commission, points, pointsCost, commission - pointsCost, simulated);
    }

    public static ModelCompareBundleDto Bundle(IReadOnlyList<Scan> scans, BillingPlan plan, BillingPlanPriceOption option, PointsRule? rule,
        decimal minimumAmount, double pointsPerJod, int days)
    {
        var eligible = scans.Where(s => s.Amount >= minimumAmount).ToList();
        var used = eligible.Count;
        var points = eligible.Sum(s => PointsRuleCalculator.PointsFor(rule, s.Amount));

        // Bundles by scans, and by periods when the option also expires (e.g. 400 scans / 1 month over 90 days = 3).
        var byScans = option.Scans is > 0 ? (int)Math.Ceiling(used / (double)option.Scans.Value) : 1;
        var byPeriods = option.Months is > 0 ? (int)Math.Ceiling(days / (30.0 * option.Months.Value)) : 1;
        var bundles = Math.Max(1, Math.Max(byScans, byPeriods));

        var cost = Jod(bundles * option.PriceJod);
        var pointsCost = PointsCost(points, pointsPerJod);
        return new ModelCompareBundleDto(
            plan.Id, plan.Key, PlanCatalog.Name(plan), option.Id, option.PriceJod, option.Scans, option.Months,
            bundles, cost,
            option.Scans is > 0 ? Jod(option.PriceJod / option.Scans.Value) : null,
            used > 0 ? Jod(cost / used) : null,
            used,
            option.Scans is > 0 ? bundles * option.Scans.Value - used : null,
            minimumAmount, scans.Count - used,
            points, pointsCost, cost - pointsCost);
    }

    /// <summary>
    /// Bundle when it is cheaper (or equal) for the partner and Cable still covers the points; percentage when the
    /// bundle would cost Cable more in points than it brings, or costs the partner more than the commission.
    /// </summary>
    public static ModelCompareRecommendationDto Recommend(ModelComparePercentageDto p, ModelCompareBundleDto b)
    {
        var saving = p.CommissionJod - b.CostJod;
        var marginDiff = b.CableMarginJod - p.CableMarginJod;

        if (p.Transactions == 0)
            return new("noData", "noTransactions", Lang.Pick(
                    "ما في معاملات مكتملة آخر 90 يوم — ما بنقدر نقارن.",
                    "No completed transactions in the last 90 days — nothing to compare."), saving, marginDiff);

        if (b.CableMarginJod < 0)
            return new("percentage", "bundleLosesForCable", Lang.Pick(
                    $"الحزمة بتخسّر كيبل {-b.CableMarginJod:0.000} د بالنقاط على هالفترة — خلّيه على النسبة أو اختر حزمة أغلى.",
                    $"The bundle would cost Cable {-b.CableMarginJod:0.000} JOD in points over this period — keep the percentage or pick a pricier bundle."),
                saving, marginDiff);

        if (saving >= 0)
            return new("bundle", "bundleSavesPartner", Lang.Pick(
                    $"الحزمة بتوفّر على الشريك {saving:0.000} د خلال 90 يوم، وهامش كيبل {b.CableMarginJod:0.000} د.",
                    $"The bundle saves the partner {saving:0.000} JOD over 90 days, with a Cable margin of {b.CableMarginJod:0.000} JOD."),
                saving, marginDiff);

        return new("percentage", "percentageCheaperForPartner", Lang.Pick(
                $"النسبة أرخص على الشريك بـ{-saving:0.000} د خلال 90 يوم.",
                $"The percentage model is {-saving:0.000} JOD cheaper for the partner over 90 days."),
            saving, marginDiff);
    }
}

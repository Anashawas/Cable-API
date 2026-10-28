using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Billing;

/// <summary>Reading plans and mapping them onto the existing subscription engine. One place, so the rules cannot fork.</summary>
public static class PlanCatalog
{
    public static List<string> AppliesTo(BillingPlan p) => BillingJson.ReadStrings(p.AppliesTo);
    public static List<string> Features(BillingPlan p) => BillingJson.ReadStrings(p.Features);
    public static List<int> Includes(BillingPlan p) => BillingJson.ReadInts(p.IncludesPlanIds);

    public static List<string> LegacyEntityTypes(BillingPlan p) =>
        string.IsNullOrWhiteSpace(p.LegacyEntityTypes)
            ? []
            : p.LegacyEntityTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(SubscriptionEntityTypes.IsValid).Distinct().ToList();

    public static bool AppliesToProvider(BillingPlan p, string providerType) =>
        AppliesTo(p).Contains(providerType, StringComparer.OrdinalIgnoreCase);

    /// <summary>The existing Subscription.EntityType this plan is sold through for this provider type, if any.</summary>
    public static string? LegacyEntityTypeFor(BillingPlan p, string providerType) =>
        LegacyEntityTypes(p).FirstOrDefault(t => BillingProviderTypes.OfEntityType(t) == providerType);

    /// <summary>The plan an existing Subscription row belongs to (active plans first, then by sort order).</summary>
    public static BillingPlan? PlanForEntityType(IEnumerable<BillingPlan> plans, string entityType) =>
        plans.Where(p => p.Category == PlanCategories.Subscription && LegacyEntityTypes(p).Contains(entityType))
            .OrderByDescending(p => p.IsActive).ThenBy(p => p.SortOrder).FirstOrDefault();

    public static Task<List<BillingPlan>> LoadAllAsync(IApplicationDbContext db, CancellationToken ct) =>
        db.BillingPlans.AsNoTracking().Include(p => p.PriceOptions).Where(p => !p.IsDeleted).ToListAsync(ct);

    public static async Task<BillingPlan> LoadAsync(IApplicationDbContext db, int planId, CancellationToken ct, bool track = false)
    {
        var q = track ? db.BillingPlans.Include(p => p.PriceOptions) : db.BillingPlans.AsNoTracking().Include(p => p.PriceOptions);
        return await q.FirstOrDefaultAsync(p => p.Id == planId && !p.IsDeleted, ct)
               ?? throw new NotFoundException(Lang.Pick($"لم يتم العثور على الباقة رقم {planId}", $"Plan with id {planId} not found"));
    }

    /// <summary>Plan features plus the features of the plans it includes (connect ⊇ premium).</summary>
    public static HashSet<string> EffectiveFeatures(BillingPlan plan, IReadOnlyCollection<BillingPlan> all)
    {
        var set = new HashSet<string>(Features(plan));
        foreach (var id in Includes(plan))
            if (all.FirstOrDefault(p => p.Id == id) is { } inc)
                set.UnionWith(Features(inc));
        return set;
    }

    public static BillingPlanPriceOption RequireOption(BillingPlan plan, int? priceOptionId, bool allowInactive = false)
    {
        var option = plan.PriceOptions.FirstOrDefault(o => o.Id == priceOptionId && !o.IsDeleted && (allowInactive || o.IsActive));
        return option ?? throw new Cable.Core.Exceptions.UnprocessableException(BillingErrorCodes.InvalidPriceOption,
            Lang.Pick("خيار السعر غير موجود لهذه الباقة", "That price option does not exist on this plan"));
    }

    public static void EnsureSellable(BillingPlan plan, string providerType)
    {
        if (!plan.IsActive)
            throw new UnprocessableException(BillingErrorCodes.PlanArchived,
                Lang.Pick("هذه الباقة مؤرشفة ولا تُباع حالياً", "This plan is archived and is no longer sold"));
        if (!AppliesToProvider(plan, providerType))
            throw new UnprocessableException(BillingErrorCodes.PlanNotApplicable,
                Lang.Pick("هذه الباقة غير متاحة لهذا النوع", "This plan is not offered for this provider type"));
    }

    public static string Name(BillingPlan p) => Lang.Pick(p.NameAr, p.NameEn);

    public static string SnapshotOf(BillingPlanPriceOption o) => BillingJson.Write(new PriceOptionSnapshot(o.Months, o.PriceJod, o.Scans));
}

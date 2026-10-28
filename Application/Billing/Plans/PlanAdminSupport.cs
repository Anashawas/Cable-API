using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Application.Settings;
using Application.Subscriptions;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Billing.Plans;

/// <summary>Server-side validation of a plan body (§2b.9 error codes).</summary>
public static partial class PlanInputValidator
{
    [GeneratedRegex("^[a-z0-9_]{2,50}$")]
    private static partial Regex KeyPattern();

    public static readonly string[] SellableEntityTypes =
        [SubscriptionEntityTypes.StationPremium, SubscriptionEntityTypes.ServiceProviderPremium, SubscriptionEntityTypes.OcppConnect];

    public static void Validate(PlanInput input, string category, IReadOnlyCollection<BillingPlan> others, int? selfId, string key)
    {
        var errors = new Dictionary<string, string[]>();
        void Err(string k, string ar, string en) => errors[k] = [Lang.Pick(ar, en)];

        if (selfId is null && !KeyPattern().IsMatch(key ?? ""))
            Err("Key", "المفتاح حروف إنجليزية صغيرة وأرقام و _ فقط (2–50)", "Key must be 2–50 lowercase letters, digits or _");
        if (string.IsNullOrWhiteSpace(input.NameAr) || input.NameAr.Length > 100) Err("NameAr", "الاسم بالعربي مطلوب (حتى 100 حرف)", "Arabic name is required (up to 100 characters)");
        if (string.IsNullOrWhiteSpace(input.NameEn) || input.NameEn.Length > 100) Err("NameEn", "الاسم بالإنجليزي مطلوب (حتى 100 حرف)", "English name is required (up to 100 characters)");
        if ((input.DescriptionAr?.Length ?? 0) > 1000 || (input.DescriptionEn?.Length ?? 0) > 1000) Err("Description", "الوصف حتى 1000 حرف", "Description is up to 1000 characters");

        var applies = input.AppliesTo ?? [];
        if (applies.Count == 0 || applies.Any(a => !BillingProviderTypes.IsValid(a)))
            Err("AppliesTo", "حدّد لمين الباقة: ChargingPoint و/أو ServiceProvider", "appliesTo must list ChargingPoint and/or ServiceProvider");

        if (input.TrialDays is < 0 or > 365) Err("TrialDays", "أيام التجربة بين 0 و 365", "trialDays must be between 0 and 365");
        if (input.TrialScans is < 0 or > 100000) Err("TrialScans", "مسحات التجربة غير صحيحة", "trialScans must be between 0 and 100000");
        if (category != PlanCategories.Loyalty && input.TrialScans > 0) Err("TrialScans", "مسحات التجربة لباقات الولاء فقط", "trialScans applies to loyalty plans only");
        if ((input.BadgeColor?.Length ?? 0) > 20) Err("BadgeColor", "اللون غير صحيح", "badgeColor is too long");
        if (input.DailySoftCap is < 1) Err("DailySoftCap", "الحد اليومي يجب أن يكون أكبر من صفر", "dailySoftCap must be at least 1");
        if (input.TargetMarginPercent is < 0 or > 100) Err("TargetMarginPercent", "الهامش بين 0 و 100", "targetMarginPercent must be between 0 and 100");

        var legacy = input.LegacyEntityTypes ?? [];
        if (legacy.Count > 0)
        {
            if (category != PlanCategories.Subscription || key == PlanKeys.Free)
                Err("LegacyEntityTypes", "الربط بأنواع الاشتراك لباقات الاشتراك المدفوعة فقط", "legacyEntityTypes applies to paid subscription plans only");
            else if (legacy.Any(t => !SellableEntityTypes.Contains(t)))
                Err("LegacyEntityTypes", "نوع اشتراك غير معروف", $"legacyEntityTypes must be among: {string.Join(", ", SellableEntityTypes)}");
            else if (others.FirstOrDefault(o => o.Id != selfId && PlanCatalog.LegacyEntityTypes(o).Intersect(legacy).Any()) is { } clash)
                Err("LegacyEntityTypes", $"هذا النوع مربوط بباقة '{clash.Key}'", $"Already sold through plan '{clash.Key}'");
        }

        var includes = input.IncludesPlanIds ?? [];
        if (includes.Any(id => id == selfId || others.All(o => o.Id != id)))
            Err("IncludesPlanIds", "باقة مضمّنة غير موجودة", "includesPlanIds lists an unknown plan");

        var options = input.PriceOptions ?? [];
        for (var i = 0; i < options.Count; i++)
        {
            var o = options[i];
            if (o.PriceJod < 0) Err($"PriceOptions[{i}].PriceJod", "السعر لا يكون سالب", "priceJod cannot be negative");
            if (o.Months is < 1 or > 24) Err($"PriceOptions[{i}].Months", "المدة بين 1 و 24 شهر", "months must be between 1 and 24");
            if (o.Scans is < 1) Err($"PriceOptions[{i}].Scans", "عدد المسحات يجب أن يكون أكبر من صفر", "scans must be at least 1");
            if (category == PlanCategories.Subscription)
            {
                if (o.Months is null) Err($"PriceOptions[{i}].Months", "باقة الاشتراك تحتاج مدة", "A subscription price option needs months");
                if (o.Scans is not null) Err($"PriceOptions[{i}].Scans", "المسحات لباقات الولاء فقط", "scans applies to loyalty plans only");
            }
            else if (o.Months is null && o.Scans is null)
                Err($"PriceOptions[{i}]", "حدّد مدة أو عدد مسحات (غير محدود يحتاج مدة)", "Set months, scans, or both (unlimited scanning needs months)");
        }
        if (key == PlanKeys.Free && options.Any(o => o.PriceJod > 0))
            Err("PriceOptions", "الباقة المجانية بسعر 0", "The free plan costs 0");

        if (errors.Count > 0) throw new DataValidationException(errors);

        var unknown = (input.Features ?? []).Where(f => !PlanFeatureCatalog.IsKnown(f)).Distinct().ToList();
        if (unknown.Count > 0)
            throw new UnprocessableException(BillingErrorCodes.UnknownFeature,
                Lang.Pick($"مفتاح ميزة غير موجود بالكتالوج: {string.Join(", ", unknown)}", $"Unknown feature key(s): {string.Join(", ", unknown)}"),
                new Dictionary<string, object?> { ["unknown"] = unknown });

        if (category == PlanCategories.Loyalty)
        {
            var reason = PointsRuleValidator.Validate(input.PointsRule);
            if (reason is not null)
                throw new UnprocessableException(BillingErrorCodes.InvalidTiers, reason);
        }
    }
}

/// <summary>Field-level differences between two saved versions of a plan (§2b.8). Pure.</summary>
public static class PlanDiff
{
    public record OptionShape(int? Months, decimal PriceJod, int? Scans, bool IsActive);

    public record Shape(
        string NameAr, string NameEn, string? DescriptionAr, string? DescriptionEn,
        List<string> AppliesTo, List<string> Features, List<int> IncludesPlanIds, List<string> LegacyEntityTypes,
        Dictionary<int, OptionShape> Options, string? PointsRule,
        int TrialDays, int TrialScans, bool IsActive, bool IsVisible, int SortOrder, string? BadgeColor,
        int? DailySoftCap, decimal? TargetMarginPercent);

    public static Shape Of(BillingPlan p) => new(
        p.NameAr, p.NameEn, p.DescriptionAr, p.DescriptionEn,
        PlanCatalog.AppliesTo(p), PlanCatalog.Features(p), PlanCatalog.Includes(p), PlanCatalog.LegacyEntityTypes(p),
        p.PriceOptions.Where(o => !o.IsDeleted).ToDictionary(o => o.Id, o => new OptionShape(o.Months, o.PriceJod, o.Scans, o.IsActive)),
        p.PointsRule, p.TrialDays, p.TrialScans, p.IsActive, p.IsVisible, p.SortOrder, p.BadgeColor, p.DailySoftCap, p.TargetMarginPercent);

    public static List<PlanChange> Diff(Shape a, Shape b)
    {
        var changes = new List<PlanChange>();
        void Scalar<T>(string field, T x, T y) { if (!EqualityComparer<T>.Default.Equals(x, y)) changes.Add(new PlanChange(field, Node(x), Node(y))); }
        void Set(string field, IEnumerable<string> x, IEnumerable<string> y)
        {
            var added = y.Except(x).ToList();
            var removed = x.Except(y).ToList();
            if (added.Count + removed.Count > 0) changes.Add(new PlanChange(field, Added: added, Removed: removed));
        }

        Scalar("nameAr", a.NameAr, b.NameAr);
        Scalar("nameEn", a.NameEn, b.NameEn);
        Scalar("descriptionAr", a.DescriptionAr, b.DescriptionAr);
        Scalar("descriptionEn", a.DescriptionEn, b.DescriptionEn);
        Set("appliesTo", a.AppliesTo, b.AppliesTo);
        Set("features", a.Features, b.Features);
        Set("includesPlanIds", a.IncludesPlanIds.Select(i => i.ToString()), b.IncludesPlanIds.Select(i => i.ToString()));
        Set("legacyEntityTypes", a.LegacyEntityTypes, b.LegacyEntityTypes);

        foreach (var (id, after) in b.Options)
        {
            if (!a.Options.TryGetValue(id, out var before))
            {
                changes.Add(new PlanChange($"priceOptions[{id}]", null, Node(new { after.Months, after.PriceJod, after.Scans })));
                continue;
            }
            Scalar($"priceOptions[{id}].months", before.Months, after.Months);
            Scalar($"priceOptions[{id}].priceJod", before.PriceJod, after.PriceJod);
            Scalar($"priceOptions[{id}].scans", before.Scans, after.Scans);
            Scalar($"priceOptions[{id}].isActive", before.IsActive, after.IsActive);
        }

        if (!string.Equals(a.PointsRule, b.PointsRule, StringComparison.Ordinal))
            changes.Add(new PlanChange("pointsRule", ParseJson(a.PointsRule), ParseJson(b.PointsRule)));

        Scalar("trialDays", a.TrialDays, b.TrialDays);
        Scalar("trialScans", a.TrialScans, b.TrialScans);
        Scalar("isActive", a.IsActive, b.IsActive);
        Scalar("isVisible", a.IsVisible, b.IsVisible);
        Scalar("sortOrder", a.SortOrder, b.SortOrder);
        Scalar("badgeColor", a.BadgeColor, b.BadgeColor);
        Scalar("dailySoftCap", a.DailySoftCap, b.DailySoftCap);
        Scalar("targetMarginPercent", a.TargetMarginPercent, b.TargetMarginPercent);
        return changes;
    }

    public static bool TouchesPricing(IEnumerable<PlanChange> changes) =>
        changes.Any(c => c.Field.StartsWith("priceOptions", StringComparison.Ordinal) || c.Field == "pointsRule");

    private static JsonNode? Node<T>(T value) => value is null ? null : JsonSerializerNode(value);
    private static JsonNode? JsonSerializerNode<T>(T value) => System.Text.Json.JsonSerializer.SerializeToNode(value, BillingJson.Options);
    private static JsonNode? ParseJson(string? json) { try { return json is null ? null : JsonNode.Parse(json); } catch { return JsonValue.Create(json); } }
}

/// <summary>Who is on a plan right now (subscriber counts, the subscribers tab, the "applies to N subscribers" warning).</summary>
public static class PlanSubscribers
{
    public record Row(int PlanId, string ProviderType, int ProviderId, int? PlanSubscriptionId, int? SubscriptionId,
        string Status, DateTime? ExpiresAt, bool IsTrial, int? ScansRemaining);

    public static async Task<List<Row>> ListAsync(IApplicationDbContext db, IReadOnlyCollection<BillingPlan> plans, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var grace = await AppSettingsProvider.GetSubscriptionGraceAsync(db, ct);
        var rows = new List<Row>();

        var legacyTypes = plans.SelectMany(PlanCatalog.LegacyEntityTypes).Distinct().ToList();
        var legacy = legacyTypes.Count == 0 ? new List<Subscription>() : await db.Subscriptions.AsNoTracking()
            .Where(s => !s.IsDeleted && legacyTypes.Contains(s.EntityType)).ToListAsync(ct);

        var planIds = plans.Select(p => p.Id).ToList();
        var records = await db.PlanSubscriptions.AsNoTracking()
            .Where(r => !r.IsDeleted && planIds.Contains(r.BillingPlanId) && r.Status == PlanSubscriptionStatuses.Active && r.StartsAt <= now)
            .ToListAsync(ct);

        foreach (var plan in plans)
        {
            var types = PlanCatalog.LegacyEntityTypes(plan);
            foreach (var s in legacy.Where(s => types.Contains(s.EntityType)))
            {
                var g = SubscriptionPeriodCalculator.EffectiveGrace(s, grace);
                if (!SubscriptionPeriodCalculator.IsOn(s, g, now)) continue;
                var latest = records.Where(r => r.SubscriptionId == s.Id).MaxBy(r => r.EndsAt ?? DateTime.MinValue);
                rows.Add(new Row(plan.Id, BillingProviderTypes.OfEntityType(s.EntityType)!, s.EntityId, latest?.Id, s.Id,
                    SubscriptionPeriodCalculator.StatusOf(s, g, now), s.ExpiresAt, latest?.IsTrial ?? false, null));
            }
            if (types.Count > 0) continue;

            foreach (var r in records.Where(r => r.BillingPlanId == plan.Id))
            {
                var st = PlanSubscriptionState.Compute(r, null, grace, false, now);
                if (!st.IsOn) continue;
                rows.Add(new Row(plan.Id, r.ProviderType, r.ProviderId, r.Id, null, st.Status, r.EndsAt, r.IsTrial, st.ScansRemaining));
            }
        }
        return rows;
    }
}

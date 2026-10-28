using System.Text.Json.Nodes;

namespace Application.Billing.Plans;

public record PriceOptionDto(int Id, int? Months, decimal PriceJod, int? Scans, bool IsUnlimited, bool IsActive, int SortOrder);

public record PlanFeatureDto(string Key, string Name, string NameAr, string NameEn);

/// <summary>Admin view of a plan (§2b.3).</summary>
public record PlanDto(
    int Id,
    string Key,
    string Category,
    string Name,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    List<string> AppliesTo,
    List<string> Features,
    List<int> IncludesPlanIds,
    List<string> LegacyEntityTypes,
    List<PriceOptionDto> PriceOptions,
    PointsRule? PointsRule,
    int TrialDays,
    int TrialScans,
    bool IsActive,
    bool IsVisible,
    int SortOrder,
    string? BadgeColor,
    int? DailySoftCap,
    decimal? TargetMarginPercent,
    /// <summary>Providers currently on this plan.</summary>
    int SubscriberCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>Partner view (GET /api/plans): what can be bought, no internal margin data.</summary>
public record PublicPlanDto(
    int Id,
    string Key,
    string Category,
    string Name,
    string NameAr,
    string NameEn,
    string? Description,
    List<PlanFeatureDto> Features,
    List<int> IncludesPlanIds,
    List<PriceOptionDto> PriceOptions,
    PointsRule? PointsRule,
    int TrialDays,
    int TrialScans,
    int SortOrder,
    string? BadgeColor);

public record PriceOptionInput(int? Id, int? Months, decimal PriceJod, int? Scans, int? SortOrder = null);

/// <summary>Create / update body. Key and category are fixed after creation.</summary>
public record PlanInput(
    string Key,
    string Category,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    List<string>? AppliesTo,
    List<string>? Features,
    List<int>? IncludesPlanIds,
    List<string>? LegacyEntityTypes,
    List<PriceOptionInput>? PriceOptions,
    PointsRule? PointsRule,
    int TrialDays = 0,
    int TrialScans = 0,
    bool IsActive = true,
    bool IsVisible = true,
    int SortOrder = 0,
    string? BadgeColor = null,
    int? DailySoftCap = null,
    decimal? TargetMarginPercent = null,
    /// <summary>Update only: why the plan changed (kept in the change log).</summary>
    string? Note = null);

public record PlanChange(string Field, JsonNode? From = null, JsonNode? To = null, List<string>? Added = null, List<string>? Removed = null);

public record PlanChangeLogDto(int Id, int PlanId, int? ChangedByUserId, string? ChangedByName, DateTime ChangedAt, List<PlanChange> Changes, string? Note);

public record PlanSaveResult(
    PlanDto Plan,
    List<PlanChange> Changes,
    /// <summary>Feature edits apply at once to this many current subscribers.</summary>
    int AffectedSubscribers,
    /// <summary>True when price / rule edits were made — they apply to new subscriptions and renewals only.</summary>
    bool PricingChangedForNewOnly);

public static class PlanDtoMapper
{
    public static PriceOptionDto Map(BillingPlanPriceOption o) =>
        new(o.Id, o.Months, o.PriceJod, o.Scans, o.Scans is null && o.Months is not null, o.IsActive, o.SortOrder);

    public static PlanDto Map(BillingPlan p, int subscribers) => new(
        p.Id, p.Key, p.Category, PlanCatalog.Name(p), p.NameAr, p.NameEn, p.DescriptionAr, p.DescriptionEn,
        PlanCatalog.AppliesTo(p), PlanCatalog.Features(p), PlanCatalog.Includes(p), PlanCatalog.LegacyEntityTypes(p),
        p.PriceOptions.Where(o => !o.IsDeleted).OrderBy(o => o.SortOrder).ThenBy(o => o.Id).Select(Map).ToList(),
        BillingJson.ReadRule(p.PointsRule),
        p.TrialDays, p.TrialScans, p.IsActive, p.IsVisible, p.SortOrder, p.BadgeColor, p.DailySoftCap, p.TargetMarginPercent,
        subscribers, p.CreatedAt, p.ModifiedAt);

    public static PublicPlanDto MapPublic(BillingPlan p) => new(
        p.Id, p.Key, p.Category, PlanCatalog.Name(p), p.NameAr, p.NameEn,
        Cable.Core.Utilities.Lang.Pick(p.DescriptionAr ?? p.DescriptionEn ?? "", p.DescriptionEn ?? p.DescriptionAr ?? "") is { Length: > 0 } d ? d : null,
        PlanCatalog.Features(p).Where(PlanFeatureCatalog.IsKnown).Select(k =>PlanFeatureCatalog.All.First(f => f.Key == k)).Select(f => new PlanFeatureDto(f.Key, PlanFeatureCatalog.Name(f.Key), f.NameAr, f.NameEn)).ToList(),
        PlanCatalog.Includes(p),
        p.PriceOptions.Where(o => !o.IsDeleted && o.IsActive).OrderBy(o => o.SortOrder).ThenBy(o => o.Id).Select(Map).ToList(),
        BillingJson.ReadRule(p.PointsRule),
        p.TrialDays, p.TrialScans, p.SortOrder, p.BadgeColor);
}

using Cable.Core.Constants;
using Cable.Core.Utilities;

namespace Application.Billing;

public static class PlanCategories
{
    public const string Subscription = "Subscription";
    public const string Loyalty = "Loyalty";
    public static readonly IReadOnlyList<string> All = [Subscription, Loyalty];
    public static string? Normalize(string? v) => All.FirstOrDefault(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Keys of the seeded plans the code refers to. Everything else about them is data.</summary>
public static class PlanKeys
{
    /// <summary>The default plan for anyone without an active subscription. A real row (decision 18).</summary>
    public const string Free = "free";
    public const string Premium = "premium";
    public const string Connect = "connect";
}

public static class BillingModels
{
    /// <summary>Commission per transaction — the default, and what every existing agreement stays on.</summary>
    public const string Percentage = "percentage";
    /// <summary>Prepaid scans from a Loyalty plan.</summary>
    public const string Bundle = "bundle";
    public static bool IsValid(string? v) => v is Percentage or Bundle;
    public static string Normalize(string? v) => string.Equals(v, Bundle, StringComparison.OrdinalIgnoreCase) ? Bundle : Percentage;
}

public static class BillingProviderTypes
{
    public const string ChargingPoint = "ChargingPoint";
    public const string ServiceProvider = "ServiceProvider";
    public static readonly IReadOnlyList<string> All = [ChargingPoint, ServiceProvider];
    public static bool IsValid(string? v) => v is ChargingPoint or ServiceProvider;

    /// <summary>Which provider type an existing Subscription.EntityType belongs to (null for banners).</summary>
    public static string? OfEntityType(string entityType) => entityType switch
    {
        SubscriptionEntityTypes.StationPremium or SubscriptionEntityTypes.OcppConnect => ChargingPoint,
        SubscriptionEntityTypes.ServiceProviderPremium => ServiceProvider,
        _ => null
    };

    /// <summary>The existing entity types a provider can be billed through.</summary>
    public static IReadOnlyList<string> EntityTypesFor(string providerType) => providerType switch
    {
        ChargingPoint => [SubscriptionEntityTypes.StationPremium, SubscriptionEntityTypes.OcppConnect],
        ServiceProvider => [SubscriptionEntityTypes.ServiceProviderPremium],
        _ => []
    };
}

/// <summary>
/// Stored statuses of a PlanSubscription (Active / Cancelled / Upgraded / Renewed) and the computed
/// ones returned to clients (ExpiringSoon / InGrace / Expired / Scheduled / Exhausted).
/// </summary>
public static class PlanSubscriptionStatuses
{
    public const string Active = "Active";
    public const string ExpiringSoon = "ExpiringSoon";
    public const string InGrace = "InGrace";
    public const string Expired = "Expired";
    public const string SwitchedOff = "SwitchedOff";
    public const string Cancelled = "Cancelled";
    /// <summary>Replaced mid-period by an upgrade; the remaining value was credited.</summary>
    public const string Upgraded = "Upgraded";
    /// <summary>A bundle replaced by the next one; its leftover scans were carried over.</summary>
    public const string Renewed = "Renewed";
    /// <summary>A renewal that starts when the current period ends.</summary>
    public const string Scheduled = "Scheduled";
    /// <summary>A scan bundle with no scans left.</summary>
    public const string Exhausted = "Exhausted";

    public static bool IsTerminal(string s) => s is Cancelled or Upgraded or Renewed;
}

public static class PlanSubscriptionSources
{
    public const string Payment = "payment";
    public const string Admin = "admin";
    public const string Trial = "trial";
    public const string Bundle = "bundle";
    public const string Upgrade = "upgrade";
    public const string Migration = "migration";
}

public static class UpgradeRequestStatuses
{
    public const string Pending = "Pending";
    public const string Fulfilled = "Fulfilled";
    public const string Rejected = "Rejected";
}

public static class UpgradeRequestKinds
{
    public const string Upgrade = "upgrade";
    public const string Renew = "renew";
    public const string Bundle = "bundle";
}

/// <summary>Stable error codes (409 / 422 "code" field) the apps and the admin switch on.</summary>
public static class BillingErrorCodes
{
    public const string PlanKeyTaken = "PLAN_KEY_TAKEN";
    public const string InvalidTiers = "INVALID_TIERS";
    public const string UnknownFeature = "UNKNOWN_FEATURE";
    public const string PlanArchived = "PLAN_ARCHIVED";
    public const string TrialAlreadyUsed = "TRIAL_ALREADY_USED";
    public const string TrialNotAvailable = "TRIAL_NOT_AVAILABLE";
    public const string ActiveSubscriptionExists = "ACTIVE_SUBSCRIPTION_EXISTS";
    public const string UpgradeRequestPending = "UPGRADE_REQUEST_PENDING";
    public const string PlanNotApplicable = "PLAN_NOT_APPLICABLE";
    public const string InvalidPriceOption = "INVALID_PRICE_OPTION";
    public const string SubscriptionNotActive = "SUBSCRIPTION_NOT_ACTIVE";
    /// <summary>The partner creates a new code with 0 scans left (camelCase, as the bundle document names it).</summary>
    public const string BundleExhausted = "bundleExhausted";
    public const string MinimumAmountRequired = "MINIMUM_AMOUNT_REQUIRED";
}

/// <summary>
/// The fixed catalog of feature keys (§2.3). Which plan has which feature is data (admin matrix);
/// the keys themselves are code, because the apps switch on them.
/// </summary>
public static class PlanFeatureCatalog
{
    public record Feature(string Key, string NameAr, string NameEn);

    public static readonly IReadOnlyList<Feature> All =
    [
        new("verifiedBadge", "شارة موثّقة", "Verified badge"),
        new("editInfo", "تعديل المعلومات", "Edit information"),
        new("appearFirst", "الظهور أولاً بالخريطة", "Appear first on the map"),
        new("images", "إدارة الصور", "Manage photos"),
        new("complaints", "الشكاوى والتقييمات", "Complaints and ratings"),
        new("analytics", "الإحصائيات المتقدّمة", "Advanced analytics"),
        new("workers", "الموظفين والدوام", "Employees and attendance"),
        new("biometrics", "الدخول بالبصمة", "Biometric sign-in"),
        new("connect", "كيبل كونكت", "Cable Connect"),
    ];

    public static bool IsKnown(string key) => All.Any(f => f.Key == key);
    public static string Name(string key) => All.FirstOrDefault(f => f.Key == key) is { } f ? Lang.Pick(f.NameAr, f.NameEn) : key;
}

/// <summary>AppSetting keys for partner billing, with their defaults.</summary>
public static class BillingSettings
{
    /// <summary>Points per 1 JOD of value (decision 10: 1,000 points = 1 JOD). Margin maths depends on it.</summary>
    public const string PointsPerJodKey = "Billing.PointsPerJod";
    /// <summary>Only when there is neither a Billing.PointsPerJod row nor a default PointsConversionRate (the real source: 50 points = 1 JOD).</summary>
    public const double DefaultPointsPerJod = 50;

    /// <summary>Cable's target margin on loyalty plans (decision 19: 20%).</summary>
    public const string TargetMarginPercentKey = "Billing.TargetMarginPercent";
    public const decimal DefaultTargetMarginPercent = 20m;

    /// <summary>Minimum transaction amount enforced on bundle-billed agreements when the agreement has none (decision 12).</summary>
    public const string BundleMinimumAmountKey = "Billing.BundleMinimumTransactionAmount";
    public const decimal DefaultBundleMinimumAmount = 2m;

    /// <summary>Bundle push thresholds, percent of scans remaining.</summary>
    public static readonly int[] BundleThresholds = [20, 10, 0];
}

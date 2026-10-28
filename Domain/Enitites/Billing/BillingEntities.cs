using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// A plan Cable sells to partners (Sprint 5 partner billing, §2b). Plans are data managed
/// by the admin — created, edited, archived, never deleted and never hard-coded.
///
/// Category "Subscription" sells features for a period (free / premium / connect);
/// category "Loyalty" sells loyalty scans and points (scan bundles, unlimited scanning).
///
/// List-like values (appliesTo, features, includesPlanIds, pointsRule) are stored as JSON
/// strings: they are always read and written as a whole with the plan, never queried
/// one item at a time.
/// </summary>
public class BillingPlan : BaseAuditableEntity
{
    /// <summary>Stable code, unique, fixed after creation ("free", "premium", "connect", "loyalty_unlimited"…).</summary>
    public string Key { get; set; } = null!;

    /// <summary>PlanCategories: "Subscription" | "Loyalty".</summary>
    public string Category { get; set; } = null!;

    public string NameAr { get; set; } = null!;
    public string NameEn { get; set; } = null!;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }

    /// <summary>JSON array of provider types: ["ChargingPoint","ServiceProvider"].</summary>
    public string AppliesTo { get; set; } = "[]";

    /// <summary>JSON array of feature keys from the fixed catalog (PlanFeatureCatalog). Subscription plans.</summary>
    public string Features { get; set; } = "[]";

    /// <summary>JSON array of plan ids this plan includes (connect includes premium).</summary>
    public string IncludesPlanIds { get; set; } = "[]";

    /// <summary>JSON points rule (Fixed | Tiered). Loyalty plans only; null otherwise.</summary>
    public string? PointsRule { get; set; }

    /// <summary>
    /// CSV of the existing Subscription.EntityType values this plan is sold through
    /// ("StationPremium,ServiceProviderPremium" for premium, "OcppConnect" for connect).
    /// The existing subscription engine (expiry, grace, switch-off, payments, receipts) keeps
    /// driving those; null = a plan that lives only in PlanSubscription records.
    /// </summary>
    public string? LegacyEntityTypes { get; set; }

    /// <summary>0 = no trial.</summary>
    public int TrialDays { get; set; }
    /// <summary>Loyalty plans: scans granted by the trial instead of an open period. 0 = none.</summary>
    public int TrialScans { get; set; }

    /// <summary>False = archived: not sold to new subscribers; existing subscribers are unaffected.</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>Shown in the partner's plan comparison.</summary>
    public bool IsVisible { get; set; } = true;
    public int SortOrder { get; set; }
    public string? BadgeColor { get; set; }

    /// <summary>Unlimited loyalty plans: scans per station per day above which the admin is alerted. Never stops scanning.</summary>
    public int? DailySoftCap { get; set; }
    /// <summary>The margin the admin aims for on this plan (margin calculator default). Null = the global default.</summary>
    public decimal? TargetMarginPercent { get; set; }

    public virtual ICollection<BillingPlanPriceOption> PriceOptions { get; set; } = new List<BillingPlanPriceOption>();
}

/// <summary>
/// One way to buy a plan: a period (<see cref="Months"/>), a number of scans (<see cref="Scans"/>),
/// or both (whichever runs out first). Null scans on a loyalty plan = unlimited scanning.
/// Removed options are deactivated, not deleted — subscription records keep a snapshot anyway.
/// </summary>
public class BillingPlanPriceOption : BaseAuditableEntity
{
    public int BillingPlanId { get; set; }
    public int? Months { get; set; }
    public decimal PriceJod { get; set; }
    public int? Scans { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public virtual BillingPlan Plan { get; set; } = null!;
}

/// <summary>One saved edit of a plan, with field-level differences (JSON array of changes).</summary>
public class BillingPlanChangeLog : BaseEntity
{
    public int BillingPlanId { get; set; }
    public int? ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; }
    /// <summary>JSON: [{ field, from, to }] or [{ field: "features", added: [], removed: [] }].</summary>
    public string Changes { get; set; } = "[]";
    public string? Note { get; set; }
}

/// <summary>
/// One subscription, renewal, trial or bundle purchase — a record, never overwritten (§2b.7).
/// The plan's name, price option and points rule are copied at the moment of purchase, so a
/// later edit of the plan never changes what this partner bought.
///
/// For plans sold through the existing engine (<see cref="BillingPlan.LegacyEntityTypes"/>),
/// <see cref="SubscriptionId"/> points at that engine's Subscription row and
/// <see cref="PaymentId"/> at the recorded payment; whether the features are on is still
/// computed from the Subscription row (grace and switch-off rules unchanged).
/// </summary>
public class PlanSubscription : BaseAuditableEntity
{
    public string ProviderType { get; set; } = null!;
    public int ProviderId { get; set; }

    public int BillingPlanId { get; set; }
    public string PlanKey { get; set; } = null!;
    public string PlanCategory { get; set; } = null!;
    public string PlanNameAr { get; set; } = null!;
    public string PlanNameEn { get; set; } = null!;

    public int? PriceOptionId { get; set; }
    /// <summary>JSON { months, priceJod, scans } at purchase.</summary>
    public string? PriceOptionSnapshot { get; set; }
    /// <summary>JSON points rule at purchase (loyalty plans).</summary>
    public string? PointsRuleSnapshot { get; set; }

    public bool IsTrial { get; set; }
    public DateTime StartsAt { get; set; }
    /// <summary>Null for a scan bundle with no time limit.</summary>
    public DateTime? EndsAt { get; set; }

    /// <summary>Null = unlimited scanning (or not a loyalty plan).</summary>
    public int? ScansIncluded { get; set; }
    /// <summary>Incremented atomically when a bundle-billed partner transaction completes.</summary>
    public int ScansUsed { get; set; }
    /// <summary>Scans carried over from the previous bundle (already included in <see cref="ScansIncluded"/>).</summary>
    public int ScansCarriedOver { get; set; }

    /// <summary>Stored part of the status: Active | Cancelled | Upgraded | Renewed. The rest is computed.</summary>
    public string Status { get; set; } = "Active";

    public decimal PricePaidJod { get; set; }
    /// <summary>Upgrade credit for the remaining days / scans of the record it replaced (decision 16).</summary>
    public decimal CreditAppliedJod { get; set; }

    public int? PaymentId { get; set; }
    public int? SubscriptionId { get; set; }
    public int? WalletTransactionId { get; set; }
    public int? UpgradedFromId { get; set; }

    /// <summary>payment | admin | trial | bundle | upgrade | migration.</summary>
    public string Source { get; set; } = "admin";

    public int? CreatedByUserId { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public int? CancelledByUserId { get; set; }
    public string? Note { get; set; }

    public virtual BillingPlan Plan { get; set; } = null!;
}

/// <summary>
/// A partner's "upgrade / renew / buy a bundle" request (§2.5). No online payment: Cable's team
/// follows up, records the payment with the existing flow, and that fulfils the request.
/// </summary>
public class PlanUpgradeRequest : BaseAuditableEntity
{
    public string ProviderType { get; set; } = null!;
    public int ProviderId { get; set; }
    public int BillingPlanId { get; set; }
    public string PlanKey { get; set; } = null!;
    public int? PriceOptionId { get; set; }
    public int? Months { get; set; }
    /// <summary>upgrade | renew | bundle.</summary>
    public string Kind { get; set; } = "upgrade";
    public string? Note { get; set; }
    /// <summary>Pending | Fulfilled | Rejected.</summary>
    public string Status { get; set; } = "Pending";
    public int RequestedByUserId { get; set; }
    public int? HandledByUserId { get; set; }
    public DateTime? HandledAt { get; set; }
    public string? RejectReason { get; set; }
    public int? FulfilledPaymentId { get; set; }
    public int? FulfilledPlanSubscriptionId { get; set; }
}

/// <summary>
/// Every billing push and admin alert, keyed so that the same event is never sent twice
/// (unique <see cref="EventKey"/>). Admin-audience rows are the admin's billing alerts list
/// (soft cap exceeded, break-even passed, new upgrade request).
/// </summary>
public class BillingEvent : BaseEntity
{
    public string EventKey { get; set; } = null!;
    public string Kind { get; set; } = null!;
    /// <summary>owner | admin.</summary>
    public string Audience { get; set; } = "owner";
    public string? ProviderType { get; set; }
    public int? ProviderId { get; set; }
    public int? PlanSubscriptionId { get; set; }
    public int? SubscriptionId { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public int? AcknowledgedByUserId { get; set; }
}

/// <summary>
/// Marks a partner transaction as billed on a scan bundle (created with the code) and records
/// the scan it consumed when it completed. A side table rather than columns on PartnerTransaction
/// so percentage-billed transactions — every transaction before this feature — are untouched.
/// </summary>
public class PartnerTransactionBundleScan : BaseEntity
{
    public int PartnerTransactionId { get; set; }
    /// <summary>The bundle that was active when the code was created.</summary>
    public int? PlanSubscriptionId { get; set; }
    /// <summary>The bundle the scan was actually taken from at completion (may be a renewal).</summary>
    public int? ConsumedPlanSubscriptionId { get; set; }
    public DateTime? ConsumedAt { get; set; }
    /// <summary>True when the code completed with no scan left: the customer got no points.</summary>
    public bool QuotaReached { get; set; }
    public DateTime? RefundedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual PartnerTransaction PartnerTransaction { get; set; } = null!;
}

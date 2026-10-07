using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// One paid subscription per billable thing — a station's premium placement, a
/// banner's paid run, later a service provider's premium — keyed by
/// <see cref="EntityType"/> + <see cref="EntityId"/>. Evolved from the old
/// StationPremiumSubscription (same table, renamed) so premium history carried
/// over instead of being rebuilt beside it.
///
/// Money lives on <see cref="Payment"/>; this row is the period. <see cref="ExpiresAt"/>
/// is the end of the latest non-void payment's period and is maintained on record
/// and void — it is a cache of the payments, not a second source of truth.
/// Whether the thing is currently "on" is computed from ExpiresAt, the grace
/// rule and <see cref="IsSwitchedOff"/>; there is deliberately no stored status.
/// </summary>
public class Subscription : BaseAuditableEntity
{
    /// <summary>See SubscriptionEntityTypes: "StationPremium" | "Banner" | "ServiceProviderPremium".</summary>
    public string EntityType { get; set; } = null!;
    public int EntityId { get; set; }

    /// <summary>Plan length of the latest payment. Null for legacy rows recorded with an explicit expiry.</summary>
    public int? PlanMonths { get; set; }

    /// <summary>Start of the first period (UTC).</summary>
    public DateTime StartDate { get; set; }

    /// <summary>End of the latest non-void period (UTC).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Per-subscription override of the global grace rule (SubscriptionGraceMode).
    /// Null inherits the AppSetting default. Manual = stays on until an admin
    /// switches it off; AfterDays = the daily job switches it off GraceDays after expiry.
    /// </summary>
    public int? GraceMode { get; set; }
    public int? GraceDays { get; set; }

    /// <summary>The admin's kill switch — the only thing that turns a manual-grace subscription off.</summary>
    public bool IsSwitchedOff { get; set; }
    public DateTime? SwitchedOffAt { get; set; }
    /// <summary>Null when the daily grace job did it.</summary>
    public int? SwitchedOffByUserId { get; set; }

    public string? Note { get; set; }

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

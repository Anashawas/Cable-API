using Cable.Core.Enums;
using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// A promo campaign: what the promo does (<see cref="EffectType"/>), where it applies (stations / service
/// providers / categories / cities / minimum amount), who pays for a discount (<see cref="CableSharePercent"/>)
/// and its limits. Created by admins only. See Docs/Sprint5-GiftsAndPromos.md.
/// </summary>
public class PromoCampaign : BaseAuditableEntity
{
    public string TitleAr { get; set; } = null!;
    public string TitleEn { get; set; } = null!;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }

    /// <summary>Optional "where it applies" text for the card ("at Irbid car washes"); a generic one is used when empty.</summary>
    public string? ScopeLabelAr { get; set; }
    public string? ScopeLabelEn { get; set; }

    /// <summary>0–100: the part of a discount Cable pays the partner. 100 = the partner gets his full price.</summary>
    public decimal CableSharePercent { get; set; }

    /// <summary>Cap on Cable's total share of discounts; null = no cap. The campaign stops when reached.</summary>
    public decimal? CableBudgetJod { get; set; }

    /// <summary>Sum of CableShareJod of every use so far.</summary>
    public decimal CableSpentJod { get; set; }

    public PromoTargetScope StationsScope { get; set; }

    /// <summary>Comma-separated ChargingPoint ids when <see cref="StationsScope"/> is Selected.</summary>
    public string? StationIds { get; set; }

    public PromoTargetScope ServiceProvidersScope { get; set; }

    /// <summary>Comma-separated ServiceProvider ids when <see cref="ServiceProvidersScope"/> is Selected.</summary>
    public string? ServiceProviderIds { get; set; }

    public PromoEffectType EffectType { get; set; }

    /// <summary>Percent (1–100), JOD, multiplier (e.g. 2) or points, by <see cref="EffectType"/>.</summary>
    public decimal EffectValue { get; set; }

    /// <summary>Cap on the discount of one use, in JOD.</summary>
    public decimal? MaxDiscountJod { get; set; }

    /// <summary>Comma-separated transaction kinds; version 1 only "PartnerScan".</summary>
    public string AppliesTo { get; set; } = "PartnerScan";

    /// <summary>Comma-separated ServiceCategory ids; empty = every category.</summary>
    public string? ServiceCategoryIds { get; set; }

    /// <summary>Comma-separated canonical city names of the provider; empty = every city.</summary>
    public string? Cities { get; set; }

    public decimal? MinAmountJod { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? StartsAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? EndsAt { get; set; }

    /// <summary>The promo expires this many days after the user adds it (never after <see cref="EndsAt"/>).</summary>
    public int? ValidDaysAfterClaim { get; set; }

    /// <summary>Cap on how many users can add the code; null = no cap.</summary>
    public int? TotalLimit { get; set; }

    /// <summary>How many times one user can have (and use) this promo. Usually 1.</summary>
    public int PerUserLimit { get; set; } = 1;

    /// <summary>Only accounts created within the last 30 days.</summary>
    public bool NewUsersOnly { get; set; }

    public PromoCodeMode CodeMode { get; set; }

    /// <summary>The admin-chosen code (CABLE20) for Public mode, as typed.</summary>
    public string? PublicCode { get; set; }

    /// <summary>Upper case, no spaces or dashes — what claims are matched on.</summary>
    public string? PublicCodeNormalized { get; set; }

    /// <summary>Users who added it with a code (assignments are not counted against <see cref="TotalLimit"/>).</summary>
    public int ClaimsCount { get; set; }

    public int UsesCount { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Why the server switched it off ("BudgetExhausted"); null when an admin did or it is active.</summary>
    public string? DeactivatedReason { get; set; }

    public virtual ICollection<PromoCode> Codes { get; set; } = new List<PromoCode>();
    public virtual ICollection<UserPromo> UserPromos { get; set; } = new List<UserPromo>();
}

/// <summary>One unique code of a Unique-mode campaign ("PRM-" + 8 characters). One user only.</summary>
public class PromoCode : BaseEntity
{
    public int PromoCampaignId { get; set; }
    public string Code { get; set; } = null!;
    public string NormalizedCode { get; set; } = null!;
    public int? ClaimedByUserId { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Codes generated in the same call share a batch.</summary>
    public Guid? BatchId { get; set; }

    public virtual PromoCampaign Campaign { get; set; } = null!;
}

/// <summary>A promo in a user's "My codes". Single use: Available → Used (or Expired / Revoked).</summary>
public class UserPromo : BaseAuditableEntity
{
    public int UserId { get; set; }
    public int PromoCampaignId { get; set; }
    public int? PromoCodeId { get; set; }
    public UserPromoSource Source { get; set; }
    public DateTime ClaimedAt { get; set; }

    /// <summary>UTC; null = as long as the campaign runs.</summary>
    public DateTime? ExpiresAt { get; set; }

    public UserPromoStatus Status { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public int? RevokedByUserId { get; set; }
    public string? RevokeReason { get; set; }

    /// <summary>When the "expires in two days" push went out; never twice.</summary>
    public DateTime? ExpiryReminderSentAt { get; set; }

    /// <summary>Rows created by one admin assignment share it, so the push job finds them.</summary>
    public Guid? AssignmentBatchId { get; set; }

    public virtual PromoCampaign Campaign { get; set; } = null!;
    public virtual PromoCode? PromoCode { get; set; }
    public virtual UserAccount User { get; set; } = null!;
}

/// <summary>
/// One use of a promo — the base of the reports and of the settlement line "discounts paid by Cable".
/// All figures are snapshotted when used, so editing the campaign later changes nothing here.
/// </summary>
public class PromoUsage : BaseEntity
{
    public int UserPromoId { get; set; }
    public int PromoCampaignId { get; set; }
    public int UserId { get; set; }

    /// <summary>"PartnerScan" in version 1.</summary>
    public string TransactionType { get; set; } = null!;
    public int TransactionId { get; set; }
    public string ProviderType { get; set; } = null!;
    public int ProviderId { get; set; }

    public decimal OriginalAmountJod { get; set; }
    public decimal DiscountJod { get; set; }
    public decimal FinalAmountJod { get; set; }

    /// <summary>Owed by Cable to the partner.</summary>
    public decimal CableShareJod { get; set; }

    /// <summary>The partner's own part of the discount (reports only).</summary>
    public decimal ProviderShareJod { get; set; }

    /// <summary>Commission reserved when the code was issued, and the commission after the promo.</summary>
    public decimal CommissionBeforeJod { get; set; }
    public decimal CommissionAfterJod { get; set; }

    /// <summary>Points on the final amount before any multiplier.</summary>
    public int BasePoints { get; set; }

    /// <summary>Points the promo itself added (multiplier above the boost, or bonus points).</summary>
    public int BonusPoints { get; set; }

    public int FinalPoints { get; set; }

    public int? SettledInSettlementId { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual UserPromo UserPromo { get; set; } = null!;
    public virtual PromoCampaign Campaign { get; set; } = null!;
}

/// <summary>A wrong code typed in "Add code" — at most 10 per user per hour.</summary>
public class PromoClaimAttempt : BaseEntity
{
    public int UserId { get; set; }
    public string CodeTried { get; set; } = null!;
    public DateTime AttemptedAt { get; set; }
}

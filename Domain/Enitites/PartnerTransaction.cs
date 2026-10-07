using Domain.Common;

namespace Domain.Enitites;

public class PartnerTransaction : BaseAuditableEntity
{
    public int PartnerAgreementId { get; set; }
    public int? UserId { get; set; }
    public string TransactionCode { get; set; } = null!;
    public int Status { get; set; }
    public string ProviderType { get; set; } = null!;
    public int ProviderId { get; set; }
    public decimal? TransactionAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public double CommissionPercentage { get; set; }
    public decimal? CommissionAmount { get; set; }
    public double PointsRewardPercentage { get; set; }
    public double PointsConversionRate { get; set; }
    public decimal? PointsEligibleAmount { get; set; }
    public int? PointsAwarded { get; set; }

    /// <summary>
    /// Points before any boost. Set alongside <see cref="PointsAwarded"/> so the
    /// bonus is derivable as the difference, with no counter table.
    /// </summary>
    public int? BasePoints { get; set; }

    /// <summary>The boost applied at scan, if any. Never cascade-deleted.</summary>
    public int? AppliedBoostId { get; set; }

    /// <summary>
    /// Snapshot of the multiplier at the moment it was applied. Stored rather
    /// than read back through <see cref="AppliedBoostId"/> so that editing a
    /// campaign later cannot silently rewrite historical figures.
    /// </summary>
    public double? BoostMultiplier { get; set; }

    /// <summary>
    /// True when the multiplier came from the welcome bonus rather than a
    /// campaign. Stated explicitly rather than inferred from a null
    /// <see cref="AppliedBoostId"/>, so "what has the welcome bonus cost us"
    /// stays a direct question and does not depend on that inference holding.
    /// </summary>
    public bool IsWelcomeBonus { get; set; }

    public virtual LoyaltyBoost? AppliedBoost { get; set; }
    public int? ConfirmedByUserId { get; set; }
    public DateTime CodeExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public decimal WalletCoveredAmount { get; set; }

    public virtual PartnerAgreement Agreement { get; set; } = null!;
    public virtual UserAccount? User { get; set; }
    public virtual UserAccount? ConfirmedByUser { get; set; }
}

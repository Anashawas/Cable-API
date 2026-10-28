using Domain.Common;

namespace Domain.Enitites;

public class PartnerAgreement : BaseAuditableEntity
{
    public string ProviderType { get; set; } = null!;
    public int ProviderId { get; set; }
    public double CommissionPercentage { get; set; }
    public double PointsRewardPercentage { get; set; }
    public int? PointsConversionRateId { get; set; }
    public decimal? MinimumTransactionAmount { get; set; }
    public int CodeExpirySeconds { get; set; } = 60;
    public bool IsActive { get; set; }
    public string? Note { get; set; }

    /// <summary>
    /// How Cable bills this partner for loyalty (Sprint 5, §3): "percentage" (commission per
    /// transaction — the default and what every existing agreement stays on) or "bundle"
    /// (prepaid scans from a Loyalty plan). Changed only by an admin.
    /// </summary>
    public string BillingModel { get; set; } = "percentage";
    public DateTime? BillingModelChangedAt { get; set; }

    public virtual PointsConversionRate? ConversionRate { get; set; }
    public virtual ICollection<PartnerTransaction> Transactions { get; set; } = new List<PartnerTransaction>();
}

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
    public int? ConfirmedByUserId { get; set; }
    public DateTime CodeExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public virtual PartnerAgreement Agreement { get; set; } = null!;
    public virtual UserAccount? User { get; set; }
    public virtual UserAccount? ConfirmedByUser { get; set; }
}

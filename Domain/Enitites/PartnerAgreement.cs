using Domain.Common;

namespace Domain.Enitites;

public class PartnerAgreement : BaseAuditableEntity
{
    public string ProviderType { get; set; } = null!;
    public int ProviderId { get; set; }
    public double CommissionPercentage { get; set; }
    public double PointsRewardPercentage { get; set; }
    public int? PointsConversionRateId { get; set; }
    public int CodeExpiryMinutes { get; set; } = 30;
    public bool IsActive { get; set; }
    public string? Note { get; set; }

    public virtual PointsConversionRate? ConversionRate { get; set; }
    public virtual ICollection<PartnerTransaction> Transactions { get; set; } = new List<PartnerTransaction>();
}

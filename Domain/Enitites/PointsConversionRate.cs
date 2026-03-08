using Domain.Common;

namespace Domain.Enitites;

public class PointsConversionRate : BaseAuditableEntity
{
    public string Name { get; set; } = null!;
    public string CurrencyCode { get; set; } = null!;
    public double PointsPerUnit { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }

    public virtual ICollection<PartnerAgreement> PartnerAgreements { get; set; } = new List<PartnerAgreement>();
}

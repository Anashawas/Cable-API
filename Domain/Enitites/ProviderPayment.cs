using Domain.Common;

namespace Domain.Enitites;

public class ProviderPayment : BaseAuditableEntity
{
    public string ProviderType { get; set; } = null!;
    public int ProviderId { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public int RecordedByUserId { get; set; }
    public virtual UserAccount RecordedByUser { get; set; } = null!;
}

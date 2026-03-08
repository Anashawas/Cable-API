using Domain.Common;

namespace Domain.Enitites;

public class LoyaltyPointAction : BaseAuditableEntity
{
    public string ActionCode { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int Points { get; set; }
    public int? MaxPerDay { get; set; }
    public int? MaxPerLifetime { get; set; }
    public bool IsActive { get; set; }

    public virtual ICollection<LoyaltyPointTransaction> Transactions { get; set; } = new List<LoyaltyPointTransaction>();
}

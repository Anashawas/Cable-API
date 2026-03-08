using Domain.Common;

namespace Domain.Enitites;

public class LoyaltySeason : BaseAuditableEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; }

    public virtual ICollection<UserSeasonProgress> UserProgresses { get; set; } = new List<UserSeasonProgress>();
    public virtual ICollection<LoyaltyPointTransaction> Transactions { get; set; } = new List<LoyaltyPointTransaction>();
}

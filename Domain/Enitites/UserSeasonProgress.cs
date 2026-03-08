using Domain.Common;

namespace Domain.Enitites;

public class UserSeasonProgress : BaseAuditableEntity
{
    public int UserId { get; set; }
    public int LoyaltySeasonId { get; set; }
    public int SeasonPointsEarned { get; set; }
    public int TierLevel { get; set; } = 1; // Default Bronze

    public virtual UserAccount User { get; set; } = null!;
    public virtual LoyaltySeason Season { get; set; } = null!;
    public virtual LoyaltyTier Tier { get; set; } = null!;
}

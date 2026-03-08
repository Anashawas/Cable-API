using Domain.Common;

namespace Domain.Enitites;

public class LoyaltyReward : BaseAuditableEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int PointsCost { get; set; }
    public int RewardType { get; set; }
    public string? RewardValue { get; set; }
    public string? ProviderType { get; set; }
    public int? ProviderId { get; set; }
    public int? ServiceCategoryId { get; set; }
    public int? MaxRedemptions { get; set; }
    public int CurrentRedemptions { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    public virtual ServiceCategory? ServiceCategory { get; set; }
    public virtual ICollection<UserRewardRedemption> Redemptions { get; set; } = new List<UserRewardRedemption>();
}

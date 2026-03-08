using Domain.Common;

namespace Domain.Enitites;

public class UserRewardRedemption : BaseAuditableEntity
{
    public int UserId { get; set; }
    public int LoyaltyRewardId { get; set; }
    public int LoyaltyPointTransactionId { get; set; }
    public int PointsSpent { get; set; }
    public int Status { get; set; }
    public string? RedemptionCode { get; set; }
    public string? ProviderType { get; set; }
    public int? ProviderId { get; set; }
    public DateTime RedeemedAt { get; set; }
    public DateTime? FulfilledAt { get; set; }

    public virtual UserAccount User { get; set; } = null!;
    public virtual LoyaltyReward Reward { get; set; } = null!;
    public virtual LoyaltyPointTransaction Transaction { get; set; } = null!;
}

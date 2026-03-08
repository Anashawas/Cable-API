using Domain.Common;

namespace Domain.Enitites;

public class LoyaltyPointTransaction : BaseAuditableEntity
{
    public int UserLoyaltyAccountId { get; set; }
    public int? LoyaltyPointActionId { get; set; }
    public int? LoyaltySeasonId { get; set; }
    public int TransactionType { get; set; }
    public int Points { get; set; }
    public int BalanceAfter { get; set; }
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public string? Note { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public virtual UserLoyaltyAccount Account { get; set; } = null!;
    public virtual LoyaltyPointAction? Action { get; set; }
    public virtual LoyaltySeason? Season { get; set; }
    public virtual ICollection<UserRewardRedemption> Redemptions { get; set; } = new List<UserRewardRedemption>();
}

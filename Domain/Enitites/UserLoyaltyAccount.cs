using Domain.Common;

namespace Domain.Enitites;

public class UserLoyaltyAccount : BaseAuditableEntity
{
    public int UserId { get; set; }
    public int TotalPointsEarned { get; set; }
    public int TotalPointsRedeemed { get; set; }
    public int CurrentBalance { get; set; }

    // Blocking
    public bool IsBlocked { get; set; }
    public DateTime? BlockedAt { get; set; }
    public DateTime? BlockedUntil { get; set; }
    public string? BlockReason { get; set; }
    public int? BlockedByUserId { get; set; }

    public virtual UserAccount User { get; set; } = null!;
    public virtual UserAccount? BlockedByUser { get; set; }
    public virtual ICollection<LoyaltyPointTransaction> Transactions { get; set; } = new List<LoyaltyPointTransaction>();
}

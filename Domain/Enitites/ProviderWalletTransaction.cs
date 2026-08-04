using Domain.Common;

namespace Domain.Enitites;

public class ProviderWalletTransaction : BaseAuditableEntity
{
    public string ProviderType { get; set; } = null!;
    public int ProviderId { get; set; }
    public int TransactionType { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public string? Note { get; set; }

    /// <summary>User who triggered the movement; null for system-generated entries on unassigned providers.</summary>
    public int? RecordedByUserId { get; set; }

    public virtual UserAccount? RecordedByUser { get; set; }
}

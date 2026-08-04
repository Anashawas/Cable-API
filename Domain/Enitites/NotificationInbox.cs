using Domain.Common;

namespace Domain.Enitites;

public partial class NotificationInbox : BaseAuditableEntity
{
    public int UserId { get; set; }

    public int NotificationTypeId { get; set; }

    public string Title { get; set; } = null!;

    public string Body { get; set; } = null!;

    public bool IsRead { get; set; }

    public string? DeepLink { get; set; }

    public string? Data { get; set; }

    /// <summary>
    /// Identifier shared by all NotificationInbox rows created in the same send operation
    /// (broadcast, targeted list, or filter-based). Used to compute per-send statistics
    /// (recipients count, read count, read rate) without grouping on Title/Body/CreatedAt.
    /// Nullable for backwards compatibility with rows created before this feature.
    /// </summary>
    public Guid? BatchId { get; set; }

    public virtual UserAccount User { get; set; } = null!;

    public virtual NotificationType NotificationType { get; set; } = null!;
}

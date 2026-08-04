using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// Per-user delivery state for a welcome message — what the frequency caps
/// (maxPerDay / cooldownHours / maxLifetime / stopOnDismiss) are enforced from.
/// </summary>
public class AnnouncementUserState : BaseEntity
{
    public int AnnouncementId { get; set; }
    public int UserId { get; set; }

    public int ShownCount { get; set; }
    public int DailyShownCount { get; set; }
    public DateTime? LastShownAt { get; set; }
    public int DismissedCount { get; set; }

    public virtual Announcement Announcement { get; set; } = null!;
    public virtual UserAccount User { get; set; } = null!;
}

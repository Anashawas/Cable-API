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

    public virtual UserAccount User { get; set; } = null!;

    public virtual NotificationType NotificationType { get; set; } = null!;
}

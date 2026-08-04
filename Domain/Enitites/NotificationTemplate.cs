using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// Admin-managed body suggestion for a notification type (Part B F5).
/// The partner compose sheet shows these as chips that pre-fill the body.
/// </summary>
public class NotificationTemplate : BaseAuditableEntity
{
    public int NotificationTypeId { get; set; }

    public string Body { get; set; } = null!;

    public virtual NotificationType NotificationType { get; set; } = null!;
}

using Domain.Common;

namespace Domain.Enitites;

public partial class NotificationType 
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public virtual ICollection<NotificationInbox> NotificationInboxes { get; set; } = new List<NotificationInbox>();
}

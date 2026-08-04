using Domain.Common;

namespace Domain.Enitites;

public partial class NotificationType 
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>
    /// Routing behavior of this type (Part B R5): where a notification of this
    /// type deep-links — "none" | "charging-point" | "service-provider" |
    /// "complaint" | "loyalty" | "provider" (link decided per send by target).
    /// </summary>
    public string? DeepLinksTo { get; set; }

    /// <summary>Human display label (DECISION-2) — Name stays the stable code.</summary>
    public string? NameEn { get; set; }

    /// <summary>Arabic label; also the partner auto-title: "{NameAr} من {station}".</summary>
    public string? NameAr { get; set; }

    public virtual ICollection<NotificationInbox> NotificationInboxes { get; set; } = new List<NotificationInbox>();
}

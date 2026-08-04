using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// A provider announcement to the users who favorited it. Doubles as the
/// worker-approval queue (Part B F3): a worker's submission sits here as
/// "pending" until the owner/admin approves (→ delivered, becomes "sent")
/// or rejects it. Sent rows are immutable audit + the daily rate limit source.
/// </summary>
public class ProviderFavoriteNotification : BaseAuditableEntity
{
    /// <summary>"ChargingPoint" | "ServiceProvider"</summary>
    public string ProviderType { get; set; } = null!;
    public int ProviderId { get; set; }
    public int SentByUserId { get; set; }
    public string Title { get; set; } = null!;
    public string Body { get; set; } = null!;
    public int RecipientCount { get; set; }

    /// <summary>"pending" | "sent" | "rejected". Only "sent" rows count toward the rate limit.</summary>
    public string Status { get; set; } = "sent";

    /// <summary>Inbox type used for the send (F5 type selection); null on legacy rows.</summary>
    public int? NotificationTypeId { get; set; }

    /// <summary>Links to the NotificationInbox rows of this send — powers readCount (F1).</summary>
    public Guid? BatchId { get; set; }

    /// <summary>Devices FCM accepted the push for (F1); null until actually sent.</summary>
    public int? DeliveredCount { get; set; }

    /// <summary>Owner/admin who approved or rejected a worker submission (F3).</summary>
    public int? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }

    /// <summary>When the announcement actually went out (approval time for worker sends).</summary>
    public DateTime? SentAt { get; set; }

    public virtual UserAccount SentBy { get; set; } = null!;
}

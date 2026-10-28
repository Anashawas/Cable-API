using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// The driver's own Cable Connect preferences (LIVE §13.2). One row per user, created on the first PUT;
/// no row = every preference at its default (email receipts off).
/// </summary>
public class UserOcppSetting : BaseAuditableEntity
{
    public int UserId { get; set; }

    /// <summary>Email the receipt PDF when one of the driver's sessions ends (needs an email on the account). Default false.</summary>
    public bool EmailReceipts { get; set; }
}

/// <summary>
/// One receipt email per session (LIVE §13.2): written when it was sent or failed, so a retried job never sends it
/// twice and support can see what went out.
/// </summary>
public class OcppReceiptEmail : BaseEntity
{
    public int OcppTransactionId { get; set; }
    public int UserId { get; set; }
    public string Email { get; set; } = null!;

    /// <summary>Sent | Failed.</summary>
    public string Status { get; set; } = null!;
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
}

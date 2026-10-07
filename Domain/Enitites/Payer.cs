using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// Who paid. Reusable across payments so the admin stops retyping the same
/// person for every renewal.
///
/// When the payer is a Cable user — normally the station owner —
/// <see cref="UserAccountId"/> is set and name/phone/email are READ FROM THE
/// USER, not copied here; the columns on this row are for payers who are not
/// users (an accountant settling on the owner's behalf). Keeping one source per
/// fact is what stops an owner's phone change from silently orphaning receipts.
/// </summary>
public class Payer : BaseAuditableEntity
{
    public int? UserAccountId { get; set; }

    /// <summary>Only for non-user payers; null when <see cref="UserAccountId"/> is set.</summary>
    public string? Name { get; set; }
    /// <summary>Storage format 962…; only for non-user payers.</summary>
    public string? Phone { get; set; }
    public string? Email { get; set; }

    public bool HasWhatsApp { get; set; }
    /// <summary>Asked not to receive reminders / receipts. Honoured by every sender.</summary>
    public bool OptOut { get; set; }
    public string? Note { get; set; }

    public virtual UserAccount? UserAccount { get; set; }
    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

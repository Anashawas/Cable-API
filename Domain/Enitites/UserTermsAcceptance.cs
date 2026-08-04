using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// Immutable audit trail of terms acceptances: who accepted which TermsVersion
/// when. Rows are never updated or deleted — accepting a newer version adds a
/// new row; the old ones remain as evidence.
/// </summary>
public class UserTermsAcceptance : BaseEntity
{
    public int UserId { get; set; }
    public int TermsVersionId { get; set; }
    public DateTime AcceptedAt { get; set; }

    public virtual UserAccount User { get; set; } = null!;
    public virtual TermsVersion TermsVersion { get; set; } = null!;
}

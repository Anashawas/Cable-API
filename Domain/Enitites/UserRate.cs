using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// A provider's rating OF a driver — the reverse direction of <see cref="Rate"/>
/// (driver rates station) and <see cref="ServiceProviderRate"/>.
///
/// Every row is anchored to one completed <see cref="PartnerTransaction"/>, which is
/// the only server-side record that a specific driver was actually served at a
/// specific provider. That anchor is what makes the rating trustworthy: a provider
/// can only rate someone who transacted with them, and a unique index on
/// <see cref="PartnerTransactionId"/> caps it at one rating per visit.
/// </summary>
public class UserRate : BaseAuditableEntity
{
    /// <summary>The driver being rated (the transaction's UserId).</summary>
    public int UserId { get; set; }

    /// <summary>The provider owner, worker, or admin who left the rating.</summary>
    public int RatedByUserId { get; set; }

    /// <summary>"ChargingPoint" or "ServiceProvider" — copied from the transaction.</summary>
    public string ProviderType { get; set; } = null!;

    /// <summary>The provider the driver was served at — copied from the transaction.</summary>
    public int ProviderId { get; set; }

    /// <summary>The completed transaction this rating is for. One rating per transaction.</summary>
    public int PartnerTransactionId { get; set; }

    /// <summary>1–5.</summary>
    public int Rating { get; set; }

    /// <summary>Optional note from the provider about the driver.</summary>
    public string? Comment { get; set; }

    public virtual UserAccount User { get; set; } = null!;
    public virtual UserAccount RatedByUser { get; set; } = null!;
    public virtual PartnerTransaction PartnerTransaction { get; set; } = null!;
}

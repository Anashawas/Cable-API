using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// A published terms &amp; conditions / policy document. Content lives in the DB
/// (Arabic + English). RoleId scopes the policy to one role; null = all roles.
/// Exactly one active version per role scope (filtered unique index).
/// </summary>
public class TermsVersion : BaseAuditableEntity
{
    /// <summary>App/system version this policy ships with (e.g. "2.2.2"). Display only — acceptance anchors to Id.</summary>
    public string SystemVersion { get; set; } = null!;

    /// <summary>Target role (Role table); null = applies to all roles.</summary>
    public int? RoleId { get; set; }

    public string ContentEn { get; set; } = null!;
    public string ContentAr { get; set; } = null!;

    public DateTime EffectiveFrom { get; set; }
    public bool IsActive { get; set; }

    public virtual Role? Role { get; set; }
    public virtual ICollection<UserTermsAcceptance> Acceptances { get; set; } = new List<UserTermsAcceptance>();
}

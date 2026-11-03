using Domain.Common;
namespace Domain.Enitites;

/// <summary>
/// The partner-app password of a worker. One <see cref="UserAccount"/> per phone holds everything (driver data,
/// email login…); this row is the worker's second credential, used only by the partner app's phone login.
/// The driver password (<see cref="UserAccount.Password"/>) is never read or written through it.
/// Owners have no row: they keep the email + password + OTP login.
/// </summary>
public class PartnerCredential : BaseAuditableEntity
{
    public int UserId { get; set; }

    public string PasswordHash { get; set; } = null!;

    /// <summary>True until the worker replaces the temporary password the owner shared with them.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>UTC. Set while the stored password is a temporary one (72 h, first login only); null otherwise.</summary>
    public DateTime? TempPasswordExpiresAt { get; set; }

    /// <summary>UTC. null = "waiting for the first login" on the owner's worker card.</summary>
    public DateTime? FirstLoginAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public DateTime? PasswordChangedAt { get; set; }

    /// <summary>Wrong password attempts since the last success; the login locks after the configured count.</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>UTC. The login is refused until then (423 LOCKED).</summary>
    public DateTime? LockedUntil { get; set; }

    public virtual UserAccount User { get; set; } = null!;
}

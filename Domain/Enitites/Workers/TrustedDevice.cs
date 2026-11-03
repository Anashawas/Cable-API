using Domain.Common;
namespace Domain.Enitites;

/// <summary>
/// A device that completed a worker OTP login. The server issues a random token after the OTP; the app sends it
/// back on later logins to skip the OTP on the same device. Only the hash is stored. <see cref="DeviceId"/> alone
/// is not trusted because the app chooses it.
/// </summary>
public class TrustedDevice : BaseAuditableEntity
{
    public int UserId { get; set; }

    /// <summary>AuthApps.Provider or AuthApps.ProviderWeb — the surface the token was issued to.</summary>
    public string App { get; set; } = null!;

    public string DeviceId { get; set; } = null!;

    /// <summary>SHA-256 of the token the app holds.</summary>
    public string TokenHash { get; set; } = null!;

    public DateTime LastUsedAt { get; set; }

    /// <summary>UTC. After this the next login needs an OTP again.</summary>
    public DateTime ExpiresAt { get; set; }

    public virtual UserAccount User { get; set; } = null!;
}

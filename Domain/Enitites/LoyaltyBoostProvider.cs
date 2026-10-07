using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// Targets a <see cref="LoyaltyBoost"/> at one provider. Only consulted when the
/// boost does not apply to all providers.
/// </summary>
public class LoyaltyBoostProvider : BaseAuditableEntity
{
    public int LoyaltyBoostId { get; set; }

    /// <summary>"ChargingPoint" or "ServiceProvider".</summary>
    public string ProviderType { get; set; } = null!;

    public int ProviderId { get; set; }

    public virtual LoyaltyBoost LoyaltyBoost { get; set; } = null!;
}

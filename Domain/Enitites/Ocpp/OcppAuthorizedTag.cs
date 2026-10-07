using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// A card / password the station allows to charge. Authorize and StartTransaction
/// answer Accepted only for tags on this list, so taking over a live station from
/// its previous platform does not turn it into free charging for any RFID card.
/// Scoped per station; the same tag may exist at several stations.
/// </summary>
public class OcppAuthorizedTag : BaseAuditableEntity
{
    public int ChargingPointId { get; set; }

    /// <summary>Normalised (trimmed, upper-case). OCPP limits idTag to 20 characters.</summary>
    public string IdTag { get; set; } = null!;

    public string? Label { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }

    public virtual ChargingPoint ChargingPoint { get; set; } = null!;
}
